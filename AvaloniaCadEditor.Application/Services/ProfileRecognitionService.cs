using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.Profiles.Classifiers;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class ProfileRecognitionService : IProfileRecognitionService
{
    private readonly List<IShapeClassifier> _classifiers;
    private const double Tolerance = 1e-4;

    public ProfileRecognitionService(IEnumerable<IShapeClassifier>? classifiers = null)
    {
        _classifiers = classifiers?.ToList() ?? new List<IShapeClassifier>
        {
            new RectangleClassifier(),
            new PolygonClassifier()
        };
    }

    public IReadOnlyList<IProfile> RecognizeProfiles(Sketch sketch)
    {
        var recognizedProfiles = new List<IProfile>();

        // 1. Recognize standalone circles
        foreach (var circle in sketch.Entities.OfType<CircleEntity>().Where(e => e.IsVisible))
        {
            var localCenter = circle.Plane.ToLocalSpace(circle.Center);
            
            var circleCandidate = new ProfileCandidate(
                ProfileClassification.Circle,
                "Círculo (Paramétrico)",
                specificityRank: 1,
                factory: () => new CircleProfile(localCenter, circle.Radius, sourceEntityIds: new[] { circle.Id }, plane: circle.Plane)
            );

            var polyCandidate = new ProfileCandidate(
                ProfileClassification.Polygon,
                "Polígono Aproximado",
                specificityRank: 100,
                factory: () => new PolygonProfile(new CircleProfile(localCenter, circle.Radius, plane: circle.Plane).GetVertices(), new[] { circle.Id }, plane: circle.Plane)
            );

            var profile = new CircleProfile(localCenter, circle.Radius, sourceEntityIds: new[] { circle.Id }, plane: circle.Plane)
            {
                AvailableCandidates = new List<ProfileCandidate> { circleCandidate, polyCandidate }
            };

            recognizedProfiles.Add(profile);
        }

        // 2. Recognize closed loops from lines
        var lineEntities = sketch.Entities.OfType<LineEntity>().Where(e => e.IsVisible).ToList();
        var linesByPlane = lineEntities.GroupBy(e => e.Plane).ToList();
        
        foreach (var group in linesByPlane)
        {
            var planeLines = group.ToList();
            if (planeLines.Count >= 3)
            {
                var loops = FindClosedLoops(planeLines);

                foreach (var loop in loops)
            {
                var candidates = new List<ProfileCandidate>();

                foreach (var classifier in _classifiers)
                {
                    var candidate = classifier.Classify(loop.Vertices, loop.SourceEntityIds, loop.Plane);
                    if (candidate != null)
                    {
                        candidates.Add(candidate);
                    }
                }

                if (candidates.Count > 0)
                {
                    // Sort by specificity (simpler / more specific first)
                    var sortedCandidates = candidates.OrderBy(c => c.SpecificityRank).ToList();
                    
                    // Create default profile from the top candidate
                    var bestCandidate = sortedCandidates.First();
                    var profile = bestCandidate.Factory();
                    profile.AvailableCandidates = sortedCandidates;

                    recognizedProfiles.Add(profile);
                }
            }
        }
    }

    return recognizedProfiles;
    }

    private class LoopData
    {
        public List<Point3D> Vertices { get; set; } = new();
        public List<Guid> SourceEntityIds { get; set; } = new();
        public WorkPlane Plane { get; set; } = WorkPlane.XY;
    }

    private class GraphNode
    {
        public int Id { get; set; }
        public Point3D Point { get; set; }
        public List<GraphEdge> Edges { get; set; } = new();
    }

    private class GraphEdge
    {
        public GraphNode Target { get; set; } = null!;
        public Guid EntityId { get; set; }
    }

    private List<LoopData> FindClosedLoops(List<LineEntity> lines)
    {
        var nodes = new List<GraphNode>();

        GraphNode GetOrCreateNode(Point3D point)
        {
            foreach (var node in nodes)
            {
                if (node.Point.DistanceTo(point) <= Tolerance)
                    return node;
            }
            var newNode = new GraphNode { Id = nodes.Count, Point = point };
            nodes.Add(newNode);
            return newNode;
        }

        foreach (var line in lines)
        {
            var n1 = GetOrCreateNode(line.StartPoint);
            var n2 = GetOrCreateNode(line.EndPoint);

            if (n1.Id != n2.Id)
            {
                n1.Edges.Add(new GraphEdge { Target = n2, EntityId = line.Id });
                n2.Edges.Add(new GraphEdge { Target = n1, EntityId = line.Id });
            }
        }

        // Find all simple cycles using DFS
        var foundCycles = new List<List<GraphNode>>();
        var foundEdgeSets = new List<List<Guid>>();

        void Dfs(GraphNode startNode, GraphNode currentNode, GraphNode? parentNode, List<GraphNode> path, List<Guid> edgePath)
        {
            path.Add(currentNode);

            foreach (var edge in currentNode.Edges)
            {
                var neighbor = edge.Target;
                if (neighbor == parentNode)
                    continue;

                if (neighbor == startNode && path.Count >= 3)
                {
                    // Found a cycle
                    var cycleNodes = new List<GraphNode>(path);
                    var cycleEdges = new List<Guid>(edgePath) { edge.EntityId };
                    foundCycles.Add(cycleNodes);
                    foundEdgeSets.Add(cycleEdges);
                }
                else if (!path.Contains(neighbor) && path.Count < 20) // bounded search
                {
                    var newEdgePath = new List<Guid>(edgePath) { edge.EntityId };
                    Dfs(startNode, neighbor, currentNode, path, newEdgePath);
                }
            }

            path.RemoveAt(path.Count - 1);
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            var path = new List<GraphNode>();
            var edgePath = new List<Guid>();
            Dfs(nodes[i], nodes[i], null, path, edgePath);
        }

        // Deduplicate canonical cycles
        var uniqueLoops = new List<LoopData>();
        var seenCycleKeys = new HashSet<string>();

        for (int i = 0; i < foundCycles.Count; i++)
        {
            var cycle = foundCycles[i];
            var edges = foundEdgeSets[i];

            var nodeIds = cycle.Select(n => n.Id).ToList();
            var key = GetCanonicalCycleKey(nodeIds);

            if (!seenCycleKeys.Contains(key))
            {
                seenCycleKeys.Add(key);
                var firstLine = lines.First(l => l.Id == edges.First());
                uniqueLoops.Add(new LoopData
                {
                    Vertices = cycle.Select(n => n.Point).ToList(),
                    SourceEntityIds = edges.Distinct().ToList(),
                    Plane = firstLine.Plane
                });
            }
        }

        return uniqueLoops;
    }

    private static string GetCanonicalCycleKey(List<int> cycle)
    {
        if (cycle.Count == 0) return "";

        // Find min element and representations in forward and reverse order
        int minIndex = 0;
        for (int i = 1; i < cycle.Count; i++)
        {
            if (cycle[i] < cycle[minIndex]) minIndex = i;
        }

        var forward = new List<int>();
        for (int i = 0; i < cycle.Count; i++)
        {
            forward.Add(cycle[(minIndex + i) % cycle.Count]);
        }

        var reverse = new List<int>();
        for (int i = 0; i < cycle.Count; i++)
        {
            reverse.Add(cycle[(minIndex - i + cycle.Count) % cycle.Count]);
        }

        string fStr = string.Join(",", forward);
        string rStr = string.Join(",", reverse);
        return string.CompareOrdinal(fStr, rStr) < 0 ? fStr : rStr;
    }
}
