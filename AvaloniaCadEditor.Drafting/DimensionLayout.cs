namespace AvaloniaCadEditor.Drafting;

/// <summary>Cotas que ficam na mesma linha (uma cadeia, por exemplo) e se movem juntas no layout.</summary>
public sealed record LayoutGroup(IReadOnlyList<DraftDimension> Dimensions, DimensionSide Side, int Level);

/// <summary>
/// Encaixe das linhas de cota em fileiras, por intervalos. Cada grupo ocupa, no eixo que mede, o
/// intervalo das suas cotas mais o espaço estimado do texto; vai para a primeira fileira — daquele
/// nível em diante — onde não colide com nada. As fileiras de um nível ficam sempre além das do
/// nível anterior, então a hierarquia se lê de dentro para fora e cotas que não se sobrepõem
/// dividem a mesma fileira.
/// </summary>
public static class DimensionLayout
{
    public static IReadOnlyList<DraftDimension> Place(IEnumerable<LayoutGroup> groups, P2 partMin, P2 partMax, double gap)
    {
        var placed = new List<DraftDimension>();
        foreach (var side in groups.GroupBy(g => g.Side))
        {
            var rows = new List<List<(double Lo, double Hi)>>();
            int floor = 0;
            int maxUsed = -1;
            int? currentLevel = null;

            var ordered = side
                .Select(g => (Group: g, Interval: Interval(g, gap)))
                .OrderBy(x => x.Group.Level)
                .ThenBy(x => x.Interval.Lo)
                .ThenBy(x => x.Interval.Hi);

            foreach (var (group, interval) in ordered)
            {
                if (currentLevel != group.Level)
                {
                    if (currentLevel != null && maxUsed >= 0) floor = maxUsed + 1;
                    currentLevel = group.Level;
                }

                int row = floor;
                while (row < rows.Count && rows[row].Any(o => interval.Lo < o.Hi && o.Lo < interval.Hi)) row++;
                while (rows.Count <= row) rows.Add(new());
                rows[row].Add(interval);
                maxUsed = Math.Max(maxUsed, row);

                double line = side.Key switch
                {
                    DimensionSide.Below => partMin.V - gap * (row + 1),
                    DimensionSide.Left => partMin.U - gap * (row + 1),
                    DimensionSide.Above => partMax.V + gap * (row + 1),
                    _ => partMax.U + gap * (row + 1)
                };
                placed.AddRange(group.Dimensions.Select(d => d with { LinePosition = line, Level = group.Level, Side = side.Key }));
            }
        }
        return placed;
    }

    /// <summary>Intervalo ocupado no eixo medido: as pontas das cotas e o texto, centrado em cada uma.</summary>
    private static (double Lo, double Hi) Interval(LayoutGroup group, double gap)
    {
        bool horizontal = group.Side is DimensionSide.Below or DimensionSide.Above;
        double textHeight = gap * 0.45;
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var d in group.Dimensions)
        {
            double a = horizontal ? d.From.U : d.From.V;
            double b = horizontal ? d.To.U : d.To.V;
            double mid = (a + b) / 2;
            double half = d.Text.Length * 0.6 * textHeight / 2;
            lo = Math.Min(lo, Math.Min(Math.Min(a, b), mid - half));
            hi = Math.Max(hi, Math.Max(Math.Max(a, b), mid + half));
        }
        double pad = gap * 0.2;
        return (lo - pad, hi + pad);
    }
}
