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
            var view = new GroupView(group, top, opts.LineMargin, relations.Units);

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
    /// direção que contém seus pares, no plano de cima desse grupo. Um plano global vai, se der, no
    /// grupo dos furos verticais e atravessa o corpo inteiro: no grupo dos próprios pares ele pode cair
    /// num plano lateral e virar um toco de pé na ponta da peça.
    /// </summary>
    private static void Symmetry(ModelRelations relations, Dictionary<DirectionGroup, double> tops, ReferenceGuideOptions opts, List<GuideLine> lines)
    {
        foreach (var plane in relations.SymmetryPlanes.Where(p => p.UnitPairs >= 2 || p.IsGlobal))
        {
            var facts = plane.Pairs.SelectMany(p => new[] { p.A, p.B }).ToHashSet();
            bool Contains(DirectionGroup g) => tops.ContainsKey(g) && (g.AxisU == plane.Normal || g.AxisV == plane.Normal);

            var overBody = plane.IsGlobal && relations.BodyMin != null && relations.BodyMax != null
                ? relations.Groups.FirstOrDefault(g => g.Normal == FrameAxis.Z && Contains(g))
                : null;
            var group = overBody ?? relations.Groups
                .Where(Contains)
                .OrderByDescending(g => g.Holes.Count(facts.Contains) + g.Walls.Count(facts.Contains))
                .FirstOrDefault();
            if (group == null) continue;

            bool alongU = group.AxisU == plane.Normal;
            double crossMin, crossMax;
            if (overBody != null)
            {
                var crossAxis = (alongU ? group.AxisV : group.AxisU)!.Value;
                crossMin = FrameAxes.Component(relations.BodyMin!.Value, crossAxis) - opts.LineMargin;
                crossMax = FrameAxes.Component(relations.BodyMax!.Value, crossAxis) + opts.LineMargin;
            }
            else
            {
                var crossCandidates = (alongU ? group.CandidatesU : group.CandidatesV).Where(c => facts.Contains(c.Fact)).ToList();
                if (crossCandidates.Count == 0) crossCandidates = (alongU ? group.CandidatesU : group.CandidatesV).ToList();
                crossMin = crossCandidates.Min(c => c.Cross) - opts.LineMargin;
                crossMax = crossCandidates.Max(c => c.Cross) + opts.LineMargin;
            }

            var view = new GroupView(group, tops[group], opts.LineMargin, relations.Units);
            lines.Add(Line(GuideKind.Symmetry,
                view.Point(plane.Coordinate, crossMin, alongU), view.Point(plane.Coordinate, crossMax, alongU),
                plane.Coordinate, facts, relations.Units, plane.Normal.ToString(),
                plane.IsGlobal ? "global" : $"local, {plane.UnitPairs} pares"));
        }
    }

    /// <summary>Única forma de criar uma guia aqui: Ids e nomes saem da mesma lista de fatos, então
    /// ficam sempre na mesma ordem.</summary>
    private static GuideLine Line(GuideKind kind, Point3D start, Point3D end, double coordinate, IEnumerable<PrimitiveFact> facts,
        PositionUnits units, string? axis = null, string? note = null)
    {
        var distinct = facts.DistinctBy(f => f.Id).ToList();
        return new GuideLine(kind, start, end, coordinate, distinct.Select(f => f.Id).ToList(), Owners(distinct))
        {
            SourceNames = distinct.Select(f => f.Name).ToList(),
            MemberNames = units.Names(distinct),
            Axis = axis,
            Note = note
        };
    }

    private static GuideDimension Dimension(GuideKind kind, Point3D from, Point3D to, double value, string label, IEnumerable<PrimitiveFact> facts,
        PositionUnits units)
    {
        var distinct = facts.DistinctBy(f => f.Id).ToList();
        return new GuideDimension(kind, from, to, value, label, distinct.Select(f => f.Id).ToList(), Owners(distinct))
        {
            SourceNames = distinct.Select(f => f.Name).ToList(),
            MemberNames = units.Names(distinct)
        };
    }

    private static IReadOnlyList<Guid> Owners(IEnumerable<PrimitiveFact> facts) => facts.Select(f => f.OwnerId).Distinct().ToList();

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Apresentação de um grupo de direção: pontos no plano de cima e as linhas/cotas.</summary>
    private sealed class GroupView(DirectionGroup group, double top, double margin, PositionUnits units)
    {
        public Point3D Point(double coordinate, double cross, bool alongU) =>
            alongU ? group.ToPoint(coordinate, cross, top) : group.ToPoint(cross, coordinate, top);

        /// <summary>Linha de um alinhamento inteiro, com a margem além dos membros mais externos.</summary>
        private GuideLine ClusterLine(GuideKind kind, Cluster cluster, bool alongU, string? note = null) =>
            Line(kind,
                Point(cluster.Coordinate, cluster.CrossMin - margin, alongU),
                Point(cluster.Coordinate, cluster.CrossMax + margin, alongU),
                cluster.Coordinate, cluster.Facts, units, (alongU ? group.AxisU : group.AxisV)?.ToString(), note);

        /// <summary>Cota entre dois alinhamentos, na transversal comum aos dois.</summary>
        private GuideDimension Between(GuideKind kind, Cluster a, Cluster b, bool alongU, double value, string label)
        {
            double cross = OverlapMidpoint(a, b);
            return Dimension(kind, Point(a.Coordinate, cross, alongU), Point(b.Coordinate, cross, alongU), value, label, a.Facts.Concat(b.Facts), units);
        }

        public void AxisLines(IEnumerable<Cluster> clusters, bool alongU, List<GuideLine> lines)
        {
            foreach (var cluster in clusters)
            {
                // Fileira = 2 ou mais furos DISTINTOS: um furo com rebaixo (dois sólidos coaxiais) ou o
                // furo de um componente não formam fileira consigo mesmos.
                GuideKind kind;
                if (units.Count(cluster.Holes) >= 2) kind = GuideKind.HoleRow;
                else if (cluster.Walls.Count >= 1) kind = GuideKind.Wall;
                else continue;

                lines.Add(ClusterLine(kind, cluster, alongU));
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
                    lines.Add(ClusterLine(GuideKind.NearMiss, cluster, alongU, FormattableString.Invariant($"×{cluster.MemberCount}")));
                }

                for (int i = 1; i < sorted.Count; i++)
                {
                    var a = sorted[i - 1];
                    var b = sorted[i];
                    double diff = b.Coordinate - a.Coordinate;

                    // Mostra quantos candidatos sustentam cada lado, nunca qual está "certo" — isso
                    // depende de intenção de projeto que a análise não vê.
                    string axisLabel = alongU ? "X" : "Y";
                    string label = FormattableString.Invariant(
                        $"{axisLabel}: {a.Coordinate:0.###} (×{a.MemberCount}) vs {b.Coordinate:0.###} (×{b.MemberCount})  Δ{diff:0.###}");
                    dimensions.Add(Between(GuideKind.NearMiss, a, b, alongU, diff, label));
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
                double step = rows[i].Coordinate - rows[i - 1].Coordinate;
                steps.Add(step);
                dimensions.Add(Between(GuideKind.HoleRow, rows[i - 1], rows[i], alongU, step, Number(step)));
            }

            if (steps.Count >= 2 && steps.All(s => Math.Abs(s - steps[0]) <= analysis.AlignTolerance * 10))
            {
                var first = rows[0];
                var last = rows[^1];
                double cross = OverlapMidpoint(first, last);
                dimensions.Add(Dimension(GuideKind.HoleRow, Point(first.Coordinate, cross, alongU), Point(last.Coordinate, cross, alongU),
                    steps[0], FormattableString.Invariant($"{steps.Count}× {steps[0]:0.###}"), rows.SelectMany(r => r.Facts), units));
            }

            if (walls.Count == 0) return;
            foreach (var row in rows)
            {
                var nearestWall = walls.OrderBy(w => Math.Abs(w.Coordinate - row.Coordinate)).First();
                double dist = Math.Abs(nearestWall.Coordinate - row.Coordinate);
                if (dist < 1e-9) continue; // a própria fileira coincide com uma parede (ex.: centro de arco)

                dimensions.Add(Between(GuideKind.Wall, row, nearestWall, alongU, dist, Number(dist)));
            }
        }

        public void ArcCenters(List<GuideLine> lines)
        {
            foreach (var (fact, arc, arcTop) in group.Arcs)
            {
                double s = Math.Max(arc.Radius * 0.08, margin * 0.25);
                var facts = new[] { fact };
                var note = FormattableString.Invariant($"R{arc.Radius:0.###}");
                lines.Add(Line(GuideKind.ArcCenter, group.ToPoint(arc.CenterU - s, arc.CenterV, arcTop),
                    group.ToPoint(arc.CenterU + s, arc.CenterV, arcTop), arc.CenterV, facts, units, group.AxisV?.ToString(), note));
                lines.Add(Line(GuideKind.ArcCenter, group.ToPoint(arc.CenterU, arc.CenterV - s, arcTop),
                    group.ToPoint(arc.CenterU, arc.CenterV + s, arcTop), arc.CenterU, facts, units, group.AxisU?.ToString(), note));
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
