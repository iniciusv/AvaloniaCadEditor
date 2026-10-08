using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Analysis;

/// <summary>
/// Camada 2: infere relações de projeto sobre os fatos — alinhamentos (eixos e planos em comum),
/// quase-alinhamentos, furos coaxiais, padrões lineares com passo e planos de simetria. Pura: só
/// depende dos fatos, então roda em background e é testável com features soltas.
/// </summary>
public static class RelationAnalyzer
{
    public static ModelRelations Analyze(IReadOnlyList<PrimitiveFact> facts, AnalysisOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var opts = options ?? new AnalysisOptions();
        if (facts.Count == 0) return ModelRelations.Empty;

        var groups = BuildGroups(facts, opts);
        cancellationToken.ThrowIfCancellationRequested();

        var frameClusters = new Dictionary<FrameAxis, AxisClusters>();
        foreach (var axis in FrameAxes.All)
        {
            var candidates = groups
                .SelectMany(g => (g.AxisU == axis ? g.CandidatesU : Enumerable.Empty<AxisCandidate>())
                    .Concat(g.AxisV == axis ? g.CandidatesV : Enumerable.Empty<AxisCandidate>()));
            frameClusters[axis] = Clustering.Cluster(candidates, opts);
        }

        var coaxial = groups.SelectMany(g => CoaxialSets(g, opts)).ToList();
        var patterns = groups.SelectMany(g => LinearPatterns(g, opts)).ToList();
        cancellationToken.ThrowIfCancellationRequested();

        var bodies = facts.Where(f => f.Role == PrimitiveRole.Body).ToList();
        Point3D? bodyMin = bodies.Count == 0 ? null : new Point3D(bodies.Min(f => f.BoundsMin.X), bodies.Min(f => f.BoundsMin.Y), bodies.Min(f => f.BoundsMin.Z));
        Point3D? bodyMax = bodies.Count == 0 ? null : new Point3D(bodies.Max(f => f.BoundsMax.X), bodies.Max(f => f.BoundsMax.Y), bodies.Max(f => f.BoundsMax.Z));

        var symmetry = facts.Count <= opts.MaxSymmetryFacts
            ? FrameAxes.All.SelectMany(a => SymmetryPlanes(facts, a, bodyMin, bodyMax, opts)).ToList()
            : new List<SymmetryPlane>();

        return new ModelRelations(facts, groups, frameClusters, coaxial, patterns, symmetry, bodyMin, bodyMax);
    }

    // ------------------------------------------------------------------ Grupos por direção

    private static List<DirectionGroup> BuildGroups(IReadOnlyList<PrimitiveFact> facts, AnalysisOptions opts)
    {
        var byKey = new Dictionary<(long, long, long), (Vector3D Direction, List<PrimitiveFact> Facts)>();
        foreach (var fact in facts)
        {
            var direction = Canonicalize(fact.Axis);
            var key = (Key(direction.X), Key(direction.Y), Key(direction.Z));
            if (!byKey.TryGetValue(key, out var entry))
            {
                entry = (direction, new List<PrimitiveFact>());
                byKey[key] = entry;
            }
            entry.Facts.Add(fact);
        }

        return byKey.Values.Select(e => BuildGroup(e.Direction, e.Facts, opts)).ToList();
    }

    private static long Key(double value) => (long)Math.Round(value * 1000.0);

    /// <summary>Normaliza e vira o sentido para que eixos opostos (furos pelos dois lados) caiam juntos.</summary>
    private static Vector3D Canonicalize(Vector3D direction)
    {
        var n = direction.Normalize();
        double ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
        double dominant = ax >= ay && ax >= az ? n.X : (ay >= az ? n.Y : n.Z);
        return dominant < 0 ? n * -1 : n;
    }

