using AvaloniaCadEditor.Analysis;

namespace AvaloniaCadEditor.Drafting;

/// <summary>
/// Decide QUAIS cotas uma vista leva, com hierarquia — o <see cref="DimensionLayout"/> decide depois
/// ONDE cada linha fica. Para cada eixo da vista (U → cotas horizontais, abaixo/acima; V → verticais,
/// à esquerda/direita):
/// <list type="number">
/// <item><b>Nível 1, uma cadeia por lado, de ponta a ponta:</b> o lado de baixo/esquerda leva os
/// elementos da própria peça e os componentes que entram pela face dele; o de cima/direita, os que
/// entram pela face dele. Cada canto do contorno (um recorte) vai para o lado da borda a que encosta.
/// A cadeia fecha na outra borda com um elo de referência, entre parênteses. Um padrão de passo
/// constante vira uma cota só, "3× 20 (=60)", e cada ponto diz quem mais está no mesmo eixo
/// (<see cref="DraftDimension.AlignedWith"/>). A densidade nunca esconde essas cadeias.</item>
/// <item><b>Nível 2, face oposta:</b> componentes que entram por trás da vista (uma porca por baixo,
/// vista do topo), numa cadeia por fora do lado mais vazio, sem repetir eixos já cotados.</item>
/// <item><b>Nível 3, totais:</b> largura e altura, por fora.</item>
/// </list>
/// Com simetria global no eixo, os pares espelhados são cotados pela distância entre si, centrados
/// na linha de simetria, e o que está sobre ela não leva cota naquele eixo. Os tamanhos (furo visto de
/// lado, chave de prisma) ficam do outro lado da peça; os diâmetros de frente, em chamadas, com os
/// furos coaxiais empilhados ("Ø3,4 / Ø7,2").
/// Peças aninhadas (componentes, ex.: FuroPorca) são caixas-pretas: localizadas pela referência
/// (o eixo), identificadas por uma chamada com o rótulo e, quando deitadas na vista, com a distância
/// da face de entrada ao alojamento sobre o próprio eixo — sem as medidas internas.
/// </summary>
public static class DimensionPlanner
{
    public sealed record Plan(IReadOnlyList<LayoutGroup> Groups, IReadOnlyList<DraftDimension> Leaders, IReadOnlyList<CenterLine> CenterLines);

    /// <param name="options">De onde vem a tolerância de "mesma coordenada" — a mesma que a análise
    /// usou para agrupar, para que um alinhamento que ela reconhece o desenho também reconheça.</param>
    /// <param name="depthRange">Profundidades (ao longo de <see cref="ViewBasis.W"/>) da face de trás e da da frente
    /// da peça: um componente que entra por trás vai para a cadeia da face oposta. Sem elas, nenhum vai.</param>
    public static Plan Build(IReadOnlyList<ViewEntity> entities, ModelRelations relations, ViewBasis basis, P2 partMin, P2 partMax, double gap,
        AnalysisOptions? options = null, (double Back, double Front)? depthRange = null)
    {
        var context = new Context(entities, relations, basis, partMin, partMax, gap, (options ?? new AnalysisOptions()).AlignTolerance, depthRange);
        var groups = new List<LayoutGroup>();
        groups.AddRange(context.PlanAxis(horizontal: true));
        groups.AddRange(context.PlanAxis(horizontal: false));
        groups.AddRange(context.SideSizes());
        return new Plan(groups, context.FrontDiameters().Concat(context.ComponentCallouts()).Concat(context.FaceDepths()).ToList(), context.CenterLines());
    }

    /// <summary>A que cadeia uma marca pertence.</summary>
    private enum MarkSide
    {
        /// <summary>Elemento da própria peça (furo, alojamento, componente de frente): a cadeia
        /// principal, abaixo ou à esquerda.</summary>
        Main,

        /// <summary>Lado de baixo/esquerda por natureza: um componente que entra por essa face, ou um
        /// canto do contorno junto a ela.</summary>
        Low,

        /// <summary>Lado de cima/direita, idem.</summary>
        High,

