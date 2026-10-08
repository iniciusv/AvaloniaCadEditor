using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ReferenceGuides;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.ReferenceGuides;

/// <summary>
/// <see cref="IReferenceGuideService"/> padrão: um adaptador de apresentação sobre a análise de
/// <see cref="AvaloniaCadEditor.Analysis"/>. Cada feature de topo da cena vira um sujeito em
/// coordenadas do mundo; <see cref="FactExtractor"/> percorre as árvores (inclusive dentro das
/// peças, acumulando os Placements) e <see cref="RelationAnalyzer"/> infere os alinhamentos. Aqui
/// só se transforma o resultado em linhas e cotas 3D, no plano "de cima" de cada grupo de direção:
/// <list type="bullet">
/// <item>alinhamentos com 2+ furos → <see cref="GuideKind.HoleRow"/>; com parede → <see cref="GuideKind.Wall"/>;</item>
/// <item>quase-alinhamentos → linhas vermelhas e o rótulo "X: a (×n) vs b (×m)  Δd";</item>
/// <item>passo entre fileiras de furos ("3× 10") e distância de cada fileira à parede mais próxima;</item>
/// <item>centros de arco dos perfis e planos de simetria.</item>
/// </list>
/// </summary>
public sealed class ReferenceGuideService : IReferenceGuideService
{
    public ReferenceGuideSet Analyze(
        IReadOnlyList<IFeature> features,
        ReferenceGuideOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new ReferenceGuideOptions();
        if (features.Count == 0) return ReferenceGuideSet.Empty;

        var subjects = features.Select(f => new AnalysisSubject(f, Placement.Identity, f.Name, f.Id));
        var facts = FactExtractor.Extract(subjects);
        if (facts.Count == 0) return ReferenceGuideSet.Empty;

        cancellationToken.ThrowIfCancellationRequested();
        var relations = RelationAnalyzer.Analyze(facts, opts.Analysis, cancellationToken);

        var lines = new List<GuideLine>();
        var dimensions = new List<GuideDimension>();
        var tops = new Dictionary<DirectionGroup, double>();

        foreach (var group in relations.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.CandidatesU.Count == 0 && group.CandidatesV.Count == 0) continue;

            double top = SharedTop(group);
            tops[group] = top;
            var view = new GroupView(group, top, opts.LineMargin);

            var uAmbiguous = group.ClustersU.NearMisses.SelectMany(n => n).ToHashSet();
            var vAmbiguous = group.ClustersV.NearMisses.SelectMany(n => n).ToHashSet();

            view.AxisLines(group.ClustersU.Tight.Where(c => !uAmbiguous.Contains(c)), alongU: true, lines);
            view.AxisLines(group.ClustersV.Tight.Where(c => !vAmbiguous.Contains(c)), alongU: false, lines);
            view.NearMisses(group.ClustersU.NearMisses, alongU: true, lines, dimensions);
            view.NearMisses(group.ClustersV.NearMisses, alongU: false, lines, dimensions);
            view.RowDimensions(group.ClustersU.Tight, alongU: true, opts.Analysis, dimensions);
            view.RowDimensions(group.ClustersV.Tight, alongU: false, opts.Analysis, dimensions);
            view.ArcCenters(lines);
        }

