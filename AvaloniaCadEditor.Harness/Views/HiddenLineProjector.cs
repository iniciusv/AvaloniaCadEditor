using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;

namespace AvaloniaCadEditor.Harness.Views;

/// <summary>Limites e passos do <see cref="HiddenLineProjector"/>.</summary>
public sealed record HiddenLineOptions
{
    public double CreaseAngleDegrees { get; init; } = MeshEdgeGraph.DefaultCreaseAngleDegrees;

    /// <summary>Malhas maiores que isto são recusadas (o texto ficaria grande demais de qualquer jeito).</summary>
    public int MaxTriangles { get; init; } = 300_000;

    /// <summary>Amostras por aresta: entre 2 e este máximo, conforme o comprimento projetado.</summary>
    public int MaxSamplesPerEdge { get; init; } = 64;

    /// <summary>Passo de amostragem como fração da diagonal da caixa projetada.</summary>
    public double SampleStepFraction { get; init; } = 1.0 / 256;

    /// <summary>Folga de profundidade como fração da diagonal: abaixo disto, dois pontos estão no mesmo plano.</summary>
    public double DepthToleranceFraction { get; init; } = 1e-4;
}

/// <summary>Uma linha da vista: visível ou oculta, com o motivo de aparecer e a forma em (U, V).</summary>
/// <param name="Shape">Polilinha ou círculo em coordenadas da vista (X = U, Y = V, Z = 0).</param>
public sealed record ViewLine(bool Visible, ProjectedEdgeKind Kind, PlaneReference Shape);

/// <summary>Caixa do que foi projetado: U e V na tela, W (profundidade, maior = mais perto) para quem olha.</summary>
public readonly record struct ViewBounds(double MinU, double MaxU, double MinV, double MaxV, double MinW, double MaxW)
{
    public double Diagonal => Math.Sqrt((MaxU - MinU) * (MaxU - MinU) + (MaxV - MinV) * (MaxV - MinV) + (MaxW - MinW) * (MaxW - MinW));
}

public sealed record HiddenLineResult(IReadOnlyList<ViewLine> Lines, ViewBounds Bounds, int TriangleCount, string? Error = null)
{
    public static HiddenLineResult Refused(string error, int triangleCount) => new(Array.Empty<ViewLine>(), default, triangleCount, error);
}

/// <summary>
/// Remoção de linhas ocultas na CPU, sobre a malha: o que a vista em arame mostra (linhas cheias e
/// tracejadas), mas como dados. O depth buffer da GPU faz isso por pixel na tela; aqui, para cada
/// aresta que aparece na vista (quina, borda ou silhueta), pontos amostrados são testados contra os
/// triângulos à frente deles. Vista ortográfica: todos os raios são paralelos a W, então uma grade
/// 2D em (U, V) basta como aceleração.
/// </summary>
public static class HiddenLineProjector
{
    private const double BaryTolerance = 1e-7;
    private const int BisectionSteps = 3;

    private sealed record Triangle2D(P2 A, P2 B, P2 C, double Da, double Db, double Dc, double Area);

    /// <summary>Trecho de aresta já classificado, em coordenadas da vista.</summary>
    private readonly record struct Run(P2 A, P2 B, bool Visible, ProjectedEdgeKind Kind);

