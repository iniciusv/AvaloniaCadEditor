using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class GeometricInferenceService : IGeometricInferenceService
{
    public VertexReference? InferVertexReference(Sketch sketch, Point3D worldPoint, double tolerance = 1e-3)
    {
        if (sketch?.Features == null) return null;

        foreach (var feature in sketch.Features.Where(f => f.IsEnabled && f.IsVisible))
        {
            var vertices = feature.GetVertices();
            for (int i = 0; i < vertices.Count; i++)
            {
                if (vertices[i].DistanceTo(worldPoint) <= tolerance)
                {
                    return feature.FindVertex(i);
                }
            }
        }

        return null;
    }

    public LinkedWorkPlane? InferLinkedPlane(Sketch sketch, WorkPlane plane, double tolerance = 1e-3)
    {
        if (sketch?.Features == null || plane is LinkedWorkPlane linked)
            return plane as LinkedWorkPlane;

        var normal = plane.Normal;
        var origin = plane.Origin;

        foreach (var feature in sketch.Features.Where(f => f.IsEnabled && f.IsVisible))
        {
            if (feature is BoxFeature box)
            {
                var faceType = box.GetFaceTypeFromNormal(normal);
                if (faceType.HasValue)
                {
                    var faceCenter = box.GetFaceCenter(faceType.Value);
                    var faceNormal = box.GetFaceNormal(faceType.Value);

                    // Check if origin lies on the plane of the face
                    var diff = origin - faceCenter;
                    if (Math.Abs(diff.DotProduct(faceNormal)) <= tolerance)
                    {
                        var faceRef = box.FindFace((int)faceType.Value, faceNormal);
                        return new LinkedWorkPlane(faceRef);
                    }
                }
            }
            else if (feature is CylinderFeature cyl)
            {
                var faceType = cyl.GetFaceTypeFromNormal(normal);
                if (faceType.HasValue)
                {
                    var faceCenter = cyl.GetFaceCenter(faceType.Value);
                    var faceNormal = cyl.GetFaceNormal(faceType.Value);

                    var diff = origin - faceCenter;
                    if (Math.Abs(diff.DotProduct(faceNormal)) <= tolerance)
                    {
                        var faceRef = cyl.FindFace((int)faceType.Value, faceNormal);
                        return new LinkedWorkPlane(faceRef);
                    }
                }
            }
        }

        return null;
    }

    public LinkedWorkPlane? InferLinkedPlaneFromPoints(Sketch sketch, IEnumerable<Point3D> worldPoints, double tolerance = 1e-3)
    {
        var pts = worldPoints?.ToList();
        if (pts == null || pts.Count < 3 || sketch?.Features == null)
            return null;

        // Calculate normal from first 3 non-collinear points
        var v1 = pts[1] - pts[0];
        var v2 = pts[2] - pts[0];
        var cross = v1.CrossProduct(v2);
        if (cross.Length <= 1e-6) return null;

        var normal = cross.Normalize();
        var tempPlane = new WorkPlane(pts[0], normal, v1.Normalize());

        return InferLinkedPlane(sketch, tempPlane, tolerance);
    }
}
