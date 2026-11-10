using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

/// <summary>
/// As regiões fechadas das linhas e círculos de UM plano de esboço. Cada entidade é quebrada onde cruza
/// outra (linha × linha, linha × círculo, círculo × círculo), e as regiões são as faces limitadas do
/// arranjo resultante: um círculo que atravessa um quadrado o divide, em vez de ficar sobreposto a ele.
/// <para>
/// Os arcos viram polilinha (<see cref="ArcSegmentsPerTurn"/> lados por volta), então uma região com arco
/// é um polígono. Um círculo que não cruza nada em pelo menos dois pontos fica de fora do arranjo e volta
/// em <see cref="Result.StandaloneCircles"/>, para continuar sendo um perfil circular paramétrico.
/// </para>
/// Puro: não altera as entidades. Coordenadas locais do plano, como as das entidades.
/// </summary>
public static class SketchRegionFinder
{
    /// <summary>Lados de uma volta inteira quando um círculo é quebrado em arcos.</summary>
    public const int ArcSegmentsPerTurn = 64;

    private const double Tolerance = 1e-4;

    public sealed record Region(IReadOnlyList<Point3D> Vertices, IReadOnlyList<Guid> SourceEntityIds, bool HasArcs);

    public sealed record Result(IReadOnlyList<Region> Regions, IReadOnlyList<CircleEntity> StandaloneCircles);

    public static Result Find(IReadOnlyList<LineEntity> lines, IReadOnlyList<CircleEntity> circles)
    {
        var lineCuts = lines.Select(l => new List<(double T, Point3D P)> { (0, l.StartPoint), (1, l.EndPoint) }).ToList();
        var circleCuts = circles.Select(_ => new List<(double Angle, Point3D P)>()).ToList();

        for (int i = 0; i < lines.Count; i++)
            for (int j = i + 1; j < lines.Count; j++)
                if (IntersectSegments(lines[i], lines[j]) is { } hit)
                {
                    lineCuts[i].Add((hit.T, hit.P));
                    lineCuts[j].Add((hit.U, hit.P));
                }

        for (int i = 0; i < lines.Count; i++)
            for (int c = 0; c < circles.Count; c++)
                foreach (var (t, p) in IntersectSegmentCircle(lines[i], circles[c]))
                {
                    lineCuts[i].Add((t, p));
                    circleCuts[c].Add((AngleOn(circles[c], p), p));
                }

        for (int a = 0; a < circles.Count; a++)
            for (int b = a + 1; b < circles.Count; b++)
                foreach (var p in IntersectCircles(circles[a], circles[b]))
                {
                    circleCuts[a].Add((AngleOn(circles[a], p), p));
                    circleCuts[b].Add((AngleOn(circles[b], p), p));
                }

        var graph = new Graph();

        for (int i = 0; i < lines.Count; i++)
        {
            var nodes = lineCuts[i].OrderBy(c => c.T).Select(c => graph.NodeAt(c.P)).ToList();
            for (int k = 0; k + 1 < nodes.Count; k++)
                graph.AddStraight(nodes[k], nodes[k + 1], lines[i].Id);
        }

        var standalone = new List<CircleEntity>();
        for (int c = 0; c < circles.Count; c++)
        {
            var cuts = DistinctCuts(circleCuts[c]);
            if (cuts.Count < 2)
            {
                standalone.Add(circles[c]);
                continue;
            }
            AddArcs(graph, circles[c], cuts);
        }

        graph.PruneDanglingEdges();
        var regions = graph.BoundedFaces().Select(face => ToRegion(graph, face)).ToList();
        return new Result(regions, standalone);
    }

    private static List<(double Angle, Point3D P)> DistinctCuts(List<(double Angle, Point3D P)> cuts)
    {
        var sorted = cuts.OrderBy(c => c.Angle).ToList();
        var distinct = new List<(double, Point3D)>();
        foreach (var cut in sorted)
            if (distinct.Count == 0 || distinct[^1].Item2.DistanceTo(cut.P) > Tolerance)
                distinct.Add(cut);
        // O primeiro e o último podem ser o mesmo ponto, perto do ângulo zero.
        if (distinct.Count > 1 && distinct[0].Item2.DistanceTo(distinct[^1].Item2) <= Tolerance)
            distinct.RemoveAt(distinct.Count - 1);
        return distinct;
    }

