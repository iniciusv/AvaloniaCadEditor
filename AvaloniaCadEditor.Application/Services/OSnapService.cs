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

    public Point3D GetSnappedPoint(Sketch sketch, Point3D cursor, double snapTolerance, AvaloniaCadEditor.Domain.Mesh.TriangleMesh? solidMesh = null)
    {
        if (!IsEnabled) return cursor;

        Point3D? bestSnap = null;
        double minDistance = snapTolerance;

        foreach (var entity in sketch.Entities)
        {
            var snapPoints = _geometryEngine.GetSnapPoints(entity);
            foreach (var localPt in snapPoints)
            {
                var pt = entity.Plane.ToWorldSpace(localPt);
                var dist = cursor.DistanceTo(pt);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestSnap = pt;
                }
            }
        }

        if (solidMesh != null && solidMesh.Positions != null)
        {
            // Positions are in [x,y,z] flat array
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

        return bestSnap ?? cursor;
    }
}
