using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Services;

public class PlanarTopologyService : IPlanarTopologyService
{
    private readonly IGeometryEngine _geometryEngine;

    public PlanarTopologyService(IGeometryEngine geometryEngine)
    {
        _geometryEngine = geometryEngine;
    }

    public IEnumerable<Entity> ComputePlanarArrangement(IEnumerable<Entity> entities)
    {
        var lines = entities.OfType<LineEntity>().ToList();
        var nonLines = entities.Where(e => !(e is LineEntity)).ToList();

        if (lines.Count < 2) return entities; // Nothing to split

        var splitLines = new List<LineEntity>(lines);

        bool madeChanges = true;
        while (madeChanges)
        {
            madeChanges = false;
            for (int i = 0; i < splitLines.Count; i++)
            {
                for (int j = i + 1; j < splitLines.Count; j++)
                {
                    var lineA = splitLines[i];
                    var lineB = splitLines[j];

                    var intersections = _geometryEngine.GetIntersections(lineA, lineB).ToList();
                    
                    if (intersections.Any())
                    {
                        var inter = intersections.First();
                        
                        bool splitA = IsPointInsideSegment(inter, lineA);
                        bool splitB = IsPointInsideSegment(inter, lineB);

                        if (splitA || splitB)
                        {
                            if (splitA)
                            {
                                splitLines.RemoveAt(i);
                                splitLines.Insert(i, new LineEntity(inter, lineA.EndPoint) { Plane = lineA.Plane });
                                splitLines.Insert(i, new LineEntity(lineA.StartPoint, inter) { Plane = lineA.Plane });
                                j++; // Adjust index due to insertion
                            }

                            if (splitB)
                            {
                                var indexB = splitLines.IndexOf(lineB);
                                splitLines.RemoveAt(indexB);
                                splitLines.Insert(indexB, new LineEntity(inter, lineB.EndPoint) { Plane = lineB.Plane });
                                splitLines.Insert(indexB, new LineEntity(lineB.StartPoint, inter) { Plane = lineB.Plane });
                            }

                            madeChanges = true;
                            break; // Break inner loop to restart with new segments
                        }
                    }
                }
                if (madeChanges) break; // Break outer loop to restart evaluation
            }
        }

        // Deduplicate overlapping collinear lines
        var uniqueLines = DeduplicateLines(splitLines);

        var result = new List<Entity>(uniqueLines);
        result.AddRange(nonLines);

        return result;
    }

    private bool IsPointInsideSegment(Point3D p, LineEntity line, double tolerance = 1e-4)
    {
        // Check if the point is one of the endpoints
        if (p.DistanceTo(line.StartPoint) <= tolerance || p.DistanceTo(line.EndPoint) <= tolerance)
            return false;

        var distAP = p.DistanceTo(line.StartPoint);
        var distPB = line.EndPoint.DistanceTo(p);
        var distAB = line.StartPoint.DistanceTo(line.EndPoint);

        return Math.Abs((distAP + distPB) - distAB) <= tolerance;
    }

    private IEnumerable<LineEntity> DeduplicateLines(List<LineEntity> lines)
    {
        var unique = new List<LineEntity>();
        foreach (var line in lines)
        {
            if (line.StartPoint.DistanceTo(line.EndPoint) < 1e-4) continue; // Remove zero-length lines

            bool isDuplicate = false;
            foreach (var existing in unique)
            {
                if ((line.StartPoint.DistanceTo(existing.StartPoint) < 1e-4 && line.EndPoint.DistanceTo(existing.EndPoint) < 1e-4) ||
                    (line.StartPoint.DistanceTo(existing.EndPoint) < 1e-4 && line.EndPoint.DistanceTo(existing.StartPoint) < 1e-4))
                {
                    isDuplicate = true;
                    break;
                }
            }
            if (!isDuplicate)
            {
                unique.Add(line);
            }
        }
        return unique;
    }
}
