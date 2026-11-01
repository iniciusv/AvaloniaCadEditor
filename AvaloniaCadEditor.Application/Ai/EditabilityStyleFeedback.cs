using System.Text;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// A rodada de correção de estilo dos perfis que pedem código editável à mão
/// (<see cref="Prompts.ICadScriptPromptProfile.RequestsEditableCode"/>): depois que a resposta já compilou e
/// executou, os avisos <b>novos</b> do <see cref="CadScriptEditabilityLint"/> voltam à IA uma vez. Avisos que o
/// arquivo já tinha antes do pedido não contam: num arquivo antigo, a regra é manter o estilo dele.
/// Usado pelo <see cref="CadScriptChatSession"/> e pela sessão do harness.
/// </summary>
public static class EditabilityStyleFeedback
{
    /// <summary>Mensagem para a IA com os avisos novos de cada arquivo alterado, ou null se não houver nenhum.</summary>
    public static string? Build(IReadOnlyList<ScriptDocument> changes, CadScriptContext context)
    {
        var sb = new StringBuilder();

        foreach (var change in changes)
        {
            var warnings = NewWarnings(OriginalText(change.FilePath, context), change.Text);
            if (warnings.Count == 0) continue;

            if (context.IsMultiFile) sb.Append(change.FilePath).AppendLine(":");
            sb.AppendLine(EditabilityReport.Format(warnings, max: 10));
        }

        if (sb.Length == 0) return null;

        string answer = context.IsMultiFile
            ? "responda com um bloco ```cadscript file=<caminho> COMPLETO por arquivo que mudar"
            : "responda com o SCRIPT COMPLETO";
        return
            "O código funciona, mas não está no estilo editável à mão: a interface só consegue editar números, e estes " +
            "trechos ela não vai conseguir mexer:\n" + sb.ToString().TrimEnd() + "\n" +
            $"Reescreva só esses trechos, com a mesma geometria, e {answer}. Se o pedido pediu explicitamente código " +
            "paramétrico, repetição ou essas opções, responda só com uma frase confirmando, sem bloco de código.";
    }

    /// <summary>
    /// Os avisos de <paramref name="changed"/> que <paramref name="original"/> não tinha. Compara regra e mensagem,
    /// sem a linha (que muda quando a IA acrescenta código acima), contando repetições.
    /// </summary>
    public static IReadOnlyList<EditabilityFinding> NewWarnings(string? original, string changed)
    {
        var before = (original == null ? Enumerable.Empty<EditabilityFinding>() : CadScriptEditabilityLint.Analyze(original).Warnings)
            .GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new List<EditabilityFinding>();
        foreach (var warning in CadScriptEditabilityLint.Analyze(changed).Warnings)
        {
            var key = Key(warning);
            if (before.TryGetValue(key, out int count) && count > 0)
            {
                before[key] = count - 1;
                continue;
            }
            result.Add(warning);
        }
        return result;
    }

    private static (string, string) Key(EditabilityFinding finding) => (finding.RuleId, finding.Message);

    private static string? OriginalText(string filePath, CadScriptContext context)
    {
        var fromScope = context.Scope?.Documents.FirstOrDefault(d => SamePath(d.FilePath, filePath))
                        ?? context.ProjectDocuments?.FirstOrDefault(d => SamePath(d.FilePath, filePath));
        if (fromScope != null) return fromScope.Text;

        string active = context.FileName ?? ScriptDocument.DefaultSceneFile;
        return !context.IsMultiFile && SamePath(active, filePath) ? context.CurrentCode : null;
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
