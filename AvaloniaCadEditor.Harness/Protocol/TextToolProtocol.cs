using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiCore.Models;
using AvaloniaCadEditor.Harness.Tools;

namespace AvaloniaCadEditor.Harness.Protocol;

/// <summary>
/// Ferramentas sem function calling nativo: o modelo escreve a chamada num bloco
/// <c>```tool {"name": ..., "arguments": {...}}```</c> e o resultado volta como mensagem do usuário.
/// Serve a servidores/modelos locais sem template de tools (e ao modelo que escreve a chamada no
/// texto mesmo tendo tools nativas, como o fallback do LM Studio).
/// </summary>
public static partial class TextToolProtocol
{
    [GeneratedRegex(@"```tool\s*\r?\n(?<json>.*?)```", RegexOptions.Singleline)]
    private static partial Regex ToolBlock();

    /// <summary>Instruções para o modelo, com a lista de ferramentas.</summary>
    public static string Instructions(ToolRegistry tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### FERRAMENTAS (protocolo em texto)");
        sb.AppendLine("Este servidor não tem chamadas de ferramenta nativas. Para chamar uma ferramenta, responda SÓ com um ou mais blocos assim, e nada mais:");
        sb.AppendLine("```tool");
        sb.AppendLine("{\"name\": \"get_view\", \"arguments\": {\"target\": \"placa_a\", \"view\": \"Top\"}}");
        sb.AppendLine("```");
        sb.AppendLine("Os resultados voltam na próxima mensagem. Quando terminar, responda normalmente (sem blocos tool).");
        sb.AppendLine();
        foreach (var tool in tools.All.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            sb.AppendLine($"- {tool.Name}: {tool.Description}");
            sb.AppendLine($"  argumentos (JSON Schema): {tool.ParametersSchema.ToJsonString()}");
        }
        return sb.ToString();
    }

    /// <summary>As chamadas escritas no texto; vazio se não houver nenhuma (ou se nenhuma for JSON válido).</summary>
    public static IReadOnlyList<ToolCall> Parse(string content)
    {
        var calls = new List<ToolCall>();
        foreach (Match match in ToolBlock().Matches(content ?? string.Empty))
        {
            try
            {
                using var doc = JsonDocument.Parse(match.Groups["json"].Value);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                    continue;

                string arguments = root.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object ? args.GetRawText() : "{}";
                calls.Add(new ToolCall($"text_{calls.Count}", name.GetString()!, arguments));
            }
            catch (JsonException)
            {
                // Bloco malformado: ignora; se nenhum valer, a resposta é tratada como final.
            }
        }
        return calls;
    }

    /// <summary>A mensagem com os resultados, na ordem das chamadas.</summary>
    public static string FormatResults(IEnumerable<(ToolCall Call, string Result)> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### RESULTADOS DAS FERRAMENTAS");
        foreach (var (call, result) in results)
        {
            sb.AppendLine();
            sb.AppendLine($"#### {call.Name} {call.ArgumentsJson}");
            sb.AppendLine(result);
        }
        return sb.ToString();
    }
}
