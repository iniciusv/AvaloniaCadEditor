using AvaloniaCadEditor.Analysis;

namespace AvaloniaCadEditor.Drafting;

/// <summary>
/// Sabe remedir uma cota sobre outra versão da peça. Na folha original, resolve de onde a cota parte e
/// aonde chega (a âncora de uma primitiva, como <c>furo_a.centro</c>, ou uma borda da peça); depois,
/// dado outro conjunto de fatos (a peça com um número do código trocado), projeta-os na mesma vista e
/// mede de novo entre as mesmas referências.
///
/// Imutável e puro: <see cref="Measure(IReadOnlyList{PrimitiveFact})"/> não guarda nada entre chamadas.
/// </summary>
public sealed class DimensionGauge
{
    private const double Tolerance = 1e-6;

    private readonly ViewBasis _basis;
    private readonly End? _from;
    private readonly End? _to;
    private readonly string? _circle;
    private readonly double _scale;

    private DimensionGauge(DraftDimension dimension, ViewBasis basis, End? from, End? to, string? circle, double scale)
    {
        Dimension = dimension;
        _basis = basis;
        _from = from;
        _to = to;
        _circle = circle;
        _scale = scale;
        MeasuredSources = new[] { from?.Source, to?.Source, circle }
            .Where(s => s != null)
            .Select(s => s!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public DraftDimension Dimension { get; }

    /// <summary>As primitivas (nomes dos fatos) que a cota mede; numa borda da peça, o corpo que a forma.</summary>
    public IReadOnlyList<string> MeasuredSources { get; }

    /// <summary>O medidor da cota, ou <c>null</c> quando ela não mede nada que se possa remedir (chamada
    /// de componente, ponta que não coincide com nenhuma referência).</summary>
    public static DimensionGauge? For(DraftingSheet sheet, DraftDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(dimension);

        switch (dimension.Kind)
        {
            case DimensionKind.Diameter:
            {
                var name = dimension.SourceNames.FirstOrDefault();
                var circle = sheet.Entities.OfType<ViewCircle>().FirstOrDefault(c => c.SourceName == name);
                return circle == null ? null : new DimensionGauge(dimension, sheet.Basis, null, null, circle.SourceName, 1);
            }

            case DimensionKind.Horizontal:
            case DimensionKind.Vertical:
            {
                bool alongU = dimension.Kind == DimensionKind.Horizontal;
                var from = Resolve(sheet, dimension, dimension.From, alongU);
                var to = Resolve(sheet, dimension, dimension.To, alongU);
                if (from == null || to == null) return null;

                double measured = to.Coordinate(sheet.Entities, alongU)!.Value - from.Coordinate(sheet.Entities, alongU)!.Value;
                if (Math.Abs(measured) < Tolerance) return null;

                // O texto pode não ser a distância entre as pontas (padrão "3× 20 (=60)"): a escala
                // devolve a medida na mesma unidade do que está escrito.
                double scale = Math.Abs(dimension.Value) > Tolerance ? dimension.Value / measured : Math.Sign(measured);
                return new DimensionGauge(dimension, sheet.Basis, from, to, null, scale);
            }

            default:
                return null;
        }
    }

    /// <summary>A medida sobre os fatos de outra versão da peça (no mesmo referencial da folha).</summary>
    public double? Measure(IReadOnlyList<PrimitiveFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return Measure(ViewProjector.Project(facts, _basis).Entities);
    }

    /// <summary>A medida sobre primitivas já projetadas na vista; <c>null</c> se uma referência sumiu.</summary>
    public double? Measure(IReadOnlyList<ViewEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (_circle != null)
        {
            var circle = entities.OfType<ViewCircle>().FirstOrDefault(c => c.SourceName == _circle);
            return circle == null ? null : circle.Radius * 2;
        }

        bool alongU = Dimension.Kind == DimensionKind.Horizontal;
        var a = _from!.Coordinate(entities, alongU);
        var b = _to!.Coordinate(entities, alongU);
        return a == null || b == null ? null : (b.Value - a.Value) * _scale;
    }

    /// <summary>
    /// A referência de uma ponta, por ordem de preferência: uma âncora de uma primitiva que a cota cita
    /// (<see cref="DraftDimension.SourceNames"/>), a borda da peça, e por fim a âncora mais próxima de
    /// qualquer primitiva na mesma coordenada.
    /// </summary>
    private static End? Resolve(DraftingSheet sheet, DraftDimension dimension, P2 point, bool alongU)
    {
        double coordinate = alongU ? point.U : point.V;
        double other = alongU ? point.V : point.U;

        var candidates = sheet.Entities
            .SelectMany(e => e.Anchors.Select(a => (Entity: e, Anchor: a)))
            .Where(c => Math.Abs(Along(c.Anchor.Point, alongU) - coordinate) < Tolerance)
            .OrderBy(c => Math.Abs(Across(c.Anchor.Point, alongU) - other))
            .ThenBy(c => c.Anchor.Id, StringComparer.Ordinal)
            .ToList();

        var cited = candidates.FirstOrDefault(c => dimension.SourceNames.Contains(c.Entity.SourceName));
        if (cited.Anchor != null) return new AnchorEnd(cited.Entity.SourceName, cited.Anchor.Id);

        double partMin = alongU ? sheet.PartMin.U : sheet.PartMin.V;
        double partMax = alongU ? sheet.PartMax.U : sheet.PartMax.V;
        foreach (bool isMax in new[] { false, true })
        {
            if (Math.Abs((isMax ? partMax : partMin) - coordinate) > Tolerance) continue;

            // A borda vem da malha (com as booleanas), que a sonda não tem: mede-se pela caixa dos corpos
            // e guarda-se a diferença, que é constante enquanto o contorno for o dos corpos.
            var body = BodyEdge(sheet.Entities, alongU, isMax);
            if (body == null) continue;

            // O corpo que forma a borda é o que se destaca.
            var owner = sheet.Entities.First(e => e.Role == PrimitiveRole.Body &&
                Math.Abs(Along(isMax ? e.Max : e.Min, alongU) - body.Value) < Tolerance);
            return new PartEdgeEnd(owner.SourceName, isMax, coordinate - body.Value);
        }

        var nearest = candidates.FirstOrDefault();
        return nearest.Anchor == null ? null : new AnchorEnd(nearest.Entity.SourceName, nearest.Anchor.Id);
    }

    private static double Along(P2 p, bool alongU) => alongU ? p.U : p.V;

    private static double Across(P2 p, bool alongU) => alongU ? p.V : p.U;

    private static double? BodyEdge(IReadOnlyList<ViewEntity> entities, bool alongU, bool isMax)
    {
        var bodies = entities.Where(e => e.Role == PrimitiveRole.Body).ToList();
        if (bodies.Count == 0) return null;
        return isMax ? bodies.Max(e => Along(e.Max, alongU)) : bodies.Min(e => Along(e.Min, alongU));
    }

    private abstract record End(string? Source)
    {
        public abstract double? Coordinate(IReadOnlyList<ViewEntity> entities, bool alongU);
    }

    private sealed record AnchorEnd(string Entity, string AnchorId) : End(Entity)
    {
        public override double? Coordinate(IReadOnlyList<ViewEntity> entities, bool alongU)
        {
            var anchor = entities
                .Where(e => e.SourceName == Entity)
                .SelectMany(e => e.Anchors)
                .FirstOrDefault(a => a.Id == AnchorId);
            return anchor == null ? null : Along(anchor.Point, alongU);
        }
    }

    /// <param name="Body">O corpo que forma a borda (só para destacar: a medida usa a caixa de todos os corpos).</param>
    private sealed record PartEdgeEnd(string Body, bool IsMax, double Offset) : End(Body)
    {
        public override double? Coordinate(IReadOnlyList<ViewEntity> entities, bool alongU) =>
            BodyEdge(entities, alongU, IsMax) is { } edge ? edge + Offset : null;
    }
}
