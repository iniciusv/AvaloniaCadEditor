using System.Collections.Generic;
using System.Text;

namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>
/// O dialeto condensado do <see cref="LeanPromptProfile"/> mais o estilo "código editável à mão"
/// (<see cref="EditablePromptRules.Style"/>): números nas coordenadas, até 3 parâmetros por peça e nada de laços,
/// funções ou atributos sem pedido explícito. É o único perfil que pede à sessão de chat uma rodada de correção
/// de estilo com os avisos do <see cref="CadScriptEditabilityLint"/> (<see cref="RequestsEditableCode"/>).
/// </summary>
public sealed class EditablePromptProfile : ICadScriptPromptProfile
{
    public const string ProfileId = "editavel";

    public string Id => ProfileId;
    public string DisplayName => "Editável (código simples)";
    public string Description => "Números nas coordenadas, até 3 parâmetros por peça e sem laços ou funções, a menos que o pedido peça: o código que as ferramentas manuais conseguem editar.";

    public bool RequestsEditableCode => true;

    public IReadOnlyList<CadScriptPromptExample> Examples { get; } = new CadScriptPromptExample[]
    {
        new(CadScriptExampleKind.Script, EditablePromptRules.ScriptExample),
        new(CadScriptExampleKind.Script, LeanPromptRules.TubeExample),
        new(CadScriptExampleKind.Part, EditablePromptRules.PartExample),
        new(CadScriptExampleKind.SceneUsingPart, EditablePromptRules.SceneUsingPartExample)
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
        sb.AppendLine(EditablePromptRules.Style);
        sb.AppendLine();
        sb.AppendLine("API DISPONÍVEL (gerada do código atual — não invente membros):");
        sb.AppendLine(CadScriptApiReference.Build());
        sb.AppendLine("EXEMPLOS — script:");
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.Script);
        sb.AppendLine("EXEMPLO — peça (Pecas/Chapa.cs) e o arquivo de entrada que a usa:");
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.Part);
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.SceneUsingPart);
        sb.AppendLine();
        sb.AppendLine(CadScriptResponseFormat.Text);

        return sb.ToString();
    }
}
