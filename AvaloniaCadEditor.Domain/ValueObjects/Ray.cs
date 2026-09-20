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

    /// <summary>
    /// Möller–Trumbore ray-triangle intersection algorithm.
    /// </summary>
    public bool IntersectTriangle(Vector3 v0, Vector3 v1, Vector3 v2, out float t)
    {
        t = 0;
        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector3 h = Vector3.Cross(Direction, edge2);
        float a = Vector3.Dot(edge1, h);

        if (a > -1e-6f && a < 1e-6f)
            return false; // Ray is parallel to triangle

        float f = 1.0f / a;
        Vector3 s = Origin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(Direction, q);

        if (v < 0.0f || u + v > 1.0f)
            return false;

        t = f * Vector3.Dot(edge2, q);
        return t > 1e-4f;
    }

    /// <summary>
    /// Tests ray intersection against a 3D TriangleMesh and returns the closest hit distance.
    /// </summary>
    public bool IntersectMesh(AvaloniaCadEditor.Domain.Mesh.TriangleMesh mesh, out float minDistance)
    {
        minDistance = float.MaxValue;
        if (mesh == null || mesh.TriangleCount == 0) return false;

        bool hit = false;
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            uint i0 = mesh.Indices[i];
            uint i1 = mesh.Indices[i + 1];
            uint i2 = mesh.Indices[i + 2];

            Vector3 v0 = new Vector3(mesh.Positions[i0 * 3], mesh.Positions[i0 * 3 + 1], mesh.Positions[i0 * 3 + 2]);
            Vector3 v1 = new Vector3(mesh.Positions[i1 * 3], mesh.Positions[i1 * 3 + 1], mesh.Positions[i1 * 3 + 2]);
            Vector3 v2 = new Vector3(mesh.Positions[i2 * 3], mesh.Positions[i2 * 3 + 1], mesh.Positions[i2 * 3 + 2]);

            if (IntersectTriangle(v0, v1, v2, out float t))
            {
                if (t < minDistance)
                {
                    minDistance = t;
                    hit = true;
                }
            }
        }
        return hit;
    }
}