    private static DirectionGroup BuildGroup(Vector3D direction, List<PrimitiveFact> facts, AnalysisOptions opts)
    {
        var normal = FrameAxes.Of(direction);
        FrameAxis? axisU = normal switch { FrameAxis.Z => FrameAxis.X, FrameAxis.X => FrameAxis.Y, FrameAxis.Y => FrameAxis.X, _ => null };
        FrameAxis? axisV = normal switch { FrameAxis.Z => FrameAxis.Y, FrameAxis.X => FrameAxis.Z, FrameAxis.Y => FrameAxis.Z, _ => null };

        Vector3D u, v;
        if (axisU is { } au && axisV is { } av)
        {
            u = FrameAxes.Unit(au);
            v = FrameAxes.Unit(av);
            direction = FrameAxes.Unit(normal!.Value);
        }
        else
        {
            var plane = new WorkPlane(direction, Point3D.Origin);
            u = plane.XAxis;
            v = plane.YAxis;
        }

        var holes = facts.Where(f => f.IsRound && f.Role == PrimitiveRole.Void).Take(opts.MaxHoles).ToList();
        var walls = facts.Where(f => !f.IsRound || f.Role == PrimitiveRole.Body).ToList();

        var group = new GroupBuilder(direction, u, v);
        foreach (var hole in holes) group.AddRoundCenter(hole, ClusterMemberKind.HoleCenter);
        foreach (var wall in walls)
        {
            if (wall.IsRound) group.AddRoundCenter(wall, ClusterMemberKind.ArcCenter);
            else group.AddSection(wall, opts);
        }

        return new DirectionGroup
        {
            Direction = direction,
            U = u,
            V = v,
            Normal = normal,
            AxisU = axisU,
            AxisV = axisV,
            Holes = holes,
            Walls = walls,
            CandidatesU = group.U,
            CandidatesV = group.V,
            ClustersU = Clustering.Cluster(group.U, opts),
            ClustersV = Clustering.Cluster(group.V, opts),
            Arcs = group.Arcs
        };
    }

    private sealed class GroupBuilder(Vector3D direction, Vector3D u, Vector3D v)
    {
        public List<AxisCandidate> U { get; } = new();
        public List<AxisCandidate> V { get; } = new();
        public List<(PrimitiveFact, ArcInfo, double)> Arcs { get; } = new();

        private (double U, double V, double Depth) Local(Point3D p)
        {
            var d = p - Point3D.Origin;
            return (d.DotProduct(u), d.DotProduct(v), d.DotProduct(direction));
        }

        public void AddRoundCenter(PrimitiveFact fact, ClusterMemberKind kind)
        {
            var b = Local(fact.BaseCenter);
            var t = Local(fact.TopCenter);
            double top = Math.Max(b.Depth, t.Depth);
            U.Add(new AxisCandidate(b.U, b.V, top, fact, kind));
            V.Add(new AxisCandidate(b.V, b.U, top, fact, kind));
        }

        public void AddSection(PrimitiveFact fact, AnalysisOptions opts)
        {
            var loop = fact.Profile.Select(Local).ToList();
            if (loop.Count < 2) return;
            double top = fact.SectionVertices.Max(p => Local(p).Depth);

            // Arestas retas alinhadas: uma aresta paralela a V dá uma coordenada em U, e vice-versa.
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                var p1 = loop[i];
                var p2 = loop[(i + 1) % n];
                double du = p2.U - p1.U;
                double dv = p2.V - p1.V;
                double len = Math.Sqrt(du * du + dv * dv);
                if (len < 1e-9) continue;

                if (Math.Abs(du) / len < opts.EdgeAngleTolerance)
                {
                    U.Add(new AxisCandidate(p1.U, p1.V, top, fact, ClusterMemberKind.Edge));
                    U.Add(new AxisCandidate(p2.U, p2.V, top, fact, ClusterMemberKind.Edge));
                }
                else if (Math.Abs(dv) / len < opts.EdgeAngleTolerance)
                {
                    V.Add(new AxisCandidate(p1.V, p1.U, top, fact, ClusterMemberKind.Edge));
                    V.Add(new AxisCandidate(p2.V, p2.U, top, fact, ClusterMemberKind.Edge));
                }
            }