        Symmetry(relations, tops, opts, lines);
        return new ReferenceGuideSet(lines, dimensions);
    }

    /// <summary>Uma altura única, um pouco acima do mais alto do grupo, para que todas as guias e
    /// rótulos do grupo fiquem num plano comum e desobstruído.</summary>
    private static double SharedTop(DirectionGroup group)
    {
        var all = group.CandidatesU.Concat(group.CandidatesV).ToList();
        double maxTop = all.Max(c => c.Top);
        double span = Math.Max(all.Max(c => c.Coordinate) - all.Min(c => c.Coordinate), 1.0);
        return maxTop + Math.Max(span * 0.01, 1e-3);
    }

    /// <summary>
    /// Planos de simetria (da análise, que exige pares de mesmo tipo e tamanho) desenhados no grupo de
    /// direção que contém seus pares, no plano de cima desse grupo.
    /// </summary>
    private static void Symmetry(ModelRelations relations, Dictionary<DirectionGroup, double> tops, ReferenceGuideOptions opts, List<GuideLine> lines)
    {
        foreach (var plane in relations.SymmetryPlanes.Where(p => p.Pairs.Count >= 2 || p.IsGlobal))
        {
            var facts = plane.Pairs.SelectMany(p => new[] { p.A, p.B }).ToHashSet();
            var group = relations.Groups
                .Where(g => tops.ContainsKey(g) && (g.AxisU == plane.Normal || g.AxisV == plane.Normal))
                .OrderByDescending(g => g.Holes.Count(facts.Contains) + g.Walls.Count(facts.Contains))
                .FirstOrDefault();
            if (group == null) continue;

            bool alongU = group.AxisU == plane.Normal;
            var crossCandidates = (alongU ? group.CandidatesU : group.CandidatesV).Where(c => facts.Contains(c.Fact)).ToList();
            if (crossCandidates.Count == 0) crossCandidates = (alongU ? group.CandidatesU : group.CandidatesV).ToList();
            double crossMin = crossCandidates.Min(c => c.Cross) - opts.LineMargin;
            double crossMax = crossCandidates.Max(c => c.Cross) + opts.LineMargin;

            var view = new GroupView(group, tops[group], opts.LineMargin);
            lines.Add(new GuideLine(GuideKind.Symmetry,
                view.Point(plane.Coordinate, crossMin, alongU), view.Point(plane.Coordinate, crossMax, alongU),
                plane.Coordinate, facts.Select(f => f.Id).Distinct().ToList(), Owners(facts)));
        }
    }

    private static IReadOnlyList<Guid> Ids(IEnumerable<PrimitiveFact> facts) => facts.Select(f => f.Id).Distinct().ToList();

    private static IReadOnlyList<Guid> Owners(IEnumerable<PrimitiveFact> facts) => facts.Select(f => f.OwnerId).Distinct().ToList();

    /// <summary>Apresentação de um grupo de direção: pontos no plano de cima e as linhas/cotas.</summary>
    private sealed class GroupView(DirectionGroup group, double top, double margin)
    {
        public Point3D Point(double coordinate, double cross, bool alongU) =>
            alongU ? group.ToPoint(coordinate, cross, top) : group.ToPoint(cross, coordinate, top);

        public void AxisLines(IEnumerable<Cluster> clusters, bool alongU, List<GuideLine> lines)
        {
            foreach (var cluster in clusters)
            {
                GuideKind kind;
                if (cluster.Holes.Count >= 2) kind = GuideKind.HoleRow;
                else if (cluster.Walls.Count >= 1) kind = GuideKind.Wall;
                else continue;

                lines.Add(new GuideLine(kind,
                    Point(cluster.Coordinate, cluster.CrossMin - margin, alongU),
                    Point(cluster.Coordinate, cluster.CrossMax + margin, alongU),
                    cluster.Coordinate, Ids(cluster.Facts), Owners(cluster.Facts)));
            }
        }

        public void NearMisses(IReadOnlyList<IReadOnlyList<Cluster>> neighborhoods, bool alongU, List<GuideLine> lines, List<GuideDimension> dimensions)
        {
            foreach (var neighborhood in neighborhoods)
            {
                var sorted = neighborhood.OrderBy(c => c.Coordinate).ToList();

                // Linha vermelha inteira para cada lado do conflito, não só um toco entre os dois: o
                // toco tem o comprimento do próprio erro (muitas vezes menos de 1 mm) e some na tela.
                foreach (var cluster in sorted)
                {
                    lines.Add(new GuideLine(GuideKind.NearMiss,
                        Point(cluster.Coordinate, cluster.CrossMin - margin, alongU),
                        Point(cluster.Coordinate, cluster.CrossMax + margin, alongU),
                        cluster.Coordinate, Ids(cluster.Facts), Owners(cluster.Facts)));
                }

                for (int i = 1; i < sorted.Count; i++)
                {
                    var a = sorted[i - 1];
                    var b = sorted[i];
                    double diff = b.Coordinate - a.Coordinate;
                    double cross = OverlapMidpoint(a, b);
                    var facts = a.Facts.Concat(b.Facts).ToList();

                    // Mostra quantos candidatos sustentam cada lado, nunca qual está "certo" — isso
                    // depende de intenção de projeto que a análise não vê.
                    string axisLabel = alongU ? "X" : "Y";
                    string label = FormattableString.Invariant(
                        $"{axisLabel}: {a.Coordinate:0.###} (×{a.MemberCount}) vs {b.Coordinate:0.###} (×{b.MemberCount})  Δ{diff:0.###}");
                    dimensions.Add(new GuideDimension(GuideKind.NearMiss, Point(a.Coordinate, cross, alongU), Point(b.Coordinate, cross, alongU),
                        diff, label, Ids(facts), Owners(facts)));
                }
            }
        }

        public void RowDimensions(IReadOnlyList<Cluster> clusters, bool alongU, AnalysisOptions analysis, List<GuideDimension> dimensions)
        {
            // Toda coordenada com pelo menos um furo é uma "fileira" para medir passo, mesmo com um
            // furo só (um furo solto que acompanha o espaçamento dos outros).
            var rows = clusters.Where(c => c.Holes.Count >= 1).OrderBy(c => c.Coordinate).ToList();
            var walls = clusters.Where(c => c.Walls.Count >= 1).ToList();

            var steps = new List<double>();
            for (int i = 1; i < rows.Count; i++)
            {
                var a = rows[i - 1];
                var b = rows[i];
                double step = b.Coordinate - a.Coordinate;
                steps.Add(step);
                double cross = OverlapMidpoint(a, b);
                var facts = a.Facts.Concat(b.Facts).ToList();
                dimensions.Add(new GuideDimension(GuideKind.HoleRow, Point(a.Coordinate, cross, alongU), Point(b.Coordinate, cross, alongU),
                    step, step.ToString("0.###", CultureInfo.InvariantCulture), Ids(facts), Owners(facts)));
            }

            if (steps.Count >= 2 && steps.All(s => Math.Abs(s - steps[0]) <= analysis.AlignTolerance * 10))
            {
                var first = rows[0];
                var last = rows[^1];
                double cross = OverlapMidpoint(first, last);
                var facts = rows.SelectMany(r => r.Facts).ToList();
                dimensions.Add(new GuideDimension(GuideKind.HoleRow, Point(first.Coordinate, cross, alongU), Point(last.Coordinate, cross, alongU),
                    steps[0], FormattableString.Invariant($"{steps.Count}× {steps[0]:0.###}"), Ids(facts), Owners(facts)));
            }

            if (walls.Count == 0) return;
            foreach (var row in rows)
            {
                var nearestWall = walls.OrderBy(w => Math.Abs(w.Coordinate - row.Coordinate)).First();
                double dist = Math.Abs(nearestWall.Coordinate - row.Coordinate);
                if (dist < 1e-9) continue; // a própria fileira coincide com uma parede (ex.: centro de arco)

                double cross = OverlapMidpoint(row, nearestWall);
                var facts = row.Facts.Concat(nearestWall.Facts).ToList();
                dimensions.Add(new GuideDimension(GuideKind.Wall, Point(row.Coordinate, cross, alongU), Point(nearestWall.Coordinate, cross, alongU),
                    dist, dist.ToString("0.###", CultureInfo.InvariantCulture), Ids(facts), Owners(facts)));
            }
        }

        public void ArcCenters(List<GuideLine> lines)
        {
            foreach (var (fact, arc, arcTop) in group.Arcs)
            {
                double s = Math.Max(arc.Radius * 0.08, margin * 0.25);
                var ids = new[] { fact.Id };
                var owners = new[] { fact.OwnerId };
                lines.Add(new GuideLine(GuideKind.ArcCenter, group.ToPoint(arc.CenterU - s, arc.CenterV, arcTop),
                    group.ToPoint(arc.CenterU + s, arc.CenterV, arcTop), arc.CenterU, ids, owners));
                lines.Add(new GuideLine(GuideKind.ArcCenter, group.ToPoint(arc.CenterU, arc.CenterV - s, arcTop),
                    group.ToPoint(arc.CenterU, arc.CenterV + s, arcTop), arc.CenterV, ids, owners));
            }
        }

        private static double OverlapMidpoint(Cluster a, Cluster b)
        {
            double lo = Math.Max(a.CrossMin, b.CrossMin);
            double hi = Math.Min(a.CrossMax, b.CrossMax);
            if (lo <= hi) return (lo + hi) / 2.0;
            return (a.CrossMax + b.CrossMin) / 2.0; // alcances não se sobrepõem: divide o vão
        }
    }
}
