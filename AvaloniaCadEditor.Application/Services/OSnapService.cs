using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class OSnapService : IOSnapService
{
    private readonly IGeometryEngine _geometryEngine;

    public bool IsEnabled { get; set; } = true;
    public OsnapSettings Settings { get; } = new OsnapSettings();

    public OSnapService(IGeometryEngine geometryEngine)
    {
        _geometryEngine = geometryEngine;
    }

    public Point3D GetSnappedPoint(Sketch sketch, Point3D cursor, Point3D? referencePoint, AvaloniaCadEditor.Domain.Mesh.TriangleMesh? solidMesh = null)
    {
        if (!IsEnabled) return ApplyPolarAndGridSnap(cursor, referencePoint);

        Point3D? bestSnap = null;
        double minDistance = Settings.SnapTolerance;

        foreach (var entity in sketch.Entities)
        {
            var snapPoints = _geometryEngine.GetSnapPoints(entity, referencePoint);
            foreach (var localPt in snapPoints)
            {
                if (!IsSnapTypeEnabled(localPt.Type)) continue;

                var pt = entity.Plane.ToWorldSpace(localPt.Point);
                var dist = cursor.DistanceTo(pt);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestSnap = pt;
                }
            }
        }

        if (solidMesh != null && solidMesh.Positions != null && Settings.EndpointEnabled)
        {
            for (int i = 0; i < solidMesh.Positions.Length; i += 3)
            {
                var pt = new Point3D(
                    solidMesh.Positions[i],
                    solidMesh.Positions[i+1],
                    solidMesh.Positions[i+2]);
                    
                var dist = cursor.DistanceTo(pt);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestSnap = pt;
                }
            }
        }

        if (bestSnap != null) return bestSnap.Value;

        return ApplyPolarAndGridSnap(cursor, referencePoint);
    }

    private bool IsSnapTypeEnabled(SnapPointType type)
    {
        return type switch
        {
            SnapPointType.Endpoint => Settings.EndpointEnabled,
            SnapPointType.Midpoint => Settings.MidpointEnabled,
            SnapPointType.Center => Settings.CenterEnabled,
            SnapPointType.Tangent => Settings.TangentEnabled,
            _ => false
        };
    }

    private Point3D ApplyPolarAndGridSnap(Point3D cursor, Point3D? referencePoint)
    {
        if (referencePoint == null) return cursor;

        var refPt = referencePoint.Value;
        var dx = cursor.X - refPt.X;
        var dy = cursor.Y - refPt.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Atan2(dy, dx);

        if (distance < 0.001) return cursor;

        bool changed = false;

        if (Settings.PolarSnapEnabled)
        {
            var angleIncRad = Settings.PolarAngleIncrement * Math.PI / 180.0;
            if (angleIncRad > 0.001)
            {
                var roundedAngle = Math.Round(angle / angleIncRad) * angleIncRad;
                
                // Snap to angle if we are close to it (e.g., within 5 degrees)
                var diff = Math.Abs(angle - roundedAngle);
                if (diff > Math.PI) diff = 2 * Math.PI - diff;
                
                if (diff < (5.0 * Math.PI / 180.0))
                {
                    angle = roundedAngle;
                    changed = true;
                }
            }
        }

        if (Settings.GridSnapEnabled && Settings.GridIncrement > 0.001)
        {
            distance = Math.Round(distance / Settings.GridIncrement) * Settings.GridIncrement;
            changed = true;
        }

        if (changed)
        {
            return new Point3D(
                refPt.X + distance * Math.Cos(angle),
                refPt.Y + distance * Math.Sin(angle),
                cursor.Z // Preserve Z if needed, assuming 2D plane for polar/grid
            );
        }

        return cursor;
    }
}
