using AvaloniaCadEditor.Analysis;

namespace AvaloniaCadEditor.Drafting;

/// <summary>
/// Decide QUAIS cotas uma vista leva, com hierarquia — o <see cref="DimensionLayout"/> decide depois
/// ONDE cada linha fica. Para cada eixo da vista (U → cotas horizontais, abaixo; V → verticais, à
/// esquerda):
/// <list type="number">
/// <item><b>Nível 1, cadeias:</b> elementos na mesma linha transversal (ex.: furos na mesma altura)
/// formam uma cadeia a partir da borda: borda → 1º, 1º → 2º... Um padrão de passo constante vira uma
/// cota só, "3× 20 (=60)". A cadeia termina no último elemento (sem fechar até a outra borda).</item>
/// <item><b>Nível 2, localização:</b> o que sobrou (elementos sozinhos na sua linha) numa cadeia única.</item>
/// <item><b>Nível 3, totais:</b> largura e altura, por fora.</item>
/// </list>
/// Com simetria global no eixo, os pares espelhados são cotados pela distância entre si, centrados
/// na linha de simetria, e o que está sobre ela não leva cota naquele eixo. Os tamanhos (furo visto de
/// lado, chave de prisma) ficam do outro lado da peça; os diâmetros de frente, em chamadas, com os
/// furos coaxiais empilhados ("Ø3,4 / Ø7,2").
/// Peças aninhadas (componentes, ex.: FuroPorca) são caixas-pretas: localizadas pela referência
/// (o eixo) e identificadas por uma chamada com o rótulo, sem as medidas internas.
/// </summary>
public static class DimensionPlanner
{
    public sealed record Plan(IReadOnlyList<LayoutGroup> Groups, IReadOnlyList<DraftDimension> Leaders, IReadOnlyList<CenterLine> CenterLines);

    /// <param name="options">De onde vem a tolerância de "mesma coordenada" — a mesma que a análise
    /// usou para agrupar, para que um alinhamento que ela reconhece o desenho também reconheça.</param>
    public static Plan Build(IReadOnlyList<ViewEntity> entities, ModelRelations relations, ViewBasis basis, P2 partMin, P2 partMax, double gap,
        AnalysisOptions? options = null)
    {
        var context = new Context(entities, relations, basis, partMin, partMax, gap, (options ?? new AnalysisOptions()).AlignTolerance);
        var groups = new List<LayoutGroup>();
        groups.AddRange(context.PlanAxis(horizontal: true));
        groups.AddRange(context.PlanAxis(horizontal: false));
        groups.AddRange(context.SideSizes());
        return new Plan(groups, context.FrontDiameters().Concat(context.ComponentCallouts()).ToList(), context.CenterLines());
    }

    /// <summary>Um ponto cotável ao longo de um eixo, com os sólidos que o definem.</summary>
    private sealed class Mark(double at, double extCross, double rowCross, string source, PrimitiveFact? fact)
    {
        public double At { get; } = at;

        /// <summary>Coordenada transversal do centro do sólido: marcas com a mesma formam uma "linha" da peça.</summary>
        public double RowCross { get; } = rowCross;

        /// <summary>Coordenada transversal de onde sai a linha de chamada (a mais perto da linha de cota).</summary>
        public double ExtCross { get; set; } = extCross;

        public List<string> Sources { get; } = [source];
        public List<PrimitiveFact> Facts { get; } = fact == null ? [] : [fact];

        public void Absorb(Mark other)
        {
            foreach (var s in other.Sources) if (!Sources.Contains(s)) Sources.Add(s);
            foreach (var f in other.Facts) if (!Facts.Contains(f)) Facts.Add(f);
            ExtCross = Math.Min(ExtCross, other.ExtCross);
        }
    }

    private sealed class Context(IReadOnlyList<ViewEntity> entities, ModelRelations relations, ViewBasis basis, P2 partMin, P2 partMax, double gap, double tol)
    {
        /// <summary>Duas coordenadas a menos que isto são a mesma (<see cref="AnalysisOptions.AlignTolerance"/>).</summary>
        private double Tol => tol;

