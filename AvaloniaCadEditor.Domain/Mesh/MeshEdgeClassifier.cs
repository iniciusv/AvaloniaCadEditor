using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

/// <summary>Por que uma aresta da malha aparece quando o sólido é visto ao longo de um eixo.</summary>
public enum ProjectedEdgeKind
{
    /// <summary>Borda da região escolhida ou da própria malha (aresta com um triângulo só).</summary>
    Border,

    /// <summary>Quina: os triângulos vizinhos formam um ângulo acima do crease.</summary>
    Crease,

    /// <summary>Silhueta: aresta suave cujos triângulos olham para lados opostos do eixo de vista.</summary>
    Silhouette
}

/// <summary>
/// Decide quais arestas de um <see cref="MeshEdgeGraph"/> viram linha quando a malha é vista (ou
/// projetada) ao longo de um eixo. Visibilidade (o que está escondido atrás) não é tratada aqui.
/// </summary>
public static class MeshEdgeClassifier
{
    /// <summary>Abaixo disto, um triângulo é visto de lado (a normal é perpendicular ao eixo).</summary>
    public const double EdgeOnDot = 1e-6;

    /// <summary>
    /// Uma aresta aparece quando é borda da face, quina, ou silhueta: os dois triângulos dela olham para
    /// lados diferentes do eixo (ou um deles é visto de lado). <c>null</c> = a aresta não aparece.
    /// </summary>
    /// <param name="region">Triângulos de uma face (<see cref="MeshFaces.Region"/>); <c>null</c> = a malha toda.</param>
    public static ProjectedEdgeKind? Classify(MeshEdgeGraph graph, MeshEdgeGraph.Edge edge, IReadOnlySet<int>? region, Vector3D axis)
    {
        int inside = 0;
        foreach (int t in edge.Triangles)
            if (region == null || region.Contains(t)) inside++;

        if (inside == 0) return null;
        if (inside < edge.Triangles.Count) return ProjectedEdgeKind.Border;   // borda da face escolhida
        if (edge.Triangles.Count < 2) return ProjectedEdgeKind.Border;        // borda da malha
        if (!graph.IsSmooth(edge)) return ProjectedEdgeKind.Crease;

        return Facing(graph, edge.Triangles[0], axis) != Facing(graph, edge.Triangles[1], axis)
            ? ProjectedEdgeKind.Silhouette
            : null;
    }

    /// <summary>+1 se o triângulo olha para o lado do eixo, -1 se para o lado oposto, 0 se é visto de lado.</summary>
    public static int Facing(MeshEdgeGraph graph, int triangle, Vector3D axis)
    {
        double d = graph.Normal(triangle).DotProduct(axis);
        return d > EdgeOnDot ? 1 : d < -EdgeOnDot ? -1 : 0;
    }
}
