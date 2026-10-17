using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

/// <summary>
/// Projeção ortogonal de uma malha (ou de uma face dela) num plano de trabalho, como referência do esboço
/// (<see cref="PlaneReferenceKind.Projected"/>). Tudo sai em coordenadas locais do plano.
/// <list type="bullet">
/// <item><see cref="Edges"/>: as quinas e as silhuetas vistas ao longo da normal do plano, inclusive as que
/// ficam escondidas. Serve à face e ao volume em "todas as arestas".</item>
/// <item><see cref="Outline"/>: só a sombra do sólido — contorno externo e furos que atravessam.</item>
/// </list>
/// </summary>
public static class PlaneProjection
{
    /// <param name="region">Triângulos da face (<see cref="MeshFaces.Region"/>); <c>null</c> projeta a malha toda.</param>
    public static List<PlaneReference> Edges(MeshEdgeGraph graph, IReadOnlySet<int>? region, WorkPlane plane)
    {
        var axis = plane.Normal.Normalize();
        var segments = new List<(Point3D, Point3D)>();
        foreach (var edge in graph.Edges)
        {
            if (MeshEdgeClassifier.Classify(graph, edge, region, axis) != null)
                segments.Add((Project(plane, graph.Vertex(edge.A)), Project(plane, graph.Vertex(edge.B))));
        }
        return ReferencePolylines.Build(PlaneReferenceKind.Projected, segments);
    }

    /// <summary>
    /// A sombra do sólido no plano: a união dos triângulos que olham para o lado da normal. Num sólido
    /// fechado eles já cobrem a sombra inteira, e os do lado de trás só repetiriam a mesma área.
    /// </summary>
    public static List<PlaneReference> Outline(MeshEdgeGraph graph, WorkPlane plane, IPolygonUnion union)
    {
        var axis = plane.Normal.Normalize();
        var triangles = new List<IReadOnlyList<Point3D>>();
        for (int t = 0; t < graph.TriangleCount; t++)
        {
            if (graph.Normal(t).DotProduct(axis) <= MeshEdgeClassifier.EdgeOnDot) continue;
            var (a, b, c) = graph.Triangle(t);
            triangles.Add(new[] { Project(plane, a), Project(plane, b), Project(plane, c) });
        }
        if (triangles.Count == 0) return new List<PlaneReference>();
        return ReferencePolylines.FromLoops(PlaneReferenceKind.Projected, union.Union(triangles));
    }

    private static Point3D Project(WorkPlane plane, Point3D world)
    {
        var local = plane.ToLocalSpace(world);
        return new Point3D(local.X, local.Y, 0.0);
    }
}