        private readonly FrameAxis _axisU = FrameAxes.Of(basis.U)!.Value;
        private readonly FrameAxis _axisV = FrameAxes.Of(basis.V)!.Value;
        private double SignU => FrameAxes.Component(basis.U, _axisU);
        private double SignV => FrameAxes.Component(basis.V, _axisV);

        private IEnumerable<ViewEntity> Voids => entities.Where(e => e.Role == PrimitiveRole.Void);

        /// <summary>Vazios do próprio sólido — sem os membros de peças aninhadas, que não são cotados por dentro.</summary>
        private IEnumerable<ViewEntity> OwnVoids => Voids.Where(e => !IsComponentMember(e));

        /// <summary>A entidade é parte de uma peça aninhada (componente): caixa-preta no desenho.</summary>
        private static bool IsComponentMember(ViewEntity e) => e.Fact?.Component != null;

        // ------------------------------------------------------------------ Simetria

        /// <summary>Coordenada (na vista) da simetria global perpendicular ao eixo medido, se houver e
        /// se bater com o contorno real da peça.</summary>
        private double? Symmetry(bool horizontal)
        {
            var axis = horizontal ? _axisU : _axisV;
            var plane = relations.GlobalSymmetry(axis);
            if (plane == null) return null;
            double m = (horizontal ? SignU : SignV) * plane.Coordinate;
            double mid = horizontal ? (partMin.U + partMax.U) / 2 : (partMin.V + partMax.V) / 2;
            return Math.Abs(m - mid) <= Math.Max(Tol, gap * 0.01) ? m : null;
        }

        private bool KeepCenter(P2 p) =>
            (Symmetry(true) is not { } mu || p.U <= mu + Tol) && (Symmetry(false) is not { } mv || p.V <= mv + Tol);

        // ------------------------------------------------------------------ Pontos cotáveis

        private static double Along(P2 p, bool horizontal) => horizontal ? p.U : p.V;
        private static double Cross(P2 p, bool horizontal) => horizontal ? p.V : p.U;

        private List<Mark> Marks(bool horizontal)
        {
            double lo = Along(partMin, horizontal), hi = Along(partMax, horizontal);
            double crossLo = Cross(partMin, horizontal), crossHi = Cross(partMax, horizontal);
            bool Inside(double v) => v > lo + Tol && v < hi - Tol;
            double ClampCross(double c) => Math.Clamp(c, crossLo, crossHi);

            var marks = new List<Mark>();
            foreach (var e in entities)
            {
                if (IsComponentMember(e)) continue;

                double eLo = Along(e.Min, horizontal), eHi = Along(e.Max, horizontal);
                double cLo = ClampCross(Cross(e.Min, horizontal)), cHi = ClampCross(Cross(e.Max, horizontal));
                double center = (eLo + eHi) / 2;

                double row = (cLo + cHi) / 2;
                void AddCenter(double at) => marks.Add(new Mark(at, cLo, row, e.SourceName, e.Fact));
                void AddEdge(double at, bool maxSide)
                {
                    if (Inside(at) && !IsSeam(e, at, maxSide, horizontal, row))
                    {
                        marks.Add(new Mark(at, cLo, row, e.SourceName, e.Fact));
                    }
                }

                switch (e)
                {
                    case ViewCircle circle when e.Role == PrimitiveRole.Void:
                        double circleCross = ClampCross(Cross(circle.Center, horizontal));
                        marks.Add(new Mark(Along(circle.Center, horizontal), circleCross, circleCross, e.SourceName, e.Fact));
                        break;

                    case ViewRect { Axis: not CenterAxis.None } rect when e.Role == PrimitiveRole.Void:
                        // Furo visto de lado: posição pela linha de centro se ela cruza o eixo medido;
                        // se corre ao longo dele, pelas pontas (profundidade de furo cego).
                        bool centerLineCrosses = horizontal ? rect.Axis == CenterAxis.Vertical : rect.Axis == CenterAxis.Horizontal;
                        if (centerLineCrosses) AddCenter(center);
                        else { AddEdge(eLo, false); AddEdge(eHi, true); }
                        break;

                    case ViewPolygon when e.Role == PrimitiveRole.Void && horizontal:
                        AddCenter(center);
                        break;

                    case ViewPolygon polygon when e.Role == PrimitiveRole.Body:
                        foreach (var at in polygon.Points.Select(p => Along(p, horizontal)).Distinct())
                        {
                            if (Inside(at)) marks.Add(new Mark(at, cLo, row, e.SourceName, e.Fact));
                        }
                        break;

                    case ViewCircle:
                        break;

                    default:
                        AddEdge(eLo, false);
                        AddEdge(eHi, true);
                        break;
                }
            }

            marks.AddRange(ComponentMarks(horizontal, lo, hi, ClampCross));
            return marks;
        }

