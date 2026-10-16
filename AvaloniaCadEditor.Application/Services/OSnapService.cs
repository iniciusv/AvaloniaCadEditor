using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Mesh;
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

    public Point3D GetSnappedPoint(Sketch sketch, Point3D cursor, Point3D? referencePoint, AvaloniaCadEditor.Domain.Mesh.TriangleMesh? solidMesh = null, bool solidSnapOnPlaneOnly = false, IReadOnlyList<PlaneReference>? planeReferences = null)
    {
        if (!IsEnabled) return ApplyPolarAndGridSnap(sketch, cursor, referencePoint);

        Point3D? bestSnap = null;
        double minDistance = Settings.SnapTolerance;

        // Snap to WorkPlane Origin
        if (Settings.EndpointEnabled)
        {
            var originDist = cursor.DistanceTo(sketch.Plane.Origin);
            if (originDist < minDistance)
            {
                minDistance = originDist;
                bestSnap = sketch.Plane.Origin;
            }
        }

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

        if (solidSnapOnPlaneOnly)
        {
            var references = planeReferences
                ?? (solidMesh != null ? PlaneSection.Compute(solidMesh, sketch.Plane) : null);
            if (references != null)
            {
                foreach (var pt in PlaneReferenceSnapPoints(references, sketch.Plane))
                {
                    var dist = cursor.DistanceTo(pt);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestSnap = pt;
                    }
                }
            }
        }
        else if (solidMesh != null && solidMesh.Positions != null && Settings.EndpointEnabled)
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

        return ApplyPolarAndGridSnap(sketch, cursor, referencePoint);
    }

    /// <summary>
    /// Pontos de snap das referências do plano de trabalho (<see cref="PlaneSection"/>): o contorno das faces
    /// que estão no plano — inclusive bocas de furo — e o corte dos sólidos que o atravessam, nunca vértices
    /// de outra altura. Polilinha comum dá seus cantos e o ponto médio de cada trecho reto; laço circular dá
    /// o centro e os quatro quadrantes, e não os vértices da tesselação.
    /// </summary>
    private IEnumerable<Point3D> PlaneReferenceSnapPoints(IReadOnlyList<PlaneReference> references, WorkPlane plane)
    {
        foreach (var reference in references)
        {
            if (reference.IsCircle)
            {
                var c = reference.Center!.Value;
                var r = reference.Radius!.Value;
                if (Settings.CenterEnabled) yield return plane.ToWorldSpace(c);
                if (Settings.EndpointEnabled)
                {
                    yield return plane.ToWorldSpace(new Point3D(c.X + r, c.Y, 0));
                    yield return plane.ToWorldSpace(new Point3D(c.X - r, c.Y, 0));
                    yield return plane.ToWorldSpace(new Point3D(c.X, c.Y + r, 0));
                    yield return plane.ToWorldSpace(new Point3D(c.X, c.Y - r, 0));
                }
                continue;
            }

            var pts = reference.Points;
            if (Settings.EndpointEnabled)
            {
                foreach (var p in pts) yield return plane.ToWorldSpace(p);
            }
            if (Settings.MidpointEnabled)
            {
                int segments = reference.Closed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < segments; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    yield return plane.ToWorldSpace(new Point3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0));
                }
            }
        }
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

    private Point3D ApplyPolarAndGridSnap(Sketch sketch, Point3D cursor, Point3D? referencePoint)
    {
        var localCursor = sketch.Plane.ToLocalSpace(cursor);
        var localRef = referencePoint.HasValue ? sketch.Plane.ToLocalSpace(referencePoint.Value) : (Point3D?)null;

        double finalLocalX = localCursor.X;
        double finalLocalY = localCursor.Y;
        bool changed = false;

        // Cartesian Grid Snap
        if (Settings.GridSnapEnabled && Settings.GridIncrement > 0.001)
        {
            // Grid is relative to the origin of the WorkPlane. So we round the local X and Y.
            finalLocalX = Math.Round(localCursor.X / Settings.GridIncrement) * Settings.GridIncrement;
            finalLocalY = Math.Round(localCursor.Y / Settings.GridIncrement) * Settings.GridIncrement;
            changed = true;
        }

        // Polar Snap
        if (Settings.PolarSnapEnabled && localRef.HasValue)
        {
            var dx = localCursor.X - localRef.Value.X;
            var dy = localCursor.Y - localRef.Value.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var angle = Math.Atan2(dy, dx);

            var angleIncRad = Settings.PolarAngleIncrement * Math.PI / 180.0;
            if (angleIncRad > 0.001 && distance > 0.001)
            {
                var roundedAngle = Math.Round(angle / angleIncRad) * angleIncRad;
                
                var diff = Math.Abs(angle - roundedAngle);
                if (diff > Math.PI) diff = 2 * Math.PI - diff;
                
                if (diff < (5.0 * Math.PI / 180.0))
                {
                    // Snap to the angle. Distance can still be restricted to Grid increments along the polar line.
                    if (Settings.GridSnapEnabled && Settings.GridIncrement > 0.001)
                    {
                        distance = Math.Round(distance / Settings.GridIncrement) * Settings.GridIncrement;
                    }
                    
                    finalLocalX = localRef.Value.X + distance * Math.Cos(roundedAngle);
                    finalLocalY = localRef.Value.Y + distance * Math.Sin(roundedAngle);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            return sketch.Plane.ToWorldSpace(new Point3D(finalLocalX, finalLocalY, localCursor.Z));
        }

        return cursor;
    }
}
