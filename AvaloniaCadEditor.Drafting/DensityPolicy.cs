namespace AvaloniaCadEditor.Drafting;

/// <summary>Limites de densidade do desenho e níveis que o usuário mandou mostrar mesmo assim.</summary>
public sealed record DraftingOptions
{
    /// <summary>Fileiras de cota por lado da peça antes de começar a esconder.</summary>
    public int MaxRowsPerSide { get; init; } = 6;

    /// <summary>Cruzamentos de perna tolerados por lado.</summary>
    public int MaxCrossingsPerSide { get; init; } = 0;

    /// <summary>Níveis que nunca são escondidos pela política (os toggles da legenda).</summary>
    public IReadOnlySet<int> ForcedLevels { get; init; } = new HashSet<int>();

    public static readonly DraftingOptions Default = new();
}

/// <summary>Por que uma cota foi escondida.</summary>
public enum SuppressionReason
{
    /// <summary>O lado tinha fileiras demais.</summary>
    Rows,

    /// <summary>Havia pernas cruzando outras cotas.</summary>
    Crossings
}

/// <summary>Uma cota que a política escondeu (já com nível e lado).</summary>
public sealed record SuppressedDimension(DraftDimension Dimension, SuppressionReason Reason);

/// <summary>
/// Decide o que NÃO mostrar quando um lado da peça fica denso demais. Por lado, de forma iterativa:
/// posiciona, mede fileiras e cruzamentos e, se passou do limite, esconde o próximo candidato —
/// primeiro toda a localização (nível 2) daquele lado, depois as cadeias de nível 1, uma a uma,
/// começando pela que mais cruza (ou, se o problema é só de fileiras, pela mais externa). Os totais
/// (nível 3), o contorno, as chamadas (sem perna) e os níveis forçados nunca saem.
/// </summary>
public static class DensityPolicy
{
    public static (IReadOnlyList<DraftDimension> Placed, IReadOnlyList<SuppressedDimension> Suppressed) Apply(
        IReadOnlyList<LayoutGroup> groups, P2 partMin, P2 partMax, double gap, double tolerance, DraftingOptions? options = null)
    {
        var opts = options ?? DraftingOptions.Default;
        var active = groups.ToList();
        var suppressed = new List<SuppressedDimension>();
        var exhausted = new HashSet<DimensionSide>();

        while (true)
        {
            var placedGroups = DimensionLayout.PlaceGroups(active, partMin, partMax, gap);
            var flat = placedGroups.SelectMany(p => p.Dimensions).ToList();
            var owner = placedGroups.SelectMany(p => p.Dimensions.Select(_ => p)).ToList();
            var crossings = DimensionCrossings.Find(flat, gap, tolerance);

            // Um nível forçado pelo usuário aparece com o que ele causar: os cruzamentos e as fileiras
            // dele não contam, senão mostrá-lo esconderia outro nível no lugar. O contorno vale como
            // forçado: ele nunca sai, e contar os cruzamentos dele esconderia toda cadeia do mesmo lado.
            bool Forced(PlacedGroup g) => g.Group.IsContour || opts.ForcedLevels.Contains(g.Group.Level);
            var violation = placedGroups
                .GroupBy(p => p.Group.Side)
                .Where(s => !exhausted.Contains(s.Key))
                .Select(s => (Side: s.Key,
                    Rows: s.Where(p => !Forced(p)).Select(p => p.Row + 1).DefaultIfEmpty(0).Max(),
                    Crossings: crossings.Count(c => owner[c.Leg].Group.Side == s.Key && !Forced(owner[c.Leg]) && !Forced(owner[c.Crossed]))))
                .Where(s => s.Rows > opts.MaxRowsPerSide || s.Crossings > opts.MaxCrossingsPerSide)
                .OrderByDescending(s => s.Crossings).ThenByDescending(s => s.Rows)
                .FirstOrDefault();

            if (violation == default) return (flat, suppressed);

            var droppable = placedGroups
                .Where(p => p.Group.Side == violation.Side && p.Group.Level != 3 && !p.Group.IsContour && !opts.ForcedLevels.Contains(p.Group.Level))
                .ToList();
            if (droppable.Count == 0)
            {
                exhausted.Add(violation.Side);
                continue;
            }

            var reason = violation.Crossings > opts.MaxCrossingsPerSide ? SuppressionReason.Crossings : SuppressionReason.Rows;
            var level2 = droppable.Where(p => p.Group.Level == 2).ToList();
            List<PlacedGroup> drop;
            if (level2.Count > 0)
            {
                drop = level2;
            }
            else if (reason == SuppressionReason.Crossings)
            {
                // A cadeia que mais participa de cruzamentos — com a perna dela ou sendo atravessada.
                int Score(PlacedGroup g) => crossings.Count(c => owner[c.Leg] == g || owner[c.Crossed] == g);
                drop = [droppable.OrderByDescending(Score).ThenByDescending(g => g.Row).First()];
            }
            else
            {
                drop = [droppable.OrderByDescending(g => g.Row).ThenByDescending(g => Span(g, gap)).First()];
            }

            foreach (var g in drop)
            {
                active.Remove(g.Group);
                suppressed.AddRange(g.Dimensions.Select(d => new SuppressedDimension(d, reason)));
            }
        }
    }

    private static double Span(PlacedGroup g, double gap)
    {
        var (lo, hi) = DimensionLayout.Interval(g.Group, gap);
        return hi - lo;
    }
}