        /// <summary>
        /// Uma peça aninhada é cotada só pela referência: o eixo (visto de frente, o centro nos dois
        /// eixos; de lado, a linha de centro no eixo que ela cruza) ou, sem eixo, o centro da caixa.
        /// A posição AO LONGO do eixo e as medidas internas são parâmetros da peça e ficam de fora.
        /// </summary>
        private IEnumerable<Mark> ComponentMarks(bool horizontal, double lo, double hi, Func<double, double> clampCross)
        {
            foreach (var c in relations.Components)
            {
                var (bMin, bMax) = ProjectedBounds(c);
                var fact = c.Principal ?? c.Members[0];
                double cLo = clampCross(Cross(bMin, horizontal)), cHi = clampCross(Cross(bMax, horizontal));
                double at;
                double ext, row;

                if (c.Axis is { } axis)
                {
                    var datum = basis.Project(c.AxisPoint);
                    var alignment = AxisAlignment(axis);
                    if (alignment == 0)
                    {
                        at = Along(datum, horizontal);
                        ext = row = clampCross(Cross(datum, horizontal));
                    }
                    else if (alignment > 0 && (alignment == 1) != horizontal)
                    {
                        // Linha de centro perpendicular ao eixo medido: a posição dela.
                        at = Along(datum, horizontal);
                        ext = cLo;
                        row = (cLo + cHi) / 2;
                    }
                    else
                    {
                        continue; // eixo ao longo do eixo medido (ou oblíquo): nada a localizar aqui
                    }
                }
                else
                {
                    at = (Along(bMin, horizontal) + Along(bMax, horizontal)) / 2;
                    ext = cLo;
                    row = (cLo + cHi) / 2;
                }

                if (at < lo - Tol || at > hi + Tol) continue;
                yield return new Mark(at, ext, row, c.Ref.Name, fact);
            }
        }

        /// <summary>0 = eixo perpendicular à vista (visto de frente), 1 = ao longo de U, 2 = ao longo de V, -1 = oblíquo.</summary>
        private int AxisAlignment(AvaloniaCadEditor.Domain.ValueObjects.Vector3D axis)
        {
            var d = axis.Normalize();
            if (Math.Abs(Math.Abs(d.DotProduct(basis.W)) - 1) < 1e-6) return 0;
            if (Math.Abs(Math.Abs(d.DotProduct(basis.U)) - 1) < 1e-6) return 1;
            if (Math.Abs(Math.Abs(d.DotProduct(basis.V)) - 1) < 1e-6) return 2;
            return -1;
        }

        private (P2 Min, P2 Max) ProjectedBounds(ComponentDatum c)
        {
            var a = basis.Project(c.BoundsMin);
            var b = basis.Project(c.BoundsMax);
            return (new P2(Math.Min(a.U, b.U), Math.Min(a.V, b.V)), new P2(Math.Max(a.U, b.U), Math.Max(a.V, b.V)));
        }

