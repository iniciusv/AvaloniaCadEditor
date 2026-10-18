using System.Text;
using System.Text.Json;
using AiCore.Models;
using AvaloniaCadEditor.Harness.Tools;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>
/// <c>--dump &lt;pasta&gt;</c>: grava, por rodada, a conversa exatamente como foi para o modelo — system
/// prompt, pedido com o contexto, ferramentas oferecidas, cada chamada e cada resultado. Serve para ver o
/// que a IA tinha em mãos quando acertou ou errou.
/// </summary>
internal static class PromptDump
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(string directory, RunOutcome outcome, IReadOnlyList<ChatMessage> history, bool tools)
    {
        Directory.CreateDirectory(directory);
        string variant = outcome.Variant.Replace(' ', '_').Replace('/', '_').Replace(':', '_').Replace('@', '_');
        string path = Path.Combine(directory, $"{outcome.Case}#{outcome.Round}__{outcome.Profile}__{variant}__r{outcome.Repetition}.md");

        var sb = new StringBuilder();
        sb.AppendLine($"# {outcome.Case} · {outcome.Variant} · r{outcome.Repetition} — {(outcome.Passed ? "passou" : "falhou")}");
        sb.AppendLine();
        sb.AppendLine($"{history.Count} mensagens · {outcome.ToolSteps} chamadas de ferramenta · {outcome.PromptTokens} tokens de prompt · {outcome.Seconds:0} s");
        foreach (var check in outcome.Checks)
            sb.AppendLine($"- {(check.Passed ? "✓" : "✗")} {check.Name}{(string.IsNullOrEmpty(check.Detail) ? "" : $" — {check.Detail}")}");
        if (outcome.Error != null) sb.AppendLine($"- erro: {outcome.Error}");

        int index = 0;
        foreach (var message in history)
        {
            index++;
            sb.AppendLine();
            string title = message.Role switch
            {
                "system" => "system (pré-prompt)",
                "tool" => $"tool — resultado de {message.ToolCallId}",
                _ => message.Role
            };
            sb.AppendLine($"## {index}. {title} ({message.Content.Length} caracteres)");
            if (message.IsAssistant && message.ReasoningContent.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<details><summary>raciocínio</summary>");
                sb.AppendLine();
                sb.AppendLine(Fence(message.ReasoningContent));
                sb.AppendLine("</details>");
            }
            if (message.Content.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Fence(message.Content));
            }
            foreach (var call in message.ToolCalls)
            {
                sb.AppendLine();
                sb.AppendLine($"→ chamada `{call.Name}` ({call.Id}): `{call.ArgumentsJson}`");
            }
        }

        if (tools)
        {
            sb.AppendLine();
            sb.AppendLine("## Ferramentas oferecidas (campo `tools` da requisição)");
            foreach (var tool in ToolRegistry.Default().Definitions())
            {
                sb.AppendLine();
                sb.AppendLine($"### `{tool.Name}`");
                sb.AppendLine(tool.Description);
                sb.AppendLine();
                sb.AppendLine("```json");
                sb.AppendLine(tool.Parameters.ToJsonString(Indented));
                sb.AppendLine("```");
            }
        }

        File.WriteAllText(path, sb.ToString());
        return path;
    }

    /// <summary>Bloco de código com uma cerca maior que qualquer sequência de crases do texto.</summary>
    private static string Fence(string text)
    {
        int longest = 0, run = 0;
        foreach (char c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        string fence = new('`', Math.Max(3, longest + 1));
        return $"{fence}text\n{text.TrimEnd()}\n{fence}";
    }
}
