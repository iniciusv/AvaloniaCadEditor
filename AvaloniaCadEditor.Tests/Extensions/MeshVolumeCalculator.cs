using System;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Tests.Extensions;

public static class MeshVolumeCalculator
{
    /// <summary>
    /// Computa o volume de uma malha triangular fechada usando o teorema da divergência.
    /// </summary>
    public static double ComputeVolume(TriangleMesh mesh)
    {
        if (mesh == null || mesh.TriangleCount == 0) return 0;

        double volume = 0;
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var (v0, v1, v2) = GetTriangleVertices(mesh, i);
            volume += SignedTetrahedronVolume(v0, v1, v2);
        }
        return Math.Abs(volume);
    }

    private static (Point3D v0, Point3D v1, Point3D v2) GetTriangleVertices(TriangleMesh mesh, int indexOffset)
    {
        uint idx0 = mesh.Indices[indexOffset] * 3;
        uint idx1 = mesh.Indices[indexOffset + 1] * 3;
        uint idx2 = mesh.Indices[indexOffset + 2] * 3;

        var v0 = new Point3D(mesh.Positions[idx0], mesh.Positions[idx0 + 1], mesh.Positions[idx0 + 2]);
        var v1 = new Point3D(mesh.Positions[idx1], mesh.Positions[idx1 + 1], mesh.Positions[idx1 + 2]);
        var v2 = new Point3D(mesh.Positions[idx2], mesh.Positions[idx2 + 1], mesh.Positions[idx2 + 2]);

        return (v0, v1, v2);
    }

    private static double SignedTetrahedronVolume(Point3D v0, Point3D v1, Point3D v2)
    {
        // (v0.x * (v1.y * v2.z - v1.z * v2.y) + v0.y * (v1.z * v2.x - v1.x * v2.z) + v0.z * (v1.x * v2.y - v1.y * v2.x)) / 6.0
        return (v0.X * (v1.Y * v2.Z - v1.Z * v2.Y) + 
                v0.Y * (v1.Z * v2.X - v1.X * v2.Z) + 
                v0.Z * (v1.X * v2.Y - v1.Y * v2.X)) / 6.0;
    }

    private struct Point3D
    {
        public double X, Y, Z;
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    }
}