        /// <summary>Componente que entra pela face oposta à vista (uma porca por baixo, vista do topo):
        /// cadeia separada, por fora.</summary>
        Back
    }

    /// <summary>Um ponto cotável ao longo de um eixo, com os sólidos que o definem.</summary>
    /// <param name="extLo">Coordenada transversal de onde sai a linha de chamada para o lado de baixo/esquerda.</param>
    /// <param name="extHi">Idem, para o lado de cima/direita.</param>
    /// <param name="side">A cadeia a que a marca pertence.</param>
    /// <param name="sideDistance">Para o contorno: a distância do canto à borda do lado escolhido —
    /// num canto compartilhado, vale o que está mais junto de uma borda.</param>
    /// <param name="isAxis">A marca é um eixo (centro de furo, linha de centro, eixo de componente).</param>
    private sealed class Mark(double at, double extLo, double extHi, string source, PrimitiveFact? fact, MarkSide side,
        double sideDistance = 0, bool isAxis = false)
    {
        public double At { get; } = at;

        public MarkSide Side { get; set; } = side;

        public double SideDistance { get; } = sideDistance;

        public bool IsAxis { get; } = isAxis;

        public double ExtLo { get; private set; } = extLo;

        public double ExtHi { get; private set; } = extHi;

        public List<string> Sources { get; } = [source];
        public List<PrimitiveFact> Facts { get; } = fact == null ? [] : [fact];

        public void Absorb(Mark other)
        {
            foreach (var s in other.Sources) if (!Sources.Contains(s)) Sources.Add(s);
            foreach (var f in other.Facts) if (!Facts.Contains(f)) Facts.Add(f);
            ExtLo = Math.Min(ExtLo, other.ExtLo);
            ExtHi = Math.Max(ExtHi, other.ExtHi);
        }
    }

    private sealed class Context(IReadOnlyList<ViewEntity> entities, ModelRelations relations, ViewBasis basis, P2 partMin, P2 partMax, double gap, double tol,
        (double Back, double Front)? depthRange)
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

            // Um canto do contorno vai para o lado da borda mais próxima dele.
            Mark Contour(double at, double cMin, double cMax, ViewEntity e)
            {
                double toLow = cMin - crossLo, toHigh = crossHi - cMax;
                return toLow <= toHigh
                    ? new Mark(at, cMin, cMax, e.SourceName, e.Fact, MarkSide.Low, toLow)
                    : new Mark(at, cMin, cMax, e.SourceName, e.Fact, MarkSide.High, toHigh);
            }

