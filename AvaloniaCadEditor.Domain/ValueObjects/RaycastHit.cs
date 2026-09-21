using System;

namespace AvaloniaCadEditor.Domain.ValueObjects;

public readonly struct RaycastHit
{
    public float Distance { get; }
    public Point3D Point { get; }
    public Vector3D Normal { get; }
    public Guid? FeatureId { get; }

    public RaycastHit(float distance, Point3D point, Vector3D normal, Guid? featureId = null)
    {
        Distance = distance;
        Point = point;
        Normal = normal.Normalize();
        FeatureId = featureId;
    }
}
