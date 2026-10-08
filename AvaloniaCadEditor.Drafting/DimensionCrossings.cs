namespace AvaloniaCadEditor.Drafting;

/// <summary>
/// Onde as "pernas" (linhas de chamada) atravessam outras cotas. A perna de uma cota vai do ponto
/// do elemento até a linha dela; cruza outra cota do mesmo lado quando a linha da outra fica entre
/// as duas pontas da perna e o intervalo da outra (pontas + texto) contém a perna. Encostar na ponta
/// de outra cota não é cruzamento — é cadeia.
/// </summary>
public static class DimensionCrossings
{
    /// <summary>Um cruzamento: a perna da cota <paramref name="Leg"/> atravessa a cota <paramref name="Crossed"/>
    /// (índices em <c>placed</c>).</summary>
    public readonly record struct Crossing(int Leg, int Crossed);

    public static IReadOnlyList<Crossing> Find(IReadOnlyList<DraftDimension> placed, double gap, double tolerance)
    {
        var crossings = new List<Crossing>();
        for (int i = 0; i < placed.Count; i++)
        {
            var d = placed[i];
            if (!HasLegs(d)) continue;
            bool horizontal = d.Side is DimensionSide.Below or DimensionSide.Above;

            foreach (var foot in new[] { d.From, d.To })
            {
                double along = horizontal ? foot.U : foot.V;
                double start = horizontal ? foot.V : foot.U;
                double lo = Math.Min(start, d.LinePosition), hi = Math.Max(start, d.LinePosition);

                for (int j = 0; j < placed.Count; j++)
                {
                    var q = placed[j];
                    if (j == i || q.Side != d.Side || !HasLegs(q)) continue;
                    if (q.LinePosition <= lo + tolerance || q.LinePosition >= hi - tolerance) continue;

                    var (qLo, qHi) = DimensionLayout.Interval(q, q.Side, gap);
                    if (along <= qLo + tolerance || along >= qHi - tolerance) continue;

                    double qa = horizontal ? q.From.U : q.From.V;
                    double qb = horizontal ? q.To.U : q.To.V;
                    if (Math.Abs(along - qa) <= tolerance || Math.Abs(along - qb) <= tolerance) continue;

                    crossings.Add(new Crossing(i, j));
                }
            }
        }
        return crossings;
    }

    private static bool HasLegs(DraftDimension d) =>
        d.Side != DimensionSide.None && d.Kind is DimensionKind.Horizontal or DimensionKind.Vertical;
}
