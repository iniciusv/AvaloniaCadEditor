using System.Collections.Generic;
using System.Text;

namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>
/// O dialeto condensado do <see cref="LeanPromptProfile"/> mais uma orientação de estilo para modelos
/// simples (<see cref="SimplePromptRules.Style"/>): poucos parâmetros, poucos sólidos, sem opcionais.
/// Comparado ao enxuto, isola o efeito do estilo.
/// </summary>
public sealed class SimplePromptProfile : ICadScriptPromptProfile
{
    public const string ProfileId = "simples";

    public string Id => ProfileId;
    public string DisplayName => "Simples (poucos parâmetros)";
    public string Description => "Regras condensadas e estilo de modelo limpo: no máximo 3 parâmetros por peça, sem opcionais.";

    public IReadOnlyList<CadScriptPromptExample> Examples { get; } = new CadScriptPromptExample[]
    {
        new(CadScriptExampleKind.Script, SimplePromptRules.ScriptExample),
        new(CadScriptExampleKind.Script, LeanPromptRules.TubeExample),
        new(CadScriptExampleKind.Part, SimplePromptRules.PartExample),
        new(CadScriptExampleKind.SceneUsingPart, SimplePromptRules.SceneUsingPartExample)
    };

    public string BuildSystemPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine(LeanPromptRules.Intro);
        sb.AppendLine();
        sb.AppendLine(LeanPromptRules.Formats);
        sb.AppendLine();
        sb.AppendLine(LeanPromptRules.Rules);
        sb.AppendLine();
        sb.AppendLine(SimplePromptRules.Style);
        sb.AppendLine();
        sb.AppendLine("API DISPONÍVEL (gerada do código atual — não invente membros):");
        sb.AppendLine(CadScriptApiReference.Build());
        sb.AppendLine("EXEMPLOS — script:");
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.Script);
        sb.AppendLine("EXEMPLO — peça (Pecas/Suporte.cs) e o arquivo de entrada que a usa:");
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.Part);
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.SceneUsingPart);
        sb.AppendLine();
        sb.AppendLine(CadScriptResponseFormat.Text);

        return sb.ToString();
    }
}