        /// <summary>
        /// Uma chamada por rótulo de componente ("3× FuroPorca M3 Axial"), saindo do círculo do eixo
        /// quando ele é visto de frente, ou da referência.
        /// </summary>
        public IReadOnlyList<DraftDimension> ComponentCallouts()
        {
            var callouts = new List<DraftDimension>();
            foreach (var group in relations.Components.GroupBy(c => c.Ref.Label))
            {
                var located = group
                    .Select(c => (Component: c, Datum: CalloutAnchor(c)))
                    .ToList();
                var rep = located
                    .Where(x => KeepCenter(x.Datum))
                    .OrderBy(x => x.Datum.U).ThenBy(x => x.Datum.V)
                    .FirstOrDefault();
                if (rep.Component == null) rep = located[0];

                int count = located.Select(x => (Math.Round(x.Datum.U / Tol), Math.Round(x.Datum.V / Tol))).Distinct().Count();
                string text = (count > 1 ? $"{count}× " : "") + group.Key;

                var circle = entities.OfType<ViewCircle>().FirstOrDefault(e => rep.Component.Principal != null && e.Fact == rep.Component.Principal)
                             ?? new ViewCircle(rep.Component.Ref.Name, PrimitiveRole.Void, rep.Datum, gap * 0.2);
                var leader = DimensionFactory.Diameter(circle, $"c:{group.Key}", text,
                    group.Select(c => c.Ref.Name).ToList(), isManual: false);
                callouts.Add(leader with { Kind = DimensionKind.Callout, Value = 0 });
            }
            return callouts;
        }

        /// <summary>
        /// De onde sai a chamada de um componente: o ponto do eixo mais perto do centro dele (a base
        /// do furo de uma FuroPorca fica 1 mm fora da peça, de propósito), dentro do contorno da peça.
        /// Sem eixo, o centro da caixa.
        /// </summary>
        private P2 CalloutAnchor(ComponentDatum c)
        {
            var center = new AvaloniaCadEditor.Domain.ValueObjects.Point3D(
                (c.BoundsMin.X + c.BoundsMax.X) / 2, (c.BoundsMin.Y + c.BoundsMax.Y) / 2, (c.BoundsMin.Z + c.BoundsMax.Z) / 2);
            var point = c.Axis is { } axis
                ? c.AxisPoint + axis * (center - c.AxisPoint).DotProduct(axis)
                : center;
            var p = basis.Project(point);
            return new P2(Math.Clamp(p.U, partMin.U, partMax.U), Math.Clamp(p.V, partMin.V, partMax.V));
        }

        /// <summary>
        /// Uma borda que não existe no desenho: logo do outro lado dela há outro sólido do mesmo papel
        /// na mesma faixa de profundidade (a emenda de duas metades espelhadas, ou um vazio que continua
        /// em outro). Um sólido que só se sobrepõe na projeção, mas está mais à frente ou atrás, não
        /// apaga a borda — ela continua visível (ou tracejada).
        /// </summary>
        private bool IsSeam(ViewEntity entity, double at, bool maxSide, bool horizontal, double cross)
        {
            const double eps = 1e-4; // sonda geométrica logo além da borda, não é tolerância de alinhamento
            double probe = at + (maxSide ? eps : -eps);
            var depth = DepthRange(entity);
            return entities.Any(q => !ReferenceEquals(q, entity) && q.Role == entity.Role &&
                                     Along(q.Min, horizontal) < probe && probe < Along(q.Max, horizontal) &&
                                     Cross(q.Min, horizontal) < cross && cross < Cross(q.Max, horizontal) &&
                                     DepthOverlaps(depth, DepthRange(q)));
        }

        private (double Lo, double Hi)? DepthRange(ViewEntity entity)
        {
            if (entity.Fact == null) return null;
            double a = basis.Depth(entity.Fact.BoundsMin), b = basis.Depth(entity.Fact.BoundsMax);
            return (Math.Min(a, b), Math.Max(a, b));
        }

        private bool DepthOverlaps((double Lo, double Hi)? a, (double Lo, double Hi)? b) =>
            a is not { } x || b is not { } y || (x.Lo < y.Hi - Tol && y.Lo < x.Hi - Tol);

        private List<Mark> Merge(IEnumerable<Mark> marks)
        {
            var merged = new List<Mark>();
            foreach (var m in marks.OrderBy(m => m.At))
            {
                if (merged.Count > 0 && m.At - merged[^1].At <= Tol) merged[^1].Absorb(m);
                else merged.Add(m);
            }
            return merged;
        }

        // ------------------------------------------------------------------ Um eixo