            foreach (var arc in ArcDetection.DetectArcs(loop.Select(p => (p.U, p.V)).ToList(), opts))
            {
                U.Add(new AxisCandidate(arc.CenterU, arc.CenterV, top, fact, ClusterMemberKind.ArcCenter));
                V.Add(new AxisCandidate(arc.CenterV, arc.CenterU, top, fact, ClusterMemberKind.ArcCenter));
                Arcs.Add((fact, arc, top));
            }
        }
    }

    // ------------------------------------------------------------------ Coaxiais e padrões

    private static IEnumerable<CoaxialSet> CoaxialSets(DirectionGroup group, AnalysisOptions opts)
    {
        var holes = group.Holes.Select(h => (Fact: h, Local: group.Local(h.BaseCenter))).ToList();
        var used = new bool[holes.Count];
        for (int i = 0; i < holes.Count; i++)
        {
            if (used[i]) continue;
            var members = new List<int> { i };
            for (int j = i + 1; j < holes.Count; j++)
            {
                if (used[j]) continue;
                if (Math.Abs(holes[i].Local.U - holes[j].Local.U) <= opts.AlignTolerance &&
                    Math.Abs(holes[i].Local.V - holes[j].Local.V) <= opts.AlignTolerance)
                {
                    members.Add(j);
                }
            }

            if (members.Count < 2) continue;
            foreach (var m in members) used[m] = true;
            yield return new CoaxialSet(group, holes[i].Local.U, holes[i].Local.V,
                members.Select(m => holes[m].Fact).OrderBy(f => f.MaxRadius).ToList());
        }
    }

    private static IEnumerable<LinearPattern> LinearPatterns(DirectionGroup group, AnalysisOptions opts)
    {
        var holes = group.Holes.Select(h => (Fact: h, Local: group.Local(h.BaseCenter))).ToList();
        foreach (bool alongU in new[] { true, false })
        {
            double Along((PrimitiveFact Fact, (double U, double V, double Depth) Local) h) => alongU ? h.Local.U : h.Local.V;
            double Cross((PrimitiveFact Fact, (double U, double V, double Depth) Local) h) => alongU ? h.Local.V : h.Local.U;

            // Mesma transversal (corrida de valores a menos da tolerância) e mesmo diâmetro.
            var byCross = holes.OrderBy(Cross).ToList();
            var runs = new List<List<(PrimitiveFact Fact, (double U, double V, double Depth) Local)>>();
            foreach (var h in byCross)
            {
                if (runs.Count > 0 && Cross(h) - Cross(runs[^1][^1]) <= opts.AlignTolerance) runs[^1].Add(h);
                else runs.Add(new() { h });
            }

            foreach (var run in runs)
            {
                foreach (var byDiameter in run.GroupBy(h => Math.Round(h.Fact.MaxRadius * 2 / opts.AlignTolerance)))
                {
                    var sorted = byDiameter.OrderBy(Along).ToList();
                    var positions = new List<double>();
                    var members = new List<PrimitiveFact>();
                    foreach (var h in sorted)
                    {
                        if (positions.Count > 0 && Along(h) - positions[^1] <= opts.AlignTolerance) continue;
                        positions.Add(Along(h));
                        members.Add(h.Fact);
                    }
                    if (positions.Count < 2) continue;

                    var steps = positions.Zip(positions.Skip(1), (a, b) => b - a).ToList();
                    double? pitch = steps.All(s => Math.Abs(s - steps[0]) <= opts.AlignTolerance * 10) ? steps.Average() : null;
                    yield return new LinearPattern(group, alongU, sorted.Average(Cross), sorted[0].Fact.MaxRadius * 2,
                        positions, members, sorted.Select(h => h.Fact).ToList(), steps, pitch);
                }
            }
        }
    }

    // ------------------------------------------------------------------ Simetria

    private static IEnumerable<SymmetryPlane> SymmetryPlanes(IReadOnlyList<PrimitiveFact> facts, FrameAxis axis,
        Point3D? bodyMin, Point3D? bodyMax, AnalysisOptions opts)
    {
        double Mid(PrimitiveFact f) => (FrameAxes.Component(f.BoundsMin, axis) + FrameAxes.Component(f.BoundsMax, axis)) / 2.0;

        var candidates = new List<(double Center, SymmetryPair Pair)>();
        for (int i = 0; i < facts.Count; i++)
        {
            for (int j = i + 1; j < facts.Count; j++)
            {
                if (Mirrors(facts[i], facts[j], axis, opts))
                {
                    candidates.Add(((Mid(facts[i]) + Mid(facts[j])) / 2.0, new SymmetryPair(facts[i], facts[j])));
                }
            }
        }
        if (candidates.Count == 0) yield break;

        double? bodyMid = bodyMin is { } bmin && bodyMax is { } bmax
            ? (FrameAxes.Component(bmin, axis) + FrameAxes.Component(bmax, axis)) / 2.0
            : null;

        candidates.Sort((a, b) => a.Center.CompareTo(b.Center));
        var runs = new List<List<(double Center, SymmetryPair Pair)>>();
        foreach (var c in candidates)
        {
            if (runs.Count > 0 && c.Center - runs[^1][^1].Center <= opts.SymmetryTolerance) runs[^1].Add(c);
            else runs.Add(new() { c });
        }

        foreach (var run in runs)
        {
            double center = run.Average(c => c.Center);
            var pairs = run.Select(c => c.Pair).ToList();
            bool bodyCentered = bodyMid is { } m && Math.Abs(m - center) <= opts.AlignTolerance;
            if (pairs.Count < 2 && !bodyCentered) continue;

            var paired = pairs.SelectMany(p => new[] { p.A, p.B }).ToHashSet();
            var onPlane = facts.Where(f => !paired.Contains(f) && Math.Abs(Mid(f) - center) <= opts.AlignTolerance).ToList();
            bool isGlobal = bodyCentered && facts.All(f => paired.Contains(f) || onPlane.Contains(f));

            yield return new SymmetryPlane(axis, center, pairs, onPlane, isGlobal);
        }
    }

    /// <summary>
    /// <paramref name="a"/> e <paramref name="b"/> são o espelho um do outro por um plano
    /// perpendicular a <paramref name="axis"/>: mesmo tipo, papel e tamanho, eixos espelhados e caixas
    /// iguais nos outros dois eixos. Casar só coordenadas (uma borda com um centro de furo) não conta.
    /// </summary>
    private static bool Mirrors(PrimitiveFact a, PrimitiveFact b, FrameAxis axis, AnalysisOptions opts)
    {
        double tol = opts.AlignTolerance;
        if (a.Kind != b.Kind || a.Role != b.Role) return false;
        if (Math.Abs(a.Height - b.Height) > tol) return false;
        if (a.IsRound)
        {
            if (Math.Abs(a.Radius - b.Radius) > tol || Math.Abs(a.TopRadius - b.TopRadius) > tol) return false;
        }
        else if (a.Profile.Count != b.Profile.Count)
        {
            return false;
        }

        // Eixo de a espelhado (componente do eixo de simetria invertida) paralelo ao eixo de b.
        var mirrored = new Vector3D(
            axis == FrameAxis.X ? -a.Axis.X : a.Axis.X,
            axis == FrameAxis.Y ? -a.Axis.Y : a.Axis.Y,
            axis == FrameAxis.Z ? -a.Axis.Z : a.Axis.Z);
        if (Math.Abs(Math.Abs(mirrored.DotProduct(b.Axis)) - 1) > 1e-6) return false;

        foreach (var other in FrameAxes.All)
        {
            double aMin = FrameAxes.Component(a.BoundsMin, other), aMax = FrameAxes.Component(a.BoundsMax, other);
            double bMin = FrameAxes.Component(b.BoundsMin, other), bMax = FrameAxes.Component(b.BoundsMax, other);
            if (other == axis)
            {
                // Mesmo comprimento ao longo do eixo e centros distintos (senão não é um par).
                if (Math.Abs((aMax - aMin) - (bMax - bMin)) > tol) return false;
                if (Math.Abs((aMin + aMax) - (bMin + bMax)) / 2.0 <= tol) return false;
            }
            else if (Math.Abs(aMin - bMin) > tol || Math.Abs(aMax - bMax) > tol)
            {
                return false;
            }
        }

        return true;
    }
}
