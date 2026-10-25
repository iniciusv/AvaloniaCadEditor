using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Harness.Inspection;

public sealed record TargetResolution(IFeature? Feature, string? Error);

/// <summary>
/// Acha a feature de topo da cena que a IA quer inspecionar, a partir de um nome solto. A IA vê o
/// código, então pode chamar tanto pelo nome da instância ("placa_a") quanto pelo tipo da peça
/// ("PlacaSuporte"), desde que o tipo só tenha uma instância.
/// </summary>
public static class TargetResolver
{
    private const int MaxListed = 40;

    /// <param name="target">O nome pedido; vazio = <paramref name="fallback"/> (a seleção) ou a única feature.</param>
    public static TargetResolution Resolve(IReadOnlyList<IFeature> features, string? target, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(target)) target = fallback;

        if (string.IsNullOrWhiteSpace(target))
        {
            var solids = features.Where(f => !f.IsTransientTool).ToList();
            return solids.Count == 1
                ? new TargetResolution(solids[0], null)
                : new TargetResolution(null, "Diga qual alvo inspecionar. " + ListNames(features));
        }

        var byName = features.FirstOrDefault(f => string.Equals(f.Name, target, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return new TargetResolution(byName, null);

        var byType = features.OfType<PartInstance>()
            .Where(p => string.Equals(p.PartTypeName, target, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byType.Count == 1) return new TargetResolution(byType[0], null);
        if (byType.Count > 1)
            return new TargetResolution(null, $"'{target}' tem {byType.Count} instâncias: {string.Join(", ", byType.Select(p => p.Name))}. Use o nome da instância.");

        return new TargetResolution(null, $"Nenhuma feature de topo se chama '{target}'. " + ListNames(features));
    }

    private static string ListNames(IReadOnlyList<IFeature> features)
    {
        if (features.Count == 0) return "A cena não tem features.";
        var names = features.Take(MaxListed).Select(f => f is PartInstance p ? $"{f.Name} ({p.PartTypeName})" : f.Name);
        string more = features.Count > MaxListed ? $" … e mais {features.Count - MaxListed}" : "";
        return "Alvos válidos: " + string.Join(", ", names) + more + ".";
    }
}