            var marks = new List<Mark>();
            foreach (var e in entities)
            {
                if (IsComponentMember(e)) continue;

                double eLo = Along(e.Min, horizontal), eHi = Along(e.Max, horizontal);
                double cLo = ClampCross(Cross(e.Min, horizontal)), cHi = ClampCross(Cross(e.Max, horizontal));
                double center = (eLo + eHi) / 2;

                double row = (cLo + cHi) / 2;
                void AddCenter(double at, bool isAxis) => marks.Add(new Mark(at, cLo, cHi, e.SourceName, e.Fact, MarkSide.Main, isAxis: isAxis));
                void AddEdge(double at, bool maxSide)
                {
                    if (!Inside(at) || IsSeam(e, at, maxSide, horizontal, row)) return;
                    marks.Add(e.Role == PrimitiveRole.Body
                        ? Contour(at, cLo, cHi, e)
                        : new Mark(at, cLo, cHi, e.SourceName, e.Fact, MarkSide.Main));
                }

                switch (e)
                {
                    case ViewCircle circle when e.Role == PrimitiveRole.Void:
                        double circleCross = ClampCross(Cross(circle.Center, horizontal));
                        marks.Add(new Mark(Along(circle.Center, horizontal), circleCross, circleCross, e.SourceName, e.Fact, MarkSide.Main, isAxis: true));
                        break;

                    case ViewRect { Axis: not CenterAxis.None } rect when e.Role == PrimitiveRole.Void:
                        // Furo visto de lado: posição pela linha de centro se ela cruza o eixo medido;
                        // se corre ao longo dele, pelas pontas (profundidade de furo cego).
                        bool centerLineCrosses = horizontal ? rect.Axis == CenterAxis.Vertical : rect.Axis == CenterAxis.Horizontal;
                        if (centerLineCrosses) AddCenter(center, isAxis: true);
                        else { AddEdge(eLo, false); AddEdge(eHi, true); }
                        break;

                    case ViewPolygon when e.Role == PrimitiveRole.Void && horizontal:
                        AddCenter(center, isAxis: false);
                        break;

                    case ViewPolygon polygon when e.Role == PrimitiveRole.Body:
                        // Cada vértice com a própria coordenada transversal: o canto de um recorte fica
                        // do lado da borda a que ele encosta.
                        foreach (var p in polygon.Points)
                        {
                            double at = Along(p, horizontal);
                            if (!Inside(at)) continue;
                            double c = ClampCross(Cross(p, horizontal));
                            marks.Add(Contour(at, c, c, e));
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

            marks.AddRange(ComponentMarks(horizontal, lo, hi, crossLo, crossHi));
            return marks;
        }

        /// <summary>
        /// Uma peça aninhada é cotada só pela referência: o eixo (visto de frente, o centro nos dois
        /// eixos; de lado, a linha de centro no eixo que ela cruza) ou, sem eixo, o centro da caixa.
        /// A posição AO LONGO do eixo fica para <see cref="FaceDepths"/>, e as medidas internas, de fora.
        /// O lado da cadeia sai de por onde ela entra: de lado, pela face que a caixa dela atravessa;
        /// de frente, pela face oposta (vai para a cadeia de trás) ou não.
        /// </summary>
        private IEnumerable<Mark> ComponentMarks(bool horizontal, double lo, double hi, double crossLo, double crossHi)
        {
            foreach (var c in relations.Components)
            {
                var (bMin, bMax) = ProjectedBounds(c);
                var fact = c.Principal ?? c.Members[0];
                double rawLo = Cross(bMin, horizontal), rawHi = Cross(bMax, horizontal);
                double cLo = Math.Clamp(rawLo, crossLo, crossHi), cHi = Math.Clamp(rawHi, crossLo, crossHi);
                double at;
                double extLo, extHi;
                var side = MarkSide.Main;

                if (c.Axis is { } axis)
                {
                    var datum = basis.Project(c.AxisPoint);
                    var alignment = AxisAlignment(axis);
                    if (alignment == 0)
                    {
                        at = Along(datum, horizontal);
                        extLo = extHi = Math.Clamp(Cross(datum, horizontal), crossLo, crossHi);
                        if (depthRange is { } range && basis.Depth(c.AxisPoint) < range.Back - Tol) side = MarkSide.Back;
                    }
                    else if (alignment > 0 && (alignment == 1) != horizontal)
                    {
                        // Linha de centro perpendicular ao eixo medido: a posição dela, do lado da face
                        // por onde o componente entra.
                        at = Along(datum, horizontal);
                        extLo = cLo;
                        extHi = cHi;
                        side = rawLo - crossLo <= crossHi - rawHi ? MarkSide.Low : MarkSide.High;
                    }
                    else
                    {
                        continue; // eixo ao longo do eixo medido (ou oblíquo): nada a localizar aqui
                    }
                }
                else
                {
                    at = (Along(bMin, horizontal) + Along(bMax, horizontal)) / 2;
                    extLo = cLo;
                    extHi = cHi;
                }

                if (at < lo - Tol || at > hi + Tol) continue;
                yield return new Mark(at, extLo, extHi, c.Ref.Name, fact, side, isAxis: c.Axis != null);
            }
        }

        /// <summary>
        /// Onde fica, a partir da face de entrada, um componente cujo eixo corre ao longo de um eixo da
        /// vista (uma FuroPorca deitada): da face até a borda mais próxima dos membros que não são o furo
        /// (o alojamento da porca). A cota vai sobre o próprio eixo do componente, sem linha de chamada.
        /// </summary>
        public IEnumerable<DraftDimension> FaceDepths()
        {
            foreach (var c in relations.Components)
            {
                if (c.Axis is not { } axis || c.Principal == null) continue;
                var alignment = AxisAlignment(axis);
                if (alignment is not (1 or 2)) continue;
                bool horizontal = alignment == 1;

                var others = c.Members.Where(m => m != c.Principal).ToList();
                if (others.Count == 0) continue;

                var (bMin, bMax) = ProjectedBounds(c);
                double lo = Along(partMin, horizontal), hi = Along(partMax, horizontal);
                bool fromLow = Along(bMin, horizontal) - lo <= hi - Along(bMax, horizontal);
                double face = fromLow ? lo : hi;

                var along = others.SelectMany(m => new[] { basis.Project(m.BoundsMin), basis.Project(m.BoundsMax) })
                    .Select(p => Along(p, horizontal)).ToList();
                double nearest = fromLow ? along.Min() : along.Max();
                double depth = Math.Abs(nearest - face);
                if (depth < Tol) continue;

                double cross = Cross(basis.Project(c.AxisPoint), horizontal);
                yield return Linear($"pf:{c.Ref.Name}", horizontal, Point(face, cross, horizontal), Point(nearest, cross, horizontal),
                    depth, DimensionFactory.Format(depth), [c.Ref.Name]) with { LinePosition = cross };
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
        /// Uma chamada por rótulo e configuração de componente ("3× FuroPorca M3 Axial"), saindo do
        /// círculo do eixo quando ele é visto de frente, ou da referência. Quando o mesmo rótulo aparece
        /// com configurações diferentes, cada chamada leva os parâmetros que as distinguem
        /// ("3× FuroPorca M3 Lateral · Profundidade 6").
        /// </summary>
        public IReadOnlyList<DraftDimension> ComponentCallouts()
        {
            var callouts = new List<DraftDimension>();
            foreach (var labelGroup in relations.Components.GroupBy(c => c.Ref.Label))
            {
                var configurations = labelGroup.GroupBy(c => c.Signature).Select(g => g.ToList()).ToList();
                var differing = configurations.Count > 1 ? DifferingParameters(configurations) : [];
                foreach (var group in configurations)
                {
                    callouts.Add(Callout(labelGroup.Key, group, differing));
                }
            }
            return callouts;
        }

        /// <summary>Os parâmetros que explicam a diferença entre as configurações: iguais dentro de
        /// cada uma e diferentes entre elas (na ordem da peça). Uma posição passada num número muda
        /// dentro da configuração e fica de fora.</summary>
        private static List<string> DifferingParameters(List<List<ComponentDatum>> configurations)
        {
            string? Value(ComponentDatum c, string name) => c.Ref.Configuration.FirstOrDefault(p => p.Name == name).Value;
            return configurations[0][0].Ref.Configuration
                .Select(p => p.Name)
                .Where(name => configurations.All(g => g.Select(c => Value(c, name)).Distinct().Count() == 1) &&
                               configurations.Select(g => Value(g[0], name)).Distinct().Count() > 1)
                .ToList();
        }

        private DraftDimension Callout(string label, List<ComponentDatum> group, List<string> differing)
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
            var configuration = group[0].Ref.Configuration;
            string details = differing.Count == 0 ? ""
                : " · " + string.Join(", ", differing.Select(name => $"{name} {configuration.First(c => c.Name == name).Value}"));
            string text = (count > 1 ? $"{count}× " : "") + label + details;

            var circle = entities.OfType<ViewCircle>().FirstOrDefault(e => rep.Component.Principal != null && e.Fact == rep.Component.Principal)
                         ?? new ViewCircle(rep.Component.Ref.Name, PrimitiveRole.Void, rep.Datum, gap * 0.2);
            var leader = DimensionFactory.Diameter(circle, $"c:{label}|{group[0].Signature}", text,
                group.Select(c => c.Ref.Name).ToList(), isManual: false);
            return leader with { Kind = DimensionKind.Callout, Value = 0 };
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
                groups.AddRange(SideChains(marks, horizontal));
            }

            // Nível 3: total.
            double lo = Along(partMin, horizontal), hi = Along(partMax, horizontal);
            var total = Linear($"{AxisKey(horizontal)}:total", horizontal, Point(lo, Cross(partMin, horizontal), horizontal),
                Point(hi, Cross(partMin, horizontal), horizontal), hi - lo, DimensionFactory.Format(hi - lo), []);
            groups.Add(new LayoutGroup([total], side, 3));
            return groups;
        }

        /// <summary>
        /// Uma cadeia por lado, de ponta a ponta. Cada lado leva os componentes que entram pela face dele
        /// e os cantos do contorno que encostam na borda dele; os elementos da própria peça vão para o
        /// lado mais perto deles. Um eixo que só elementos da própria peça definem é cotado uma vez:
        /// se eles estão dos dois lados (os furos de uma malha), vai para o lado principal — o que tem
        /// mais elementos. Assim uma peça e a sua espelhada dão desenhos espelhados. O que entra pela face
        /// oposta à vista vai numa cadeia à parte (nível 2), por fora do lado mais vazio, sem repetir
        /// eixos que as outras já cotam.
        /// </summary>
        private IEnumerable<LayoutGroup> SideChains(List<Mark> marks, bool horizontal)
        {
            double lo = Along(partMin, horizontal), hi = Along(partMax, horizontal);
            var lowSide = horizontal ? DimensionSide.Below : DimensionSide.Left;
            var highSide = horizontal ? DimensionSide.Above : DimensionSide.Right;
            bool Same(double a, double b) => Math.Abs(a - b) <= Tol;

            // Canto compartilhado (vários vértices na mesma coordenada): vale o mais junto de uma borda.
            var contour = marks.Where(k => k.Side is MarkSide.Low or MarkSide.High && k.Facts.All(f => f.Component == null) && !k.IsAxis).ToList();
            foreach (var k in contour)
            {
                k.Side = contour.Where(o => Same(o.At, k.At)).MinBy(o => o.SideDistance)!.Side;
            }

            // Elementos da própria peça: o lado mais perto (null = no meio).
            double crossLo = Cross(partMin, horizontal), crossHi = Cross(partMax, horizontal);
            MarkSide? Nearest(Mark k)
            {
                double mid = (k.ExtLo + k.ExtHi) / 2, toLow = mid - crossLo, toHigh = crossHi - mid;
                return toLow < toHigh - Tol ? MarkSide.Low : toHigh < toLow - Tol ? MarkSide.High : null;
            }

            var mains = marks.Where(k => k.Side == MarkSide.Main).ToList();
            var fixedMarks = marks.Where(k => k.Side is MarkSide.Low or MarkSide.High).ToList();
            // Quantos pontos distintos cada lado teria (um canto com dois vértices na mesma coordenada conta uma vez).
            int Count(MarkSide side) => Merge(fixedMarks.Where(k => k.Side == side).Concat(mains.Where(k => Nearest(k) == side))
                .Select(k => new Mark(k.At, 0, 0, "", null, side))).Count;
            var primary = Count(MarkSide.High) > Count(MarkSide.Low) ? MarkSide.High : MarkSide.Low;

            foreach (var k in mains)
            {
                var fixedHere = fixedMarks.Where(f => Same(f.At, k.At)).Select(f => f.Side).Distinct().ToList();
                var mainsHere = mains.Where(o => Same(o.At, k.At)).Select(Nearest).Distinct().ToList();
                k.Side = fixedHere.Count == 1 ? fixedHere[0]                          // junto de quem já cota esse eixo
                    : fixedHere.Count == 2 ? Nearest(k) ?? primary                     // cotado dos dois lados: o mais perto
                    : mainsHere.Count == 1 && mainsHere[0] is { } only ? only          // todos do mesmo lado
                    : primary;                                                         // dos dois lados: o principal
            }

            var low = marks.Where(k => k.Side == MarkSide.Low).ToList();
            var high = marks.Where(k => k.Side == MarkSide.High).ToList();

            bool Inner(Mark k) => k.At > lo + Tol && k.At < hi - Tol;
            var lowPoints = Merge(low).Where(Inner).ToList();
            var highPoints = Merge(high).Where(Inner).ToList();

            if (lowPoints.Count > 0)
                yield return new LayoutGroup(Chain(lowPoints, horizontal, marks, lowSide, close: true), lowSide, 1) { IsPrimary = true };
            if (highPoints.Count > 0)
                yield return new LayoutGroup(Chain(highPoints, horizontal, marks, highSide, close: true), highSide, 1) { IsPrimary = true };

            var covered = lowPoints.Concat(highPoints).Select(k => k.At).ToList();
            var backPoints = Merge(marks.Where(k => k.Side == MarkSide.Back)).Where(Inner).Where(k => !covered.Any(c => Same(c, k.At))).ToList();
            if (backPoints.Count > 0)
            {
                var side = highPoints.Count < lowPoints.Count ? highSide : lowSide;
                yield return new LayoutGroup(Chain(backPoints, horizontal, marks, side, close: false), side, 2);
            }
        }

        /// <summary>Cadeia a partir da borda: borda → 1º → 2º ...; padrões de passo constante
        /// compactados. Com <paramref name="close"/>, fecha até a outra borda com um elo de referência,
        /// entre parênteses (a soma já é o total). <paramref name="all"/> são todas as marcas do eixo,
        /// para dizer quem mais está em cada ponto.</summary>
        private List<DraftDimension> Chain(List<Mark> points, bool horizontal, IReadOnlyList<Mark> all, DimensionSide side, bool close)
        {
            // Os eixos de outros elementos na mesma coordenada: "mesmo eixo" que o ponto cotado.
            IReadOnlyList<string> AlignedWith(Mark p) => all
                .Where(k => k.IsAxis && Math.Abs(k.At - p.At) <= Tol)
                .SelectMany(k => k.Sources)
                .Where(s => !p.Sources.Contains(s))
                .Distinct()
                .ToList();

            bool highSide = side is DimensionSide.Above or DimensionSide.Right;
            double Ext(Mark k) => highSide ? k.ExtHi : k.ExtLo;
            double edgeCross = highSide ? Cross(partMax, horizontal) : Cross(partMin, horizontal);

            var dims = new List<DraftDimension>();
            double datumAt = Along(partMin, horizontal);
            var datumPoint = Point(datumAt, edgeCross, horizontal);
            string axis = AxisKey(horizontal) + (highSide ? "+" : "");

            var previous = datumPoint;
            double previousAt = datumAt;
            int i = 0;
            while (i < points.Count)
            {
                var p = points[i];
                var pPoint = Point(p.At, Ext(p), horizontal);
                dims.Add(Linear($"{axis}:{previousAt:F3}>{p.At:F3}", horizontal, previous, pPoint, p.At - previousAt,
                    DimensionFactory.Format(p.At - previousAt), p.Sources) with { AlignedWith = AlignedWith(p) });

                var run = PatternRun(points, i, horizontal);
                if (run is { } r)
                {
                    var last = points[r.End];
                    var lastPoint = Point(last.At, Ext(last), horizontal);
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

            double endAt = Along(partMax, horizontal);
            if (close && endAt - previousAt > Tol)
            {
                double rest = endAt - previousAt;
                dims.Add(Linear($"{axis}:{previousAt:F3}>fim", horizontal, previous, Point(endAt, edgeCross, horizontal), rest,
                    $"({DimensionFactory.Format(rest)})", []));
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
                    Point(first.At, first.ExtLo, horizontal), Point(last.At, last.ExtLo, horizontal), last.At - first.At,
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
                    Point(left.At, left.ExtLo, horizontal), Point(mirrorAt, right?.ExtLo ?? left.ExtLo, horizontal),
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
            foreach (var plane in relations.SymmetryPlanes.Where(p => !p.IsGlobal && p.UnitPairs >= 2))
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
