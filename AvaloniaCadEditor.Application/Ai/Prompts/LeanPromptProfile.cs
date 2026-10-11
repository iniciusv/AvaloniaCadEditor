using System.Collections.Generic;
using System.Text;

namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>
/// Mesmo dialeto do perfil atual com bem menos texto: regras condensadas (<see cref="LeanPromptRules"/>)
/// e poucos exemplos. Mede quanto do prompt completo é de fato necessário.
/// </summary>
public sealed class LeanPromptProfile : ICadScriptPromptProfile
{
    public const string ProfileId = "enxuto";

    public string Id => ProfileId;
    public string DisplayName => "Enxuto";
    public string Description => "Mesmas regras, condensadas, com a API inteira e só 4 exemplos.";

    public IReadOnlyList<CadScriptPromptExample> Examples { get; } = new CadScriptPromptExample[]
    {
        new(CadScriptExampleKind.Script, LeanPromptRules.ScriptExample),
        new(CadScriptExampleKind.Script, LeanPromptRules.TubeExample),
        new(CadScriptExampleKind.Part, CadScriptRules.PartExampleChapaFurada),
        new(CadScriptExampleKind.SceneUsingPart, LeanPromptRules.SceneUsingPartExample)
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
        sb.AppendLine("API DISPONÍVEL (gerada do código atual — não invente membros):");
        sb.AppendLine(CadScriptApiReference.Build());
        sb.AppendLine("EXEMPLOS — script:");
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.Script);
        sb.AppendLine("EXEMPLO — peça (Pecas/ChapaFurada.cs) e o arquivo de entrada que a usa:");
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.Part);
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.SceneUsingPart);
        sb.AppendLine();
        sb.AppendLine(CadScriptResponseFormat.Text);

        return sb.ToString();
    }
}
