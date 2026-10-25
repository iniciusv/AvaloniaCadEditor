using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Harness.Inspection;

namespace AvaloniaCadEditor.Harness.Tools;

/// <summary>Avalia a cópia de trabalho e acha a feature de topo pedida (ou explica por que não deu).</summary>
internal static class TargetLookup
{
    public const string TargetDescription =
        "Nome da instância/feature de topo (ex.: placa_suporte_carrinho_y_a) ou o tipo da peça, se houver uma só instância. Vazio = a seleção da UI.";

    public static async Task<(IFeature? Feature, string? Error)> FindAsync(CadToolContext context, string? target, CancellationToken cancellationToken)
    {
        var evaluation = await context.EvaluateAsync(cancellationToken);
        if (!evaluation.Success)
            return (null, $"Erro: a cena não executa com as alterações atuais: {evaluation.ErrorMessage}");

        var resolved = TargetResolver.Resolve(evaluation.Features, target, context.Context.SelectedFeatureName);
        return resolved.Feature == null ? (null, "Erro: " + resolved.Error) : (resolved.Feature, null);
    }

    public static async Task<(InspectionTarget? Target, string? Error)> PrepareAsync(CadToolContext context, string? target, CancellationToken cancellationToken)
    {
        var (feature, error) = await FindAsync(context, target, cancellationToken);
        return feature == null ? (null, error) : (context.Inspection.Prepare(feature), null);
    }
}