    private static void AddArcs(Graph graph, CircleEntity circle, List<(double Angle, Point3D P)> cuts)
    {
        double step = 2 * Math.PI / ArcSegmentsPerTurn;
        for (int k = 0; k < cuts.Count; k++)
        {
            var from = cuts[k];
            var to = cuts[(k + 1) % cuts.Count];
            double start = from.Angle;
            double span = to.Angle - start;
            if (span <= 0) span += 2 * Math.PI;

            // Dois lados no mínimo: um arco de um lado só seria a corda, e coincidiria com uma linha
            // que ligasse os mesmos dois pontos.
            int segments = Math.Max(2, (int)Math.Ceiling(span / step - 1e-9));

            int previous = graph.NodeAt(from.P);
            int last = graph.NodeAt(to.P);
            for (int s = 1; s < segments; s++)
            {
                double angle = start + span * s / segments;
                int node = graph.NewNode(new Point3D(
                    circle.Center.X + circle.Radius * Math.Cos(angle),
                    circle.Center.Y + circle.Radius * Math.Sin(angle),
                    circle.Center.Z));
                graph.AddArc(previous, node, circle.Id);
                previous = node;
            }
            graph.AddArc(previous, last, circle.Id);
        }
    }

    private static Region ToRegion(Graph graph, List<int> face)
    {
        // (vértice, a aresta que SAI dele é arco?)
        var corners = face.Select(h => (P: graph.Origin(h), OutArc: graph.IsArc(h))).ToList();

        // Vértices no meio de um lado reto (onde uma linha foi quebrada por outra que só encosta)
        // não são cantos: tirá-los deixa o retângulo ser reconhecido como retângulo.
        bool removed = true;
        while (removed && corners.Count > 3)
        {
            removed = false;
            for (int i = 0; i < corners.Count; i++)
            {
                var prev = corners[(i - 1 + corners.Count) % corners.Count];
                var cur = corners[i];
                var next = corners[(i + 1) % corners.Count];
                if (prev.OutArc || cur.OutArc) continue;
                if (!IsStraightThrough(prev.P, cur.P, next.P)) continue;
                corners.RemoveAt(i);
                removed = true;
                break;
            }
        }

        var ids = face.Select(graph.EntityId).Distinct().ToList();
        bool hasArcs = face.Any(graph.IsArc);
        return new Region(corners.Select(c => c.P).ToList(), ids, hasArcs);
    }

    /// <summary>b está no meio do caminho reto de a para c (não é canto).</summary>
    internal static bool IsStraightThrough(Point3D a, Point3D b, Point3D c)
    {
        double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y;
        double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
        if (lu < Tolerance || lv < Tolerance) return true;
        double cross = (ux * vy - uy * vx) / (lu * lv);
        double dot = ux * vx + uy * vy;
        return Math.Abs(cross) < 1e-9 && dot > 0;
    }

    private static double AngleOn(CircleEntity circle, Point3D p)
    {
        double a = Math.Atan2(p.Y - circle.Center.Y, p.X - circle.Center.X);
        return a < 0 ? a + 2 * Math.PI : a;
    }

    private static (double T, double U, Point3D P)? IntersectSegments(LineEntity a, LineEntity b)
    {
        double dx1 = a.EndPoint.X - a.StartPoint.X, dy1 = a.EndPoint.Y - a.StartPoint.Y;
        double dx2 = b.EndPoint.X - b.StartPoint.X, dy2 = b.EndPoint.Y - b.StartPoint.Y;
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1), len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
        if (len1 < Tolerance || len2 < Tolerance) return null;

