using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

/// <summary>
/// Transforma segmentos soltos (ou laços prontos) em coordenadas locais de um plano nas
/// <see cref="PlaneReference"/> do esboço. Serve ao <see cref="PlaneSection"/>, ao <see cref="PlaneProjection"/>
/// e a quem mais projetar arestas num plano (a vista em texto do harness de IA).
/// <list type="bullet">
/// <item>As pontas são soldadas pela posição, porque a malha do OCCT não é soldada.</item>
/// <item>Os vértices colineares saem: a diagonal da tesselação e os T-junctions cortam uma aresta reta em
/// pedaços, e o ponto médio do snap tem de ser o da aresta real.</item>
/// <item>Os laços circulares ganham centro e raio.</item>
/// </list>
/// </summary>
public static class ReferencePolylines
{
    private const double WeldScale = 1e4;            // pontas a menos de ~1e-4 viram o mesmo nó
    private const double CollinearSine = 1e-4;       // giro abaixo disso (~0,006°) é colinear
    private const int MinCircleVertices = 8;
    private const double CircleRadiusDeviation = 0.01;

    /// <summary>
    /// Encadeia os segmentos em polilinhas. Segmentos que viram um ponto e segmentos repetidos (duas arestas
    /// 3D que projetam no mesmo lugar) saem no encadeamento.
    /// </summary>
    public static List<PlaneReference> Build(PlaneReferenceKind kind, IEnumerable<(Point3D A, Point3D B)> segments)
    {
        var result = new List<PlaneReference>();
        foreach (var (points, closed) in Chain(segments))
            Add(result, kind, points, closed);
        return result;
    }

    /// <summary>Laços que já chegam fechados e ordenados (o contorno de uma união de polígonos).</summary>
    public static List<PlaneReference> FromLoops(PlaneReferenceKind kind, IEnumerable<IReadOnlyList<Point3D>> loops)
    {
        var result = new List<PlaneReference>();
        foreach (var loop in loops)
            Add(result, kind, loop.Select(Flat).ToList(), closed: true);
        return result;
    }

    private static void Add(List<PlaneReference> result, PlaneReferenceKind kind, List<Point3D> points, bool closed)
    {
        var simplified = RemoveCollinear(points, closed);
        if (simplified.Count < 2) return;

        if (closed && TryFitCircle(simplified, out var center, out var radius))
            result.Add(new PlaneReference(kind, simplified, true, center, radius));
        else
            result.Add(new PlaneReference(kind, simplified, closed && simplified.Count >= 3));
    }

    /// <summary>
    /// Encadeia segmentos num grafo de nós soldados por posição. Nós com grau diferente de 2 (pontas
    /// soltas, junções de mais de duas arestas) terminam uma polilinha; o que sobra depois são laços.
    /// </summary>
    private static List<(List<Point3D> Points, bool Closed)> Chain(IEnumerable<(Point3D A, Point3D B)> segments)
    {
        var nodeOf = new Dictionary<(long, long), int>();
        var nodes = new List<Point3D>();
        int Node(Point3D pt)
        {
            var key = ((long)Math.Round(pt.X * WeldScale), (long)Math.Round(pt.Y * WeldScale));
            if (!nodeOf.TryGetValue(key, out var id))
            {
                id = nodes.Count;
                nodes.Add(pt);
                nodeOf[key] = id;
            }
            return id;
        }

        var edges = new List<(int A, int B)>();
        var seen = new HashSet<(int, int)>();
        foreach (var (a, b) in segments)
        {
            int na = Node(a), nb = Node(b);
            if (na == nb) continue;
            if (!seen.Add((Math.Min(na, nb), Math.Max(na, nb)))) continue;
            edges.Add((na, nb));
        }

        var adjacency = new List<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++) adjacency[i] = new List<int>(2);
        for (int e = 0; e < edges.Count; e++)
        {
            adjacency[edges[e].A].Add(e);
            adjacency[edges[e].B].Add(e);
        }

        var used = new bool[edges.Count];
        var result = new List<(List<Point3D>, bool)>();

