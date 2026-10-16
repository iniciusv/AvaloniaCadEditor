using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

/// <summary>
/// Tira de uma malha (em coordenadas do mundo) a geometria de referência de um plano de trabalho:
/// <list type="bullet">
/// <item><b>Coplanar</b>: as arestas vivas (<see cref="TriangleMesh.FeatureEdges"/>) com as duas pontas no
/// plano — o contorno das faces que estão nele, inclusive bocas de furo.</item>
/// <item><b>Corte</b>: a seção dos triângulos que atravessam o plano.</item>
/// </list>
/// Os segmentos são encadeados em polilinhas (soldando pontas pela posição, porque a malha não é
/// soldada) e os vértices colineares saem — a diagonal da tesselação e os T-junctions cortam uma aresta
/// reta em pedaços, e o ponto médio de snap tem de ser o da aresta real. Laços circulares ganham centro.
/// </summary>
/// <remarks>
/// Ao contrário do <c>MeshSlicer.Slice</c> (que empurra vértices no plano por um épsilon para nunca
/// ficar em dúvida), aqui um vértice a menos de <c>tol</c> do plano conta como "no plano", e só gera
/// corte o triângulo com vértice estritamente dos dois lados. Assim um sólido que só encosta no plano
/// não repete como corte o contorno que já saiu como coplanar.
/// </remarks>
public static class PlaneSection
{
    private const double WeldScale = 1e4;            // pontas a menos de ~1e-4 viram o mesmo nó
    private const double CollinearSine = 1e-4;       // giro abaixo disso (~0,006°) é colinear
    private const int MinCircleVertices = 8;
    private const double CircleRadiusDeviation = 0.01;

    public static List<PlaneReference> Compute(TriangleMesh mesh, WorkPlane plane, double tol = 1e-3)
    {
        var result = new List<PlaneReference>();
        if (mesh == null || mesh.Positions.Length == 0) return result;

        var coplanar = CoplanarSegments(mesh, plane, tol);
        var section = SectionSegments(mesh, plane, tol);

        AddPolylines(result, PlaneReferenceKind.Coplanar, coplanar);
        AddPolylines(result, PlaneReferenceKind.Section, section);
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

    private static void AddPolylines(List<PlaneReference> result, PlaneReferenceKind kind, List<(Point3D A, Point3D B)> segments)
    {
        foreach (var (points, closed) in Chain(segments))
        {
            var simplified = RemoveCollinear(points, closed);
            if (simplified.Count < 2) continue;

            if (closed && TryFitCircle(simplified, out var center, out var radius))
                result.Add(new PlaneReference(kind, simplified, true, center, radius));
            else
                result.Add(new PlaneReference(kind, simplified, closed && simplified.Count >= 3));
        }
    }

    /// <summary>
    /// Encadeia segmentos num grafo de nós soldados por posição. Nós com grau diferente de 2 (pontas
    /// soltas, junções de mais de duas arestas) terminam uma polilinha; o que sobra depois são laços.
    /// </summary>
    private static List<(List<Point3D> Points, bool Closed)> Chain(List<(Point3D A, Point3D B)> segments)
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

    private static Point3D Vertex(TriangleMesh mesh, uint index) =>
        new(mesh.Positions[index * 3], mesh.Positions[index * 3 + 1], mesh.Positions[index * 3 + 2]);
}