        public IEnumerable<LayoutGroup> PlanAxis(bool horizontal)
        {
            var side = horizontal ? DimensionSide.Below : DimensionSide.Left;
            var marks = Marks(horizontal);
            var groups = new List<LayoutGroup>();

            if (Symmetry(horizontal) is { } m)
            {
                groups.AddRange(SymmetricAxis(Merge(marks), m, horizontal, side));
            }
            else
            {
                groups.AddRange(ChainedAxis(marks, horizontal, side));
            }

            // Nível 3: total.
            double lo = Along(partMin, horizontal), hi = Along(partMax, horizontal);
            var total = Linear($"{AxisKey(horizontal)}:total", horizontal, Point(lo, Cross(partMin, horizontal), horizontal),
                Point(hi, Cross(partMin, horizontal), horizontal), hi - lo, DimensionFactory.Format(hi - lo), []);
            groups.Add(new LayoutGroup([total], side, 3));
            return groups;
        }

        private IEnumerable<LayoutGroup> ChainedAxis(List<Mark> marks, bool horizontal, DimensionSide side)
        {
            double datum = Along(partMin, horizontal);
            var covered = new List<double>();
            bool IsCovered(double v) => covered.Any(c => Math.Abs(c - v) <= Tol);

            // Linhas transversais: marcas com a mesma coordenada transversal (o mesmo "nível" da peça).
            var rows = new List<List<Mark>>();
            foreach (var mark in marks.OrderBy(k => k.RowCross))
            {
                if (rows.Count > 0 && Math.Abs(mark.RowCross - rows[^1][^1].RowCross) <= Tol) rows[^1].Add(mark);
                else rows.Add([mark]);
            }

            var merged = rows.Select(Merge).OrderByDescending(r => r.Count).ThenBy(r => r[0].RowCross).ToList();
            var singles = new List<Mark>();
            foreach (var row in merged)
            {
                var points = row.Where(k => !IsCovered(k.At) && k.At > datum + Tol).ToList();
                if (points.Count >= 2)
                {
                    covered.AddRange(points.Select(p => p.At));
                    yield return new LayoutGroup(Chain(points, horizontal), side, 1);
                }
                else if (points.Count == 1)
                {
                    singles.Add(points[0]);
                }
            }

            // Nível 2: o que ficou sozinho na sua linha vai numa cadeia de localização só.
            var rest = Merge(singles).Where(k => !IsCovered(k.At)).ToList();
            if (rest.Count > 0)
            {
                yield return new LayoutGroup(Chain(rest, horizontal), side, 2);
            }
        }

        /// <summary>Cadeia a partir da borda: borda → 1º → 2º ...; padrões de passo constante compactados.</summary>
        private List<DraftDimension> Chain(List<Mark> points, bool horizontal)
        {
            var dims = new List<DraftDimension>();
            double datumAt = Along(partMin, horizontal);
            var datumPoint = Point(datumAt, Cross(partMin, horizontal), horizontal);
            string axis = AxisKey(horizontal);

            var previous = datumPoint;
            double previousAt = datumAt;
            int i = 0;
            while (i < points.Count)
            {
                var p = points[i];
                var pPoint = Point(p.At, p.ExtCross, horizontal);
                dims.Add(Linear($"{axis}:{previousAt:F3}>{p.At:F3}", horizontal, previous, pPoint, p.At - previousAt,
                    DimensionFactory.Format(p.At - previousAt), p.Sources));

                var run = PatternRun(points, i, horizontal);
                if (run is { } r)
                {
                    var last = points[r.End];
                    var lastPoint = Point(last.At, last.ExtCross, horizontal);
                    int steps = r.End - i;
                    dims.Add(Linear($"{axis}p:{p.At:F3}>{last.At:F3}", horizontal, pPoint, lastPoint, last.At - p.At,
                        DimensionFactory.PatternText(steps, r.Pitch),
                        points.Skip(i).Take(steps + 1).SelectMany(k => k.Sources).Distinct().ToList()));
                    previous = lastPoint;
                    previousAt = last.At;
                    i = r.End + 1;
                    continue;
                }

                previous = pPoint;
                previousAt = p.At;
                i++;
            }
            return dims;
        }