    /// <param name="mesh">Malha no referencial em que a vista é pedida (o da peça, normalmente).</param>
    public static HiddenLineResult Project(TriangleMesh mesh, ProjectionBasis basis, HiddenLineOptions? options = null)
    {
        options ??= new HiddenLineOptions();
        if (mesh.TriangleCount > options.MaxTriangles)
            return HiddenLineResult.Refused($"A malha tem {mesh.TriangleCount} triângulos (limite {options.MaxTriangles}).", mesh.TriangleCount);

        var graph = MeshEdgeGraph.Build(mesh, options.CreaseAngleDegrees);
        var triangles = ProjectTriangles(graph, basis);
        var bounds = BoundsOf(graph, basis);
        if (graph.TriangleCount == 0) return new HiddenLineResult(Array.Empty<ViewLine>(), bounds, 0);

        double diagonal = Math.Max(bounds.Diagonal, 1e-9);
        var grid = new TriangleGrid(triangles, bounds);
        double depthEps = diagonal * options.DepthToleranceFraction;
        double step = diagonal * options.SampleStepFraction;

        var runs = new List<Run>();
        foreach (var edge in graph.Edges)
        {
            var kind = MeshEdgeClassifier.Classify(graph, edge, null, basis.W);
            if (kind == null) continue;

            var a = graph.Vertex(edge.A);
            var b = graph.Vertex(edge.B);
            P2 pa = basis.Project(a), pb = basis.Project(b);
            if (pa.DistanceTo(pb) < diagonal * 1e-9) continue;   // aresta vista de ponta

            bool Hidden(double t)
            {
                var p = a + (b - a) * t;
                return grid.IsOccluded(basis.Project(p), basis.Depth(p), depthEps, edge.Triangles);
            }

            int n = Math.Clamp((int)Math.Ceiling(pa.DistanceTo(pb) / step), 2, options.MaxSamplesPerEdge);
            AddRuns(runs, pa, pb, kind.Value, n, Hidden);
        }

        var cleaned = RemoveCoincident(runs, diagonal * 1e-5);
        var lines = new List<ViewLine>();
        foreach (var group in cleaned.GroupBy(r => (r.Visible, r.Kind)).OrderBy(g => g.Key.Visible ? 0 : 1).ThenBy(g => g.Key.Kind))
        {
            var segments = group.Select(r => (new Point3D(r.A.U, r.A.V), new Point3D(r.B.U, r.B.V)));
            foreach (var shape in ReferencePolylines.Build(PlaneReferenceKind.Projected, segments))
                lines.Add(new ViewLine(group.Key.Visible, group.Key.Kind, shape));
        }

        return new HiddenLineResult(lines, bounds, graph.TriangleCount);
    }

    /// <summary>Amostra a aresta no meio de n fatias; cada troca de estado é refinada por bissecção.</summary>
    private static void AddRuns(List<Run> runs, P2 pa, P2 pb, ProjectedEdgeKind kind, int n, Func<double, bool> hidden)
    {
        double T(int i) => (i + 0.5) / n;
        P2 At(double t) => pa + (pb - pa) * t;

        bool state = hidden(T(0));
        double start = 0;
        for (int i = 1; i < n; i++)
        {
            bool next = hidden(T(i));
            if (next == state) continue;

            double lo = T(i - 1), hi = T(i);
            for (int k = 0; k < BisectionSteps; k++)
            {
                double mid = (lo + hi) / 2;
                if (hidden(mid) == state) lo = mid; else hi = mid;
            }
            double cut = (lo + hi) / 2;
            runs.Add(new Run(At(start), At(cut), !state, kind));
            start = cut;
            state = next;
        }
        runs.Add(new Run(At(start), pb, !state, kind));
    }

    /// <summary>
    /// Duas arestas 3D podem cair na mesma reta da tela (as de trás de uma caixa atrás das da frente).
    /// Oculto sob visível sai; visível ou oculto repetido vira um só. Agrupa por reta e opera intervalos.
    /// </summary>
    private static List<Run> RemoveCoincident(List<Run> runs, double tolerance)
    {
        var result = new List<Run>();
        var lines = new List<(P2 Origin, P2 Dir, P2 Normal, double Offset, List<Run> Members)>();

        foreach (var run in runs)
        {
            var d = run.B - run.A;
            double len = d.Length;
            if (len < tolerance) continue;
            var dir = d * (1 / len);
            if (dir.U < 0 || (dir.U == 0 && dir.V < 0)) dir = dir * -1;
            var normal = new P2(-dir.V, dir.U);
            double offset = normal.U * run.A.U + normal.V * run.A.V;

            int match = lines.FindIndex(l =>
                Math.Abs(Dot(l.Normal, run.A) - l.Offset) < tolerance &&
                Math.Abs(Dot(l.Normal, run.B) - l.Offset) < tolerance);
            if (match < 0) lines.Add((run.A, dir, normal, offset, new List<Run> { run }));
            else lines[match].Members.Add(run);
        }

        foreach (var (origin, dir, _, _, members) in lines)
        {
            var visible = Union(members.Where(m => m.Visible), origin, dir, tolerance);
            var hidden = Subtract(Union(members.Where(m => !m.Visible), origin, dir, tolerance), visible, tolerance);

            foreach (var (t0, t1, kind) in visible) result.Add(new Run(origin + dir * t0, origin + dir * t1, true, kind));
            foreach (var (t0, t1, kind) in hidden) result.Add(new Run(origin + dir * t0, origin + dir * t1, false, kind));
        }

        return result;
    }