        double denom = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(denom) < 1e-12 * len1 * len2) return null; // paralelas (sobreposição colinear fica de fora)

        double ex = b.StartPoint.X - a.StartPoint.X, ey = b.StartPoint.Y - a.StartPoint.Y;
        double t = (ex * dy2 - ey * dx2) / denom;
        double u = (ex * dy1 - ey * dx1) / denom;

        double et = Tolerance / len1, eu = Tolerance / len2;
        if (t < -et || t > 1 + et || u < -eu || u > 1 + eu) return null;

        t = Math.Clamp(t, 0, 1);
        u = Math.Clamp(u, 0, 1);
        return (t, u, Lerp(a.StartPoint, a.EndPoint, t));
    }

    private static IEnumerable<(double T, Point3D P)> IntersectSegmentCircle(LineEntity line, CircleEntity circle)
    {
        double dx = line.EndPoint.X - line.StartPoint.X, dy = line.EndPoint.Y - line.StartPoint.Y;
        double len2 = dx * dx + dy * dy;
        double len = Math.Sqrt(len2);
        if (len < Tolerance) yield break;

        // Ponto da reta mais perto do centro, e a distância dele ao centro.
        double fx = circle.Center.X - line.StartPoint.X, fy = circle.Center.Y - line.StartPoint.Y;
        double t0 = (fx * dx + fy * dy) / len2;
        double px = line.StartPoint.X + t0 * dx - circle.Center.X;
        double py = line.StartPoint.Y + t0 * dy - circle.Center.Y;
        double h = Math.Sqrt(px * px + py * py);
        if (h > circle.Radius + Tolerance) yield break;

        double half = Math.Sqrt(Math.Max(0, circle.Radius * circle.Radius - h * h));
        var roots = half < Tolerance ? new[] { t0 } : new[] { t0 - half / len, t0 + half / len };

        double e = Tolerance / len;
        foreach (var root in roots)
        {
            if (root < -e || root > 1 + e) continue;
            double t = Math.Clamp(root, 0, 1);
            yield return (t, Lerp(line.StartPoint, line.EndPoint, t));
        }
    }

    private static IEnumerable<Point3D> IntersectCircles(CircleEntity a, CircleEntity b)
    {
        double dx = b.Center.X - a.Center.X, dy = b.Center.Y - a.Center.Y;
        double d = Math.Sqrt(dx * dx + dy * dy);
        if (d < Tolerance) yield break; // concêntricos
        if (d > a.Radius + b.Radius + Tolerance || d < Math.Abs(a.Radius - b.Radius) - Tolerance) yield break;

        double along = (a.Radius * a.Radius - b.Radius * b.Radius + d * d) / (2 * d);
        double h = Math.Sqrt(Math.Max(0, a.Radius * a.Radius - along * along));
        double mx = a.Center.X + along * dx / d, my = a.Center.Y + along * dy / d;

        if (h < Tolerance)
        {
            yield return new Point3D(mx, my, a.Center.Z);
            yield break;
        }

        double ox = -dy / d * h, oy = dx / d * h;
        yield return new Point3D(mx + ox, my + oy, a.Center.Z);
        yield return new Point3D(mx - ox, my - oy, a.Center.Z);
    }

    private static Point3D Lerp(Point3D a, Point3D b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    /// <summary>
    /// Grafo planar com meias-arestas: a aresta e tem as meias 2e (From → To) e 2e + 1 (To → From).
    /// </summary>
    private sealed class Graph
    {
        private readonly List<Point3D> _nodes = new();
        private readonly List<(int From, int To, Guid EntityId, bool IsArc)> _edges = new();
        private readonly HashSet<(int, int)> _straightPairs = new();
        private readonly HashSet<int> _removedEdges = new();

        public int NodeAt(Point3D p)
        {
            for (int i = 0; i < _nodes.Count; i++)
                if (_nodes[i].DistanceTo(p) <= Tolerance) return i;
            return NewNode(p);
        }

        public int NewNode(Point3D p)
        {
            _nodes.Add(p);
            return _nodes.Count - 1;
        }

        public void AddStraight(int a, int b, Guid entityId)
        {
            if (a == b) return;
            // Duas linhas iguais (ou pedaços iguais) dariam uma face de área zero entre elas.
            if (!_straightPairs.Add((Math.Min(a, b), Math.Max(a, b)))) return;
            _edges.Add((a, b, entityId, false));
        }

        public void AddArc(int a, int b, Guid entityId)
        {
            if (a == b) return;
            _edges.Add((a, b, entityId, true));
        }

        public Point3D Origin(int half) => _nodes[half % 2 == 0 ? _edges[half / 2].From : _edges[half / 2].To];
        private int Target(int half) => half % 2 == 0 ? _edges[half / 2].To : _edges[half / 2].From;
        private int OriginNode(int half) => half % 2 == 0 ? _edges[half / 2].From : _edges[half / 2].To;
        public bool IsArc(int half) => _edges[half / 2].IsArc;
        public Guid EntityId(int half) => _edges[half / 2].EntityId;

        /// <summary>Tira as pontas soltas (linha que passa do canto, risco que não fecha nada).</summary>
        public void PruneDanglingEdges()
        {
            var incident = new List<int>[_nodes.Count];
            for (int n = 0; n < _nodes.Count; n++) incident[n] = new List<int>();
            for (int e = 0; e < _edges.Count; e++)
            {
                incident[_edges[e].From].Add(e);
                incident[_edges[e].To].Add(e);
            }

            var degree = incident.Select(l => l.Count).ToArray();
            var queue = new Queue<int>(Enumerable.Range(0, _nodes.Count).Where(n => degree[n] == 1));
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                if (degree[n] != 1) continue;
                int e = incident[n].First(x => !_removedEdges.Contains(x));
                _removedEdges.Add(e);
                degree[n]--;
                int other = _edges[e].From == n ? _edges[e].To : _edges[e].From;
                degree[other]--;
                if (degree[other] == 1) queue.Enqueue(other);
            }
        }

        /// <summary>
        /// Percorre as faces mantendo-as à esquerda: em cada nó, sai pela meia-aresta logo no sentido
        /// horário da que chegou. As faces limitadas saem anti-horárias (área positiva); a face de fora de
        /// cada componente sai horária e é descartada.
        /// </summary>
        public IEnumerable<List<int>> BoundedFaces()
        {
            var outgoing = new List<int>[_nodes.Count];
            for (int n = 0; n < _nodes.Count; n++) outgoing[n] = new List<int>();
            for (int e = 0; e < _edges.Count; e++)
            {
                if (_removedEdges.Contains(e)) continue;
                outgoing[_edges[e].From].Add(2 * e);
                outgoing[_edges[e].To].Add(2 * e + 1);
            }

            var position = new Dictionary<int, int>();
            for (int n = 0; n < _nodes.Count; n++)
            {
                var origin = _nodes[n];
                outgoing[n].Sort((h1, h2) => Direction(origin, h1).CompareTo(Direction(origin, h2)));
                for (int i = 0; i < outgoing[n].Count; i++) position[outgoing[n][i]] = i;
            }

            var visited = new HashSet<int>();
            foreach (var start in position.Keys)
            {
                if (visited.Contains(start)) continue;

                var face = new List<int>();
                int half = start;
                while (visited.Add(half))
                {
                    face.Add(half);
                    int twin = half ^ 1;
                    var around = outgoing[OriginNode(twin)];
                    half = around[(position[twin] - 1 + around.Count) % around.Count];
                }

                if (half == start && face.Count >= 3 && SignedArea(face) > Tolerance * Tolerance)
                    yield return face;
            }
        }

        private double Direction(Point3D origin, int half)
        {
            var target = _nodes[Target(half)];
            return Math.Atan2(target.Y - origin.Y, target.X - origin.X);
        }

        private double SignedArea(List<int> face)
        {
            double sum = 0;
            foreach (var half in face)
            {
                var a = Origin(half);
                var b = _nodes[Target(half)];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2;
        }
    }
}
