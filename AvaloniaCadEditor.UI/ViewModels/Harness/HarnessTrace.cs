using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Harness;
using AvaloniaCadEditor.Harness.Agents;

namespace AvaloniaCadEditor.UI.ViewModels.Harness;

/// <summary>
/// O que o harness fez no último turno, em linhas curtas para o balão do chat (seção recolhível
/// "Ferramentas"). Sessão sem ferramentas = lista vazia; o balão não precisa saber de harness.
/// </summary>
public static class HarnessTrace
{
    private const int ResultPreview = 160;

    public static IReadOnlyList<string> Describe(ICadScriptChatSession session)
    {
        if (session is not HarnessChatSession harness) return Array.Empty<string>();

        var lines = new List<string>();
        if (harness.LastTurnSkills.Count > 0)
            lines.Add("skills: " + string.Join(", ", harness.LastTurnSkills.Select(s => s.Id)));
        if (harness.LastPreInspection is { } inspection)
        {
            lines.Add($"Inspetor ({harness.Inspector?.ProfileName}): {inspection.Steps.Count} consulta(s){(inspection.HitLimit ? " · limite atingido" : "")}");
            lines.AddRange(inspection.Steps.Select(s => "    " + Line(s)));
            lines.Add("    resumo: " + Preview(inspection.Summary));
        }

        lines.AddRange(harness.LastTurnSteps.Select(Line));
        if (harness.Mode == ToolMode.Text) lines.Add("(servidor sem function calling: ferramentas pelo protocolo em texto)");
        return lines;
    }

    private static string Line(AgentStep step)
    {
        string args = step.ArgumentsJson == "{}" ? "" : " " + step.ArgumentsJson;
        string cached = step.Cached ? " (cache)" : "";
        string time = step.Duration.TotalSeconds >= 1 ? $" · {step.Duration.TotalSeconds:0.#}s" : "";
        return $"{step.Tool}{args}{cached}{time} → {Preview(step.Result)}";
    }

    private static string Preview(string text)
    {
        string first = text.ReplaceLineEndings(" ").Trim();
        return first.Length <= ResultPreview ? first : first[..ResultPreview] + "…";
    }
}