        List<Point3D> Walk(int start, int firstEdge, out bool closed)
        {
            var path = new List<Point3D> { nodes[start] };
            int current = start, edge = firstEdge;
            closed = false;
            while (true)
            {
                used[edge] = true;
                int next = edges[edge].A == current ? edges[edge].B : edges[edge].A;
                if (next == start) { closed = true; break; }
                path.Add(nodes[next]);
                current = next;
                if (adjacency[current].Count != 2) break;
                int following = adjacency[current][0] == edge ? adjacency[current][1] : adjacency[current][0];
                if (used[following]) break;
                edge = following;
            }
            return path;
        }

        // Primeiro as cadeias que partem de pontas/junções; depois os laços puros.
        for (int n = 0; n < nodes.Count; n++)
        {
            if (adjacency[n].Count == 2) continue;
            foreach (var e in adjacency[n])
            {
                if (used[e]) continue;
                var path = Walk(n, e, out var closed);
                result.Add((path, closed));
            }
        }
        for (int e = 0; e < edges.Count; e++)
        {
            if (used[e]) continue;
            var path = Walk(edges[e].A, e, out var closed);
            result.Add((path, closed));
        }
        return result;
    }

    private static List<Point3D> RemoveCollinear(List<Point3D> points, bool closed)
    {
        int n = points.Count;
        if (n < 3) return points;

        if (!closed)
        {
            var open = new List<Point3D> { points[0] };
            for (int i = 1; i < n - 1; i++)
            {
                if (!IsCollinear(open[^1], points[i], points[i + 1])) open.Add(points[i]);
            }
            open.Add(points[n - 1]);
            return open;
        }

        // Laço: começa num canto de verdade, senão o primeiro ponto (talvez no meio de uma aresta) ficaria.
        int start = -1;
        for (int i = 0; i < n; i++)
        {
            if (!IsCollinear(points[(i - 1 + n) % n], points[i], points[(i + 1) % n])) { start = i; break; }
        }
        if (start < 0) return points;

        var loop = new List<Point3D> { points[start] };
        for (int k = 1; k < n; k++)
        {
            var curr = points[(start + k) % n];
            var next = points[(start + k + 1) % n];
            if (!IsCollinear(loop[^1], curr, next)) loop.Add(curr);
        }
        return loop;
    }

    private static bool IsCollinear(Point3D prev, Point3D curr, Point3D next)
    {
        double ax = curr.X - prev.X, ay = curr.Y - prev.Y;
        double bx = next.X - curr.X, by = next.Y - curr.Y;
        double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
        if (la < 1e-12 || lb < 1e-12) return true;
        double cross = (ax * by - ay * bx) / (la * lb);
        double dot = ax * bx + ay * by;
        return Math.Abs(cross) < CollinearSine && dot > 0;
    }

    /// <summary>
    /// Um laço é círculo quando tem vértices suficientes e todos estão à mesma distância do centróide da
    /// área (a tesselação de um cilindro é um polígono regular inscrito). Polígonos de poucos lados —
    /// retângulos, sextavados — ficam de fora pelo número de vértices.
    /// </summary>
    private static bool TryFitCircle(List<Point3D> loop, out Point3D center, out double radius)
    {
        center = default;
        radius = 0;
        if (loop.Count < MinCircleVertices) return false;

        double area2 = 0, cx = 0, cy = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            double cross = a.X * b.Y - b.X * a.Y;
            area2 += cross;
            cx += (a.X + b.X) * cross;
            cy += (a.Y + b.Y) * cross;
        }
        if (Math.Abs(area2) < 1e-12) return false;
        cx /= 3 * area2;
        cy /= 3 * area2;

        var radii = loop.Select(p => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy))).ToList();
        double mean = radii.Average();
        if (mean < 1e-9) return false;
        if (radii.Any(r => Math.Abs(r - mean) > CircleRadiusDeviation * mean)) return false;

        center = new Point3D(cx, cy, 0.0);
        radius = mean;
        return true;
    }

    private static Point3D Flat(Point3D local) => new(local.X, local.Y, 0.0);
}