        /// <summary>Uma sequência a partir de <paramref name="start"/> que é um padrão linear com passo
        /// constante (3 elementos ou mais) ao longo deste eixo.</summary>
        private (int End, double Pitch)? PatternRun(List<Mark> points, int start, bool horizontal)
        {
            var axis = horizontal ? _axisU : _axisV;
            (int End, double Pitch)? best = null;
            foreach (var pattern in relations.LinearPatterns.Where(p => p.Pitch != null && p.AlongAxis == axis))
            {
                if (!points[start].Facts.Any(pattern.AllMembers.Contains)) continue;
                double pitch = Math.Abs(pattern.Pitch!.Value);
                int end = start;
                while (end + 1 < points.Count &&
                       points[end + 1].Facts.Any(pattern.AllMembers.Contains) &&
                       Math.Abs(points[end + 1].At - points[end].At - pitch) <= Tol * 10)
                {
                    end++;
                }
                if (end - start >= 2 && (best == null || end > best.Value.End)) best = (end, pitch);
            }
            return best;
        }

        private IEnumerable<LayoutGroup> SymmetricAxis(List<Mark> marks, double m, bool horizontal, DimensionSide side)
        {
            string axis = AxisKey(horizontal);
            var covered = new List<double>();
            bool IsCovered(double v) => covered.Any(c => Math.Abs(c - v) <= Tol);
            Mark? At(double v) => marks.FirstOrDefault(k => Math.Abs(k.At - v) <= Tol);

            // Padrões simétricos: uma cota do primeiro ao último membro, centrada na simetria.
            var frameAxis = horizontal ? _axisU : _axisV;
            foreach (var pattern in relations.LinearPatterns.Where(p => p.Pitch != null && p.AlongAxis == frameAxis && p.Count >= 3))
            {
                var members = marks.Where(k => k.Facts.Any(pattern.AllMembers.Contains)).OrderBy(k => k.At).ToList();
                if (members.Count < 3) continue;
                var first = members[0];
                var last = members[^1];
                if (Math.Abs(first.At + last.At - 2 * m) > Tol * 10 || IsCovered(first.At)) continue;

                covered.AddRange(members.Select(k => k.At));
                yield return new LayoutGroup([Linear($"{axis}p:{first.At:F3}>{last.At:F3}", horizontal,
                    Point(first.At, first.ExtCross, horizontal), Point(last.At, last.ExtCross, horizontal), last.At - first.At,
                    DimensionFactory.PatternText(members.Count - 1, Math.Abs(pattern.Pitch!.Value)),
                    members.SelectMany(k => k.Sources).Distinct().ToList())], side, 1);
            }

            // Pares espelhados: a distância entre os dois.
            foreach (var left in marks.Where(k => k.At < m - Tol && !IsCovered(k.At)))
            {
                double mirrorAt = 2 * m - left.At;
                var right = At(mirrorAt);
                covered.Add(left.At);
                covered.Add(mirrorAt);
                var sources = left.Sources.Concat(right?.Sources ?? Enumerable.Empty<string>()).Distinct().ToList();
                yield return new LayoutGroup([Linear($"{axis}s:{left.At:F3}", horizontal,
                    Point(left.At, left.ExtCross, horizontal), Point(mirrorAt, right?.ExtCross ?? left.ExtCross, horizontal),
                    mirrorAt - left.At, DimensionFactory.Format(mirrorAt - left.At), sources)], side, 1);
            }
        }

        // ------------------------------------------------------------------ Tamanhos