    private static double Dot(P2 a, P2 b) => a.U * b.U + a.V * b.V;

    /// <summary>União dos intervalos ao longo da reta; quina vence borda, que vence silhueta.</summary>
    private static List<(double T0, double T1, ProjectedEdgeKind Kind)> Union(IEnumerable<Run> members, P2 origin, P2 dir, double tolerance)
    {
        var intervals = members
            .Select(m =>
            {
                double t0 = Dot(m.A - origin, dir), t1 = Dot(m.B - origin, dir);
                return (T0: Math.Min(t0, t1), T1: Math.Max(t0, t1), m.Kind);
            })
            .OrderBy(i => i.T0)
            .ToList();

        var merged = new List<(double T0, double T1, ProjectedEdgeKind Kind)>();
        foreach (var i in intervals)
        {
            if (merged.Count > 0 && i.T0 <= merged[^1].T1 + tolerance)
            {
                var last = merged[^1];
                merged[^1] = (last.T0, Math.Max(last.T1, i.T1), Stronger(last.Kind, i.Kind));
            }
            else merged.Add(i);
        }
        return merged;
    }

    private static ProjectedEdgeKind Stronger(ProjectedEdgeKind a, ProjectedEdgeKind b) =>
        a == ProjectedEdgeKind.Crease || b == ProjectedEdgeKind.Crease ? ProjectedEdgeKind.Crease
        : a == ProjectedEdgeKind.Border || b == ProjectedEdgeKind.Border ? ProjectedEdgeKind.Border
        : ProjectedEdgeKind.Silhouette;

    private static List<(double T0, double T1, ProjectedEdgeKind Kind)> Subtract(
        List<(double T0, double T1, ProjectedEdgeKind Kind)> from,
        List<(double T0, double T1, ProjectedEdgeKind Kind)> cut,
        double tolerance)
    {
        var result = new List<(double, double, ProjectedEdgeKind)>();
        foreach (var (t0, t1, kind) in from)
        {
            double cursor = t0;
            foreach (var (c0, c1, _) in cut)
            {
                if (c1 <= cursor || c0 >= t1) continue;
                if (c0 - cursor > tolerance) result.Add((cursor, c0, kind));
                cursor = Math.Max(cursor, c1);
            }
            if (t1 - cursor > tolerance) result.Add((cursor, t1, kind));
        }
        return result;
    }

    private static Triangle2D?[] ProjectTriangles(MeshEdgeGraph graph, ProjectionBasis basis)
    {
        var result = new Triangle2D?[graph.TriangleCount];
        for (int t = 0; t < graph.TriangleCount; t++)
        {
            var (a, b, c) = graph.Triangle(t);
            P2 pa = basis.Project(a), pb = basis.Project(b), pc = basis.Project(c);
            double area = (pb.U - pa.U) * (pc.V - pa.V) - (pc.U - pa.U) * (pb.V - pa.V);
            // Triângulo visto de lado não esconde nada.
            if (Math.Abs(area) < 1e-12) continue;
            result[t] = new Triangle2D(pa, pb, pc, basis.Depth(a), basis.Depth(b), basis.Depth(c), area);
        }
        return result;
    }

