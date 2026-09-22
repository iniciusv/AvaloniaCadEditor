using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

public class FaceProfile : IProfile
{
    public Guid Id { get; } = Guid.NewGuid();
    
    public ProfileClassification Classification => ProfileClassification.Custom;
    
    public IReadOnlyList<ProfileCandidate> AvailableCandidates { get; set; } = Array.Empty<ProfileCandidate>();
    
    public IReadOnlyList<Guid> SourceEntityIds { get; } = Array.Empty<Guid>();
    
    public WorkPlane Plane { get; set; }
    
    public Point3D Center => Plane.Origin;
    
    public Vector3D Normal => Plane.Normal;
    
    public double Area { get; set; }
    
    public bool IsSelected { get; set; }
    public bool IsVisible { get; set; } = true;

    public AvaloniaCadEditor.Domain.Features.IFeature BaseFeature { get; }
    private IReadOnlyList<Point3D> _vertices = Array.Empty<Point3D>();

    public FaceProfile(AvaloniaCadEditor.Domain.Features.IFeature baseFeature, WorkPlane plane, AvaloniaCadEditor.Domain.Mesh.TriangleMesh mesh)
    {
        BaseFeature = baseFeature;
        Plane = plane;
        ExtractBoundary(mesh);
    }

    private void ExtractBoundary(AvaloniaCadEditor.Domain.Mesh.TriangleMesh mesh)
    {
        if (mesh == null || mesh.TriangleCount == 0) return;

        var planeNormal = Plane.Normal;
        var planeOrigin = Plane.Origin;

        var faceIndices = new List<uint>();
        
        // 1. Find all triangles belonging to this face (coplanar and same normal)
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            uint i1 = mesh.Indices[i];
            uint i2 = mesh.Indices[i + 1];
            uint i3 = mesh.Indices[i + 2];

            var p1 = new Point3D(mesh.Positions[i1 * 3], mesh.Positions[i1 * 3 + 1], mesh.Positions[i1 * 3 + 2]);
            
            // Check if triangle is on the plane
            var v = new Vector3D(p1.X - planeOrigin.X, p1.Y - planeOrigin.Y, p1.Z - planeOrigin.Z);
            if (Math.Abs(v.DotProduct(planeNormal)) > 1e-3) continue;

            // Check if normal matches
            var nX = mesh.Normals[i1 * 3];
            var nY = mesh.Normals[i1 * 3 + 1];
            var nZ = mesh.Normals[i1 * 3 + 2];
            var normal = new Vector3D(nX, nY, nZ).Normalize();
            
            if (normal.DotProduct(planeNormal) > 0.99)
            {
                faceIndices.Add(i1);
                faceIndices.Add(i2);
                faceIndices.Add(i3);
            }
        }

        if (faceIndices.Count == 0) return;

        // 2. Find boundary edges while preserving winding order
        var edgeCount = new Dictionary<(uint, uint), int>();
        for (int i = 0; i < faceIndices.Count; i += 3)
        {
            uint i1 = faceIndices[i], i2 = faceIndices[i + 1], i3 = faceIndices[i + 2];
            CountDirectedEdge(edgeCount, i1, i2);
            CountDirectedEdge(edgeCount, i2, i3);
            CountDirectedEdge(edgeCount, i3, i1);
        }

        var boundaryEdges = new List<(uint a, uint b)>();
        foreach (var kvp in edgeCount)
        {
            // A boundary edge only appears once in its specific direction
            // If the opposite direction also exists, it's an internal edge
            var opposite = (kvp.Key.Item2, kvp.Key.Item1);
            if (!edgeCount.ContainsKey(opposite))
            {
                // KEEP the original direction to maintain Counter-Clockwise (CCW) winding order.
                // CCW wire -> Outward face normal in OpenCascade -> Correct Sweep Solid orientation.
                boundaryEdges.Add(kvp.Key);
            }
        }

        if (boundaryEdges.Count == 0) return;

        // 3. Chain edges into a loop, strictly following the directed sequence
        var orderedVertices = new List<Point3D>();
        var currentEdge = boundaryEdges[0];
        uint startNode = currentEdge.a;
        uint currentNode = currentEdge.b;
        boundaryEdges.RemoveAt(0);

        orderedVertices.Add(GetPoint(mesh, startNode));

        while (boundaryEdges.Count > 0)
        {
            orderedVertices.Add(GetPoint(mesh, currentNode));
            
            // Find the next edge that starts where the current one ended
            int nextIdx = boundaryEdges.FindIndex(e => e.a == currentNode);
            if (nextIdx == -1) break; // Open loop or hole

            var nextEdge = boundaryEdges[nextIdx];
            boundaryEdges.RemoveAt(nextIdx);
            
            currentNode = nextEdge.b;
        }

        _vertices = orderedVertices;
        
        // Calculate Area roughly
        Area = 0;
        if (_vertices.Count > 2)
        {
            for (int i = 0; i < _vertices.Count; i++)
            {
                var p1 = _vertices[i];
                var p2 = _vertices[(i + 1) % _vertices.Count];
                var v1 = Plane.ToLocalSpace(p1);
                var v2 = Plane.ToLocalSpace(p2);
                Area += (v1.X * v2.Y - v2.X * v1.Y);
            }
            Area = Math.Abs(Area / 2.0);
        }
    }

    private void CountDirectedEdge(Dictionary<(uint, uint), int> dict, uint a, uint b)
    {
        var key = (a, b);
        if (dict.ContainsKey(key)) dict[key]++;
        else dict[key] = 1;
    }

    private Point3D GetPoint(AvaloniaCadEditor.Domain.Mesh.TriangleMesh mesh, uint idx)
    {
        return new Point3D(mesh.Positions[idx * 3], mesh.Positions[idx * 3 + 1], mesh.Positions[idx * 3 + 2]);
    }

    public IReadOnlyList<Point3D> GetVertices(int segments = 32)
    {
        return _vertices;
    }

    public (IReadOnlyList<Point3D> Vertices, IReadOnlyList<uint> Indices) GetTessellatedFace(int segments = 36)
    {
        return (_vertices, Array.Empty<uint>());
    }

    public bool ContainsPoint(Point3D point)
    {
        return false;
    }

    public IProfile SwitchClassification(ProfileClassification targetClassification)
    {
        return this;
    }
}
