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

    public Point3D GetSnappedPoint(Sketch sketch, Point3D cursor, Point3D? referencePoint, AvaloniaCadEditor.Domain.Mesh.TriangleMesh? solidMesh = null, bool solidSnapOnPlaneOnly = false)
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
            if (solidMesh != null)
            {
                foreach (var pt in SolidSnapPointsOnPlane(solidMesh, sketch.Plane))
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
    /// Pontos de snap do sólido que ficam no plano de trabalho: as pontas e os pontos médios das arestas
    /// de contorno (<c>FeatureEdges</c>) com as duas pontas no plano. É o mesmo critério de "face" do
    /// realce de seleção e do chanfro por face (<see cref="WorkPlane.Contains"/>), então ao desenhar
    /// sobre uma face o snap gruda no contorno dela — inclusive bocas de furo — e nunca em outra altura.
    /// </summary>
    private IEnumerable<Point3D> SolidSnapPointsOnPlane(AvaloniaCadEditor.Domain.Mesh.TriangleMesh mesh, WorkPlane plane)
    {
        if (mesh.Positions == null || mesh.FeatureEdges == null) yield break;

        for (int i = 0; i + 1 < mesh.FeatureEdges.Length; i += 2)
        {
            var a = Vertex(mesh, mesh.FeatureEdges[i]);
            var b = Vertex(mesh, mesh.FeatureEdges[i + 1]);
            if (!plane.Contains(a) || !plane.Contains(b)) continue;

            if (Settings.EndpointEnabled)
            {
                yield return a;
                yield return b;
            }
            if (Settings.MidpointEnabled)
            {
                yield return new Point3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
            }
        }
    }

    private static Point3D Vertex(AvaloniaCadEditor.Domain.Mesh.TriangleMesh mesh, uint index) =>
        new(mesh.Positions[index * 3], mesh.Positions[index * 3 + 1], mesh.Positions[index * 3 + 2]);

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
