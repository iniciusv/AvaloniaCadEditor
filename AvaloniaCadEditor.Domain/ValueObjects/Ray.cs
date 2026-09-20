using System;
using System.Numerics;

namespace AvaloniaCadEditor.Domain.ValueObjects;

public readonly struct Ray
{
    public Vector3 Origin { get; }
    public Vector3 Direction { get; }

    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = Vector3.Normalize(direction);
    }

    /// <summary>
    /// Intersects the ray with a mathematical plane and returns the exact 3D point of intersection.
    /// Returns null if the ray is parallel to the plane.
    /// </summary>
    public Point3D? IntersectPlane(Vector3 planeNormal, Vector3 planePoint)
    {
        float denominator = Vector3.Dot(planeNormal, Direction);
        
        // Check if ray is parallel to the plane
        if (Math.Abs(denominator) < 1e-6)
            return null;

        float t = Vector3.Dot(planePoint - Origin, planeNormal) / denominator;
        
        // Check if intersection is behind the ray origin
        if (t < 0)
            return null;

        Vector3 intersection = Origin + Direction * t;
        return new Point3D(intersection.X, intersection.Y, intersection.Z);
    }
}
