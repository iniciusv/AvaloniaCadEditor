using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

/// <summary>
/// Tira de uma malha (em coordenadas do mundo) a geometria de referência de um plano de trabalho:
/// <list type="bullet">
/// <item><b>Coplanar</b>: as arestas vivas (<see cref="TriangleMesh.FeatureEdges"/>) com as duas pontas no
/// plano — o contorno das faces que estão nele, inclusive bocas de furo.</item>
/// <item><b>Corte</b>: a seção dos triângulos que atravessam o plano.</item>
/// </list>
/// Os segmentos viram polilinhas em <see cref="ReferencePolylines"/> (solda, colineares, círculos).
/// </summary>
/// <remarks>
/// Ao contrário do <c>MeshSlicer.Slice</c> (que empurra vértices no plano por um épsilon para nunca
/// ficar em dúvida), aqui um vértice a menos de <c>tol</c> do plano conta como "no plano", e só gera
/// corte o triângulo com vértice estritamente dos dois lados. Assim um sólido que só encosta no plano
/// não repete como corte o contorno que já saiu como coplanar.
/// </remarks>
public static class PlaneSection
{
    public static List<PlaneReference> Compute(TriangleMesh mesh, WorkPlane plane, double tol = 1e-3)
    {
        var result = new List<PlaneReference>();
        if (mesh == null || mesh.Positions.Length == 0) return result;

        result.AddRange(ReferencePolylines.Build(PlaneReferenceKind.Coplanar, CoplanarSegments(mesh, plane, tol)));
        result.AddRange(ReferencePolylines.Build(PlaneReferenceKind.Section, SectionSegments(mesh, plane, tol)));
        return result;
    }

    private static List<(Point3D A, Point3D B)> CoplanarSegments(TriangleMesh mesh, WorkPlane plane, double tol)
    {
        var segments = new List<(Point3D, Point3D)>();
        var edges = mesh.FeatureEdges;
        for (int i = 0; i + 1 < edges.Length; i += 2)
        {
            var a = plane.ToLocalSpace(Vertex(mesh, edges[i]));
            var b = plane.ToLocalSpace(Vertex(mesh, edges[i + 1]));
            if (Math.Abs(a.Z) >= tol || Math.Abs(b.Z) >= tol) continue;
            segments.Add((Flat(a), Flat(b)));
        }
        return segments;
    }

    private static List<(Point3D A, Point3D B)> SectionSegments(TriangleMesh mesh, WorkPlane plane, double tol)
    {
        var segments = new List<(Point3D, Point3D)>();
        var idx = mesh.Indices;
        var p = new Point3D[3];
        var s = new int[3];
        var crossings = new List<Point3D>(3);

        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            bool hasPositive = false, hasNegative = false;
            for (int k = 0; k < 3; k++)
            {
                p[k] = plane.ToLocalSpace(Vertex(mesh, idx[t + k]));
                s[k] = p[k].Z > tol ? 1 : p[k].Z < -tol ? -1 : 0;
                hasPositive |= s[k] > 0;
                hasNegative |= s[k] < 0;
            }
            if (!hasPositive || !hasNegative) continue;

            crossings.Clear();
            for (int k = 0; k < 3; k++)
            {
                int j = (k + 1) % 3;
                if (s[k] == 0) crossings.Add(Flat(p[k]));
                if (s[k] * s[j] < 0) crossings.Add(Crossing(p[k], p[j]));
            }
            if (crossings.Count == 2) segments.Add((crossings[0], crossings[1]));
        }
        return segments;
    }

    /// <summary>
    /// Cruzamento da aresta com o plano, interpolado sempre do lado negativo para o positivo: os dois
    /// triângulos que dividem a aresta chegam ao mesmo ponto bit a bit, e a solda pela posição não falha.
    /// </summary>
    private static Point3D Crossing(Point3D a, Point3D b)
    {
        if (a.Z > b.Z) (a, b) = (b, a);
        double t = -a.Z / (b.Z - a.Z);
        return new Point3D(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), 0.0);
    }

    private static Point3D Flat(Point3D local) => new(local.X, local.Y, 0.0);

    private static Point3D Vertex(TriangleMesh mesh, uint index) =>
        new(mesh.Positions[index * 3], mesh.Positions[index * 3 + 1], mesh.Positions[index * 3 + 2]);
}
