using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.Profiles.Classifiers;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

/// <summary>
/// Junta regiões vizinhas do esboço numa região só, antes de virar sólido. Duas metades de um quadrado
/// selecionadas com Ctrl são o quadrado: devem gerar um <c>BoxFeature</c>, não duas caixas e uma união.
/// Além de o código dizer o que foi desenhado, evita a booleana de sólidos que só se encostam numa face,
/// que o OCCT pode resolver descartando um operando sem avisar.
/// <para>
/// Como funciona: o contorno de cada região é percorrido no sentido anti-horário, e a aresta que duas
/// regiões compartilham aparece uma vez em cada sentido e se cancela. O que sobra, se for um laço só, é o
/// contorno da união, que passa pelos mesmos classificadores do esboço (retângulo ou polígono).
/// </para>
/// Só regiões de esboço (<see cref="RectangleProfile"/> e <see cref="PolygonProfile"/>) do mesmo plano se
/// juntam. Quando a união não é um contorno simples (regiões que se sobrepõem, só se tocam num ponto, ou
/// cercam um buraco), o grupo fica como estava: cada região vira o seu sólido, como antes.
/// </summary>
public static class SketchRegionMerger
{
    private const double Tolerance = 1e-4;

    public static IReadOnlyList<IProfile> MergeAdjacent(IReadOnlyList<IProfile> profiles)
    {
        // Para cada perfil, quem o substitui: ele mesmo, a união do grupo dele (no primeiro do grupo) ou nada.
        var replacement = profiles.Select(p => (IProfile?)p).ToArray();

        var byPlane = Enumerable.Range(0, profiles.Count)
            .Where(i => profiles[i] is RectangleProfile or PolygonProfile)
            .GroupBy(i => profiles[i].Plane);

        foreach (var plane in byPlane)
        {
            var members = plane.ToList();
            if (members.Count < 2) continue;

            foreach (var (group, merged) in MergeOnPlane(members.Select(i => profiles[i]).ToList(), plane.Key))
            {
                var indices = group.Select(g => members[g]).OrderBy(i => i).ToList();
                replacement[indices[0]] = merged;
                foreach (var i in indices.Skip(1)) replacement[i] = null;
            }
        }

        return replacement.Where(p => p != null).Select(p => p!).ToList();
    }

    private static IEnumerable<(List<int> Group, IProfile Merged)> MergeOnPlane(List<IProfile> regions, WorkPlane plane)
    {
        var nodes = new List<Point3D>();
        int NodeAt(Point3D p)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].DistanceTo(p) <= Tolerance) return i;
            nodes.Add(p);
            return nodes.Count - 1;
        }

        var outlines = regions.Select(r => CounterClockwiseOutline(r, plane).Select(NodeAt).ToList()).ToList();

        // Arestas dirigidas de cada região, quebradas nos vértices das outras que caem sobre elas: o lado
        // inteiro de uma região pode ser só metade do lado da vizinha (um T no desenho).
        var edges = new List<(int From, int To, int Owner)>();
        for (int owner = 0; owner < outlines.Count; owner++)
        {
            var outline = outlines[owner];
            for (int k = 0; k < outline.Count; k++)
            {
                int a = outline[k], b = outline[(k + 1) % outline.Count];
                if (a == b) continue;
                var stops = Enumerable.Range(0, nodes.Count)
                    .Where(n => n != a && n != b && StrictlyInside(nodes[n], nodes[a], nodes[b]))
                    .OrderBy(n => nodes[n].DistanceTo(nodes[a]))
                    .Prepend(a).Append(b).ToList();
                for (int s = 0; s + 1 < stops.Count; s++)
                    edges.Add((stops[s], stops[s + 1], owner));
            }
        }

        // Duas regiões que dividem uma aresta (em sentidos opostos) são vizinhas.
        var parent = Enumerable.Range(0, regions.Count).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        var edgeOwner = new Dictionary<(int, int), int>();
        foreach (var e in edges) edgeOwner.TryAdd((e.From, e.To), e.Owner);
        foreach (var e in edges)
            if (edgeOwner.TryGetValue((e.To, e.From), out int other) && other != e.Owner)
                parent[Find(e.Owner)] = Find(other);

        foreach (var group in Enumerable.Range(0, regions.Count).GroupBy(Find).Select(g => g.ToList()))
        {
            if (group.Count < 2) continue;
            var groupEdges = edges.Where(e => group.Contains(e.Owner)).Select(e => (e.From, e.To)).ToList();
            var loop = SingleBoundaryLoop(groupEdges);
            if (loop == null) continue;

            var vertices = WithoutStraightThroughVertices(loop.Select(n => nodes[n]).ToList());
            var ids = group.SelectMany(g => regions[g].SourceEntityIds).Distinct().ToList();
            yield return (group, ProfileOf(vertices, ids, plane));
        }
    }

    /// <summary>
    /// O contorno que sobra quando as arestas internas se cancelam, se for um laço simples. Nulo quando a
    /// união tem buraco, se toca num ponto só ou as regiões se sobrepõem (aresta repetida no mesmo sentido).
    /// </summary>
    private static List<int>? SingleBoundaryLoop(List<(int From, int To)> edges)
    {
        if (edges.Distinct().Count() != edges.Count) return null;

        var set = edges.ToHashSet();
        var boundary = edges.Where(e => !set.Contains((e.To, e.From))).ToList();
        if (boundary.Count < 3) return null;

        var next = new Dictionary<int, int>();
        foreach (var e in boundary)
            if (!next.TryAdd(e.From, e.To)) return null; // dois caminhos saindo do mesmo ponto

        var loop = new List<int>();
        int start = boundary[0].From, current = start;
        do
        {
            loop.Add(current);
            if (!next.TryGetValue(current, out current) || loop.Count > boundary.Count) return null;
        } while (current != start);

        return loop.Count == boundary.Count ? loop : null;
    }

    private static IProfile ProfileOf(List<Point3D> vertices, List<Guid> ids, WorkPlane plane)
    {
        var rectangle = new RectangleClassifier().Classify(vertices, ids, plane);
        return rectangle?.Factory() ?? new PolygonProfile(vertices, ids, plane: plane);
    }

    private static List<Point3D> CounterClockwiseOutline(IProfile region, WorkPlane plane)
    {
        var local = region.GetVertices().Select(plane.ToLocalSpace).ToList();
        double area = 0;
        for (int i = 0; i < local.Count; i++)
        {
            var a = local[i];
            var b = local[(i + 1) % local.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        if (area < 0) local.Reverse();
        return local;
    }

    private static bool StrictlyInside(Point3D p, Point3D a, Point3D b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < Tolerance * Tolerance) return false;
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        if (t <= 0 || t >= 1) return false;
        double distance = Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / Math.Sqrt(len2);
        return distance <= Tolerance;
    }

    /// <summary>Tira os vértices no meio de um lado reto, onde ficava a divisa entre duas regiões.</summary>
    private static List<Point3D> WithoutStraightThroughVertices(List<Point3D> vertices)
    {
        bool removed = true;
        while (removed && vertices.Count > 3)
        {
            removed = false;
            for (int i = 0; i < vertices.Count; i++)
            {
                var prev = vertices[(i - 1 + vertices.Count) % vertices.Count];
                var next = vertices[(i + 1) % vertices.Count];
                if (!SketchRegionFinder.IsStraightThrough(prev, vertices[i], next)) continue;
                vertices.RemoveAt(i);
                removed = true;
                break;
            }
        }
        return vertices;
    }
}