    private static ViewBounds BoundsOf(MeshEdgeGraph graph, ProjectionBasis basis)
    {
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        double minW = double.MaxValue, maxW = double.MinValue;
        for (int t = 0; t < graph.TriangleCount; t++)
        {
            var (a, b, c) = graph.Triangle(t);
            foreach (var p in new[] { a, b, c })
            {
                var q = basis.Project(p);
                double w = basis.Depth(p);
                minU = Math.Min(minU, q.U); maxU = Math.Max(maxU, q.U);
                minV = Math.Min(minV, q.V); maxV = Math.Max(maxV, q.V);
                minW = Math.Min(minW, w); maxW = Math.Max(maxW, w);
            }
        }
        return graph.TriangleCount == 0 ? default : new ViewBounds(minU, maxU, minV, maxV, minW, maxW);
    }

    /// <summary>Grade uniforme em (U, V): cada célula guarda os triângulos cuja caixa projetada a toca.</summary>
    private sealed class TriangleGrid
    {
        private readonly Triangle2D?[] _triangles;
        private readonly List<int>[] _cells;
        private readonly int _size;
        private readonly double _minU, _minV, _cellU, _cellV;

        public TriangleGrid(Triangle2D?[] triangles, ViewBounds bounds)
        {
            _triangles = triangles;
            _size = Math.Clamp((int)Math.Sqrt(triangles.Length), 1, 512);
            _minU = bounds.MinU;
            _minV = bounds.MinV;
            _cellU = Math.Max((bounds.MaxU - bounds.MinU) / _size, 1e-12);
            _cellV = Math.Max((bounds.MaxV - bounds.MinV) / _size, 1e-12);
            _cells = new List<int>[_size * _size];

            for (int t = 0; t < triangles.Length; t++)
            {
                if (triangles[t] is not { } tri) continue;
                int u0 = CellU(Math.Min(tri.A.U, Math.Min(tri.B.U, tri.C.U)));
                int u1 = CellU(Math.Max(tri.A.U, Math.Max(tri.B.U, tri.C.U)));
                int v0 = CellV(Math.Min(tri.A.V, Math.Min(tri.B.V, tri.C.V)));
                int v1 = CellV(Math.Max(tri.A.V, Math.Max(tri.B.V, tri.C.V)));
                for (int u = u0; u <= u1; u++)
                    for (int v = v0; v <= v1; v++)
                        (_cells[v * _size + u] ??= new List<int>()).Add(t);
            }
        }

        private int CellU(double u) => Math.Clamp((int)((u - _minU) / _cellU), 0, _size - 1);
        private int CellV(double v) => Math.Clamp((int)((v - _minV) / _cellV), 0, _size - 1);

        /// <summary>Algum triângulo (fora dos <paramref name="own"/>) cobre o ponto e está à frente dele?</summary>
        public bool IsOccluded(P2 point, double depth, double depthEps, IReadOnlyList<int> own)
        {
            var cell = _cells[CellV(point.V) * _size + CellU(point.U)];
            if (cell == null) return false;

            foreach (int t in cell)
            {
                if (Contains(own, t)) continue;
                var tri = _triangles[t]!;

                double l1 = ((tri.B.U - point.U) * (tri.C.V - point.V) - (tri.C.U - point.U) * (tri.B.V - point.V)) / tri.Area;
                double l2 = ((tri.C.U - point.U) * (tri.A.V - point.V) - (tri.A.U - point.U) * (tri.C.V - point.V)) / tri.Area;
                double l3 = 1 - l1 - l2;
                if (l1 < -BaryTolerance || l2 < -BaryTolerance || l3 < -BaryTolerance) continue;

                double triDepth = l1 * tri.Da + l2 * tri.Db + l3 * tri.Dc;
                if (triDepth > depth + depthEps) return true;
            }
            return false;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }
    }
}
