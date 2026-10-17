using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Harness.Tests.Views;

/// <summary>
/// Malhas montadas à mão como o tesselador do OCCT as entrega (vértices não soldados por face,
/// tampas em leque, paredes partidas por uma diagonal). Mesma construção do TestMeshes de
/// AvaloniaCadEditor.Tests, que é interno àquele projeto.
/// </summary>
internal static class TestMeshes
{
    /// <summary>Prisma reto sobre um polígono anti-horário do plano XY, entre <paramref name="z0"/> e <paramref name="z1"/>.</summary>
    public static TriangleMesh Prism(IReadOnlyList<(double X, double Y)> polygon, double z0, double z1)
    {
        var positions = new List<float>();
        var indices = new List<uint>();
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
        }

        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            uint v0 = Add(a.X, a.Y, z0), v1 = Add(b.X, b.Y, z0), v2 = Add(b.X, b.Y, z1), v3 = Add(a.X, a.Y, z1);
            indices.AddRange(new[] { v0, v1, v2, v0, v2, v3 });
        }

        var arr = positions.ToArray();
        return new TriangleMesh(arr, new float[arr.Length], indices.ToArray());
    }

    public static TriangleMesh Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
        Prism(new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }, z0, z1);

    /// <summary>Cilindro de eixo Z, com um vértice do polígono em cada ponto cardeal (32 lados).</summary>
    public static TriangleMesh Cylinder(double cx, double cy, double r, double z0, double z1, int sides = 32) =>
        Prism(Enumerable.Range(0, sides)
            .Select(i => (cx + r * Math.Cos(2 * Math.PI * i / sides), cy + r * Math.Sin(2 * Math.PI * i / sides)))
            .ToList(), z0, z1);

    /// <summary>Gira a malha levando o eixo Z para X (x, y, z) → (z, x, y): uma rotação, a orientação dos triângulos se mantém.</summary>
    public static TriangleMesh ZToX(TriangleMesh mesh)
    {
        var p = mesh.Positions;
        var rotated = new float[p.Length];
        for (int i = 0; i < p.Length; i += 3)
        {
            rotated[i] = p[i + 2];
            rotated[i + 1] = p[i];
            rotated[i + 2] = p[i + 1];
        }
        return new TriangleMesh(rotated, new float[p.Length], mesh.Indices);
    }

    public static TriangleMesh Combine(params TriangleMesh[] meshes) => TriangleMesh.Combine(meshes, includeFeatureNormals: false);
}
