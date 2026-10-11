using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>Medidas do código devolvido pela IA — o "quão limpo" ficou o modelo.</summary>
internal sealed record CodeMetrics(
    int FeatureCount,
    int CodeLines,
    IReadOnlyList<int> PartParameterCounts,
    IReadOnlyList<string> NewFiles)
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
                .ToList());
    }

    /// <summary>Os arquivos da cena antes do pedido.</summary>
    public static IReadOnlyList<ScriptDocument> OriginalDocuments(RecordedContext context) =>
        context.ProjectDocuments
        ?? context.Scope?.Documents
        ?? (IReadOnlyList<ScriptDocument>)new[] { new ScriptDocument(context.FileName ?? ScriptDocument.DefaultSceneFile, context.CurrentCode) };
}
