using System;

namespace AvaloniaCadEditor.Domain.ValueObjects;

public readonly struct RaycastHit
{
    public float Distance { get; }
    public Point3D Point { get; }
    public Vector3D Normal { get; }
    public Guid? FeatureId { get; }

    /// <summary>Triângulo atingido na malha da feature, ou <see cref="NoTriangle"/> quando não se sabe.</summary>
    public int TriangleIndex { get; }

    public const int NoTriangle = -1;

    public RaycastHit(float distance, Point3D point, Vector3D normal, Guid? featureId = null, int triangleIndex = NoTriangle)
    {
        Distance = distance;
        Point = point;
        Normal = normal.Normalize();
        FeatureId = featureId;
        TriangleIndex = triangleIndex;
    }
}
