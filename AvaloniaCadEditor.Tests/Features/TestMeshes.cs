using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Tests.Features;

/// <summary>Malhas montadas à mão como o tesselador do OCCT as entrega, para testar algoritmos de malha.</summary>
internal static class TestMeshes
{
    /// <summary>
    /// Prisma reto sobre um polígono anti-horário do plano XY, entre <paramref name="z0"/> e <paramref name="z1"/>:
    /// vértices não soldados por face, tampas em leque (as duas com a normal para fora) e cada parede partida por
    /// uma diagonal. Arestas vivas: os contornos das tampas e, se <paramref name="sharpWalls"/>, as quinas
    /// verticais (num cilindro elas são lisas e não entram).
    /// </summary>
    public static TriangleMesh Prism(IReadOnlyList<(double X, double Y)> polygon, double z0, double z1, bool sharpWalls = true)
    {
        var positions = new List<float>();
        var indices = new List<uint>();
        var featureEdges = new List<uint>();
        int n = polygon.Count;

        uint Add(double x, double y, double z)
        {
            positions.Add((float)x); positions.Add((float)y); positions.Add((float)z);
            return (uint)(positions.Count / 3 - 1);
        }

        foreach (var (z, up) in new[] { (z0, false), (z1, true) })
        {
            uint first = (uint)(positions.Count / 3);
            foreach (var (x, y) in polygon) Add(x, y, z);
            for (int i = 1; i + 1 < n; i++)
            {
                uint b = first + (uint)i, c = first + (uint)i + 1;
                indices.AddRange(up ? new[] { first, b, c } : new[] { first, c, b });
            }
            for (int i = 0; i < n; i++) { featureEdges.Add(first + (uint)i); featureEdges.Add(first + (uint)((i + 1) % n)); }
        }

        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            uint v0 = Add(a.X, a.Y, z0), v1 = Add(b.X, b.Y, z0), v2 = Add(b.X, b.Y, z1), v3 = Add(a.X, a.Y, z1);
            indices.AddRange(new[] { v0, v1, v2, v0, v2, v3 });
            if (sharpWalls) { featureEdges.Add(v0); featureEdges.Add(v3); }
        }

        var arr = positions.ToArray();
        return new TriangleMesh(arr, new float[arr.Length], indices.ToArray(), featureEdges.ToArray());
    }

    public static TriangleMesh Box(double x0, double y0, double x1, double y1, double z0, double z1) =>
        Prism(new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }, z0, z1);

    public static TriangleMesh Cylinder(double cx, double cy, double r, double z0, double z1, int sides = 32) =>
        Prism(Enumerable.Range(0, sides)
            .Select(i => (cx + r * Math.Cos(2 * Math.PI * i / sides), cy + r * Math.Sin(2 * Math.PI * i / sides)))
            .ToList(), z0, z1, sharpWalls: false);

    public static TriangleMesh Combine(params TriangleMesh[] meshes) => TriangleMesh.Combine(meshes, includeFeatureNormals: false);
}
