using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>Medidas do código devolvido pela IA — o "quão limpo" e o quão editável à mão ficou o modelo.</summary>
/// <param name="Editability">O <see cref="CadScriptEditabilityLint"/> somado sobre os arquivos alterados.</param>
/// <param name="NewStyleWarnings">Avisos de estilo que os arquivos não tinham antes do pedido.</param>
internal sealed record CodeMetrics(
    int FeatureCount,
    int CodeLines,
    IReadOnlyList<int> PartParameterCounts,
    IReadOnlyList<string> NewFiles,
    EditabilityMetrics Editability,
    int TrackabilityErrors,
    int NewStyleWarnings)
{
    public int? MaxPartParameters => PartParameterCounts.Count == 0 ? null : PartParameterCounts.Max();

    public static CodeMetrics From(CadScriptTurnResult result, RecordedContext context)
    {
        var original = OriginalDocuments(context);
        var parts = PartSourceEditor.FindPartTypes(result.Changes);

        return new CodeMetrics(
            result.FeatureCount,
            result.Changes.Sum(c => c.Text.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line))),
            parts.Select(p => p.Parameters.Count).ToList(),
            result.Changes
                .Where(c => !original.Any(d => string.Equals(d.FilePath, c.FilePath, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.FilePath)
                .ToList(),
            result.Changes.Aggregate(EditabilityMetrics.Empty, (sum, c) => sum.Add(CadScriptEditabilityLint.Analyze(c.Text).Metrics)),
            result.Changes.Sum(c => CadScriptEditabilityLint.Analyze(c.Text).Errors.Count()),
            result.Changes.Sum(c => EditabilityStyleFeedback.NewWarnings(
                original.FirstOrDefault(d => string.Equals(d.FilePath, c.FilePath, StringComparison.OrdinalIgnoreCase))?.Text,
                c.Text).Count));
    }

    /// <summary>Os arquivos da cena antes do pedido.</summary>
    public static IReadOnlyList<ScriptDocument> OriginalDocuments(RecordedContext context) =>
        context.ProjectDocuments
        ?? context.Scope?.Documents
        ?? (IReadOnlyList<ScriptDocument>)new[] { new ScriptDocument(context.FileName ?? ScriptDocument.DefaultSceneFile, context.CurrentCode) };
}
