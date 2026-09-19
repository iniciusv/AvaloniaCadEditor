using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class OSnapService : IOSnapService
{
    private readonly IGeometryEngine _geometryEngine;

    public bool IsEnabled { get; set; } = true;

    public OSnapService(IGeometryEngine geometryEngine)
    {
        _geometryEngine = geometryEngine;
    }

    public Point3D GetSnappedPoint(Sketch sketch, Point3D cursor, double snapTolerance)
    {
        if (!IsEnabled) return cursor;

        Point3D? bestSnap = null;
        double minDistance = snapTolerance;

        foreach (var entity in sketch.Entities)
        {
            var snapPoints = _geometryEngine.GetSnapPoints(entity);
            foreach (var pt in snapPoints)
            {
                var dist = cursor.DistanceTo(pt);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestSnap = pt;
                }
            }
        }

        return bestSnap ?? cursor;
    }
}
