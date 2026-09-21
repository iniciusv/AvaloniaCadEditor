using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Interfaces;

public interface IModelContext
{
    CircleProfile CircleProfile(Point3D center, double radius, Vector3D? normal = null);

    RectangleProfile RectangleProfile(Point3D center, double width, double height, double rotationAngle = 0, Vector3D? normal = null);

    PolygonProfile PolygonProfile(IEnumerable<Point3D> vertices, Vector3D? normal = null);

    ExtrudeFeature Extrude(IProfile profile, double depth, Vector3D? direction = null, string? name = null);

    BooleanFeature Boolean(BooleanOperationType operation, IFeature baseFeature, IFeature[] toolFeatures, string? name = null);
}