        /// <summary>Diâmetros de furos vistos de lado (largura do retângulo) e chaves de prismas vistos
        /// pelo eixo, do outro lado da peça.</summary>
        public IEnumerable<LayoutGroup> SideSizes()
        {
            var rects = OwnVoids.OfType<ViewRect>().Where(r => r.Axis != CenterAxis.None).ToList();

            foreach (var group in rects.Where(r => r.Axis == CenterAxis.Vertical).GroupBy(r => Math.Round((r.Max.U - r.Min.U) / Tol)))
            {
                var members = group.ToList();
                var rep = members.Where(r => KeepCenter(r.Center)).OrderBy(r => r.Center.U).FirstOrDefault() ?? members[0];
                double top = Math.Min(rep.Max.V, partMax.V);
                double value = rep.Max.U - rep.Min.U;
                yield return new LayoutGroup([new DraftDimension($"ds:{rep.SourceName}", DimensionKind.Horizontal,
                    new P2(rep.Min.U, top), new P2(rep.Max.U, top), 0, value, CountPrefix(members) + $"Ø{DimensionFactory.Format(value)}",
                    members.Select(r => r.SourceName).ToList())], DimensionSide.Above, 1);
            }

            foreach (var group in rects.Where(r => r.Axis == CenterAxis.Horizontal).GroupBy(r => Math.Round((r.Max.V - r.Min.V) / Tol)))
            {
                var members = group.ToList();
                var rep = members.Where(r => KeepCenter(r.Center)).OrderBy(r => r.Center.V).FirstOrDefault() ?? members[0];
                double right = Math.Min(rep.Max.U, partMax.U);
                double value = rep.Max.V - rep.Min.V;
                yield return new LayoutGroup([new DraftDimension($"ds:{rep.SourceName}", DimensionKind.Vertical,
                    new P2(right, rep.Min.V), new P2(right, rep.Max.V), 0, value, CountPrefix(members) + $"Ø{DimensionFactory.Format(value)}",
                    members.Select(r => r.SourceName).ToList())], DimensionSide.Right, 1);
            }

            foreach (var group in OwnVoids.OfType<ViewPolygon>().GroupBy(p => Math.Round((p.Max.U - p.Min.U) / Tol)))
            {
                var members = group.ToList();
                var rep = members.Where(p => KeepCenter(new P2((p.Min.U + p.Max.U) / 2, (p.Min.V + p.Max.V) / 2)))
                    .OrderBy(p => p.Min.U).FirstOrDefault() ?? members[0];
                double top = Math.Min(rep.Max.V, partMax.V);
                double value = rep.Max.U - rep.Min.U;
                yield return new LayoutGroup([new DraftDimension($"pw:{rep.SourceName}", DimensionKind.Horizontal,
                    new P2(rep.Min.U, top), new P2(rep.Max.U, top), 0, value, CountPrefix(members) + DimensionFactory.Format(value),
                    members.Select(p => p.SourceName).ToList())], DimensionSide.Above, 1);
            }
        }

        /// <summary>
        /// Diâmetros dos furos vistos de frente, em chamadas. Furos coaxiais (furo + rebaixo) viram uma
        /// chamada só com os diâmetros empilhados; conjuntos iguais são agrupados ("2× Ø3,4 / Ø7,2").
        /// </summary>
        public IReadOnlyList<DraftDimension> FrontDiameters()
        {
            var circles = OwnVoids.OfType<ViewCircle>().ToList();
            var stacks = new List<List<ViewCircle>>();
            foreach (var circle in circles)
            {
                var set = relations.CoaxialSets.FirstOrDefault(s => circle.Fact != null && s.Members.Contains(circle.Fact));
                var stack = set == null
                    ? null
                    : stacks.FirstOrDefault(st => st.Any(c => c.Fact != null && set.Members.Contains(c.Fact)));
                if (stack != null) stack.Add(circle);
                else stacks.Add([circle]);
            }

            var leaders = new List<DraftDimension>();
            foreach (var group in stacks.GroupBy(Signature))
            {
                var members = group.ToList();
                var rep = members
                    .Where(st => KeepCenter(st[0].Center))
                    .OrderBy(st => st[0].Center.U).ThenBy(st => st[0].Center.V)
                    .FirstOrDefault() ?? members[0];
                var largest = rep.OrderByDescending(c => c.Radius).First();
                var diameters = rep.Select(c => c.Radius * 2).Distinct(new ToleranceComparer(Tol)).OrderBy(d => d);
                string text = CountPrefix(members.Select(st => st.OrderByDescending(c => c.Radius).First()))
                              + string.Join(" / ", diameters.Select(d => $"Ø{DimensionFactory.Format(d)}"));
                leaders.Add(DimensionFactory.Diameter(largest, $"d:{largest.SourceName}", text,
                    members.SelectMany(st => st.Select(c => c.SourceName)).Distinct().ToList(), isManual: false));
            }
            return leaders;

            string Signature(List<ViewCircle> stack) =>
                string.Join("|", stack.Select(c => Math.Round(c.Radius * 2 / Tol)).Distinct().OrderBy(d => d));
        }

