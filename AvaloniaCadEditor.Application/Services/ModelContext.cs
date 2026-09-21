using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class ModelContext : IModelContext
{
    private readonly Sketch _sketch;

    public ModelContext(Sketch sketch)
    {
        _sketch = sketch;
    }

    public CircleProfile CircleProfile(Point3D center, double radius, Vector3D? normal = null)
    {
        var profile = new CircleProfile(center, radius, normal);
        _sketch.AddProfile(profile);
        return profile;
    }

    public RectangleProfile RectangleProfile(Point3D center, double width, double height, double rotationAngle = 0, Vector3D? normal = null)
    {
        var profile = new RectangleProfile(center, width, height, rotationAngle, normal);
        _sketch.AddProfile(profile);
        return profile;
    }

    public PolygonProfile PolygonProfile(IEnumerable<Point3D> vertices, Vector3D? normal = null)
    {
        var profile = new PolygonProfile(vertices, normal: normal);
        _sketch.AddProfile(profile);
        return profile;
    }

    public ExtrudeFeature Extrude(IProfile profile, double depth, Vector3D? direction = null, string? name = null)
    {
        var feature = new ExtrudeFeature(profile, depth, direction, name);
        _sketch.AddFeature(feature);
        return feature;
    }

    public BooleanFeature Boolean(BooleanOperationType operation, IFeature baseFeature, IFeature[] toolFeatures, string? name = null)
    {
        var feature = new BooleanFeature(
            name ?? $"Boolean {operation}",
            operation, baseFeature, toolFeatures);
        _sketch.AddFeature(feature);
        return feature;
    }
}
