using System;

namespace AvaloniaCadEditor.Domain.ValueObjects;

public class WorkPlane : IEquatable<WorkPlane>
{
    public Vector3D Normal { get; }
    public Point3D Origin { get; }
    public Vector3D XAxis { get; }
    public Vector3D YAxis { get; }

    public WorkPlane(Vector3D normal, Point3D origin)
    {
        Normal = normal.Normalize();
        Origin = origin;

        // Calculate a consistent orthonormal basis
        // If Normal is parallel to Z-axis
        if (Math.Abs(Math.Abs(Normal.Z) - 1.0) < 1e-6)
        {
            XAxis = new Vector3D(1, 0, 0);
        }
        else
        {
            XAxis = new Vector3D(0, 0, 1).CrossProduct(Normal).Normalize();
        }

        YAxis = Normal.CrossProduct(XAxis).Normalize();
    }

    public WorkPlane(Point3D origin, Vector3D normal, Vector3D xAxis)
    {
        Origin = origin;
        Normal = normal.Normalize();
        XAxis = xAxis.Normalize();
        YAxis = Normal.CrossProduct(XAxis).Normalize();
    }

    /// <summary>
    /// Default XY Plane (Z=0) used as the floor.
    /// </summary>
    public static WorkPlane XY => new WorkPlane(new Vector3D(0, 0, 1), Point3D.Origin);

    public Point3D ToWorldSpace(Point3D localPoint)
    {
        var worldVector = (XAxis * localPoint.X) + (YAxis * localPoint.Y) + (Normal * localPoint.Z);
        return Origin + worldVector;
    }

    public Point3D ToLocalSpace(Point3D worldPoint)
    {
        var d = worldPoint - Origin;
        var u = d.DotProduct(XAxis);
        var v = d.DotProduct(YAxis);
        var w = d.DotProduct(Normal);
        return new Point3D(u, v, w);
    }

    public bool Equals(WorkPlane? other)
    {
        if (other is null) return false;
        return Normal.Equals(other.Normal) && Origin.Equals(other.Origin) && XAxis.Equals(other.XAxis);
    }

    public override bool Equals(object? obj) => Equals(obj as WorkPlane);

    public override int GetHashCode() => HashCode.Combine(Normal, Origin, XAxis);
}