        // ------------------------------------------------------------------ Linhas de centro

        public IReadOnlyList<CenterLine> CenterLines()
        {
            var lines = new List<CenterLine>();
            double extra = gap * 0.25;

            if (Symmetry(true) is { } mu)
                lines.Add(new CenterLine(new P2(mu, partMin.V - gap * 0.5), new P2(mu, partMax.V + gap * 0.5)));
            if (Symmetry(false) is { } mv)
                lines.Add(new CenterLine(new P2(partMin.U - gap * 0.5, mv), new P2(partMax.U + gap * 0.5, mv)));

            // Simetrias locais (um grupo espelhado fora do meio da peça).
            foreach (var plane in relations.SymmetryPlanes.Where(p => !p.IsGlobal && p.Pairs.Count >= 2))
            {
                bool vertical = plane.Normal == _axisU;
                if (!vertical && plane.Normal != _axisV) continue;
                var facts = plane.Pairs.SelectMany(p => new[] { p.A, p.B }).ToHashSet();
                var members = entities.Where(e => e.Fact != null && facts.Contains(e.Fact)).ToList();
                if (members.Count == 0) continue;
                if (vertical)
                {
                    double u = SignU * plane.Coordinate;
                    lines.Add(new CenterLine(new P2(u, members.Min(e => e.Min.V) - extra), new P2(u, members.Max(e => e.Max.V) + extra)));
                }
                else
                {
                    double v = SignV * plane.Coordinate;
                    lines.Add(new CenterLine(new P2(members.Min(e => e.Min.U) - extra, v), new P2(members.Max(e => e.Max.U) + extra, v)));
                }
            }

            foreach (var circle in Voids.OfType<ViewCircle>())
            {
                var c = circle.Center;
                double r = circle.Radius + extra;
                lines.Add(new CenterLine(new P2(c.U - r, c.V), new P2(c.U + r, c.V)));
                lines.Add(new CenterLine(new P2(c.U, c.V - r), new P2(c.U, c.V + r)));
            }
            foreach (var rect in entities.OfType<ViewRect>().Where(r => r.Axis != CenterAxis.None))
            {
                var c = rect.Center;
                lines.Add(rect.Axis == CenterAxis.Horizontal
                    ? new CenterLine(new P2(rect.Min.U - extra, c.V), new P2(rect.Max.U + extra, c.V))
                    : new CenterLine(new P2(c.U, rect.Min.V - extra), new P2(c.U, rect.Max.V + extra)));
            }
            return lines;
        }

        // ------------------------------------------------------------------ Utilidades

        private static string AxisKey(bool horizontal) => horizontal ? "h" : "v";

        private static P2 Point(double along, double cross, bool horizontal) => horizontal ? new P2(along, cross) : new P2(cross, along);

        private static DraftDimension Linear(string key, bool horizontal, P2 from, P2 to, double value, string text, IReadOnlyList<string> sources) =>
            new(key, horizontal ? DimensionKind.Horizontal : DimensionKind.Vertical, from, to, 0, value, text, sources);

        /// <summary>"3× " quando há mais de uma posição distinta; sobrepostos na vista contam uma vez.</summary>
        private string CountPrefix(IEnumerable<ViewEntity> members)
        {
            int distinct = members
                .Select(e => (Math.Round(e.Min.U / Tol), Math.Round(e.Min.V / Tol), Math.Round(e.Max.U / Tol), Math.Round(e.Max.V / Tol)))
                .Distinct()
                .Count();
            return distinct > 1 ? $"{distinct}× " : "";
        }

        private sealed class ToleranceComparer(double tol) : IEqualityComparer<double>
        {
            public bool Equals(double a, double b) => Math.Abs(a - b) <= tol;
            public int GetHashCode(double value) => 0;
        }
    }
}
