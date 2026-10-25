using System.Text.Json;
using System.Text.Json.Nodes;
using AvaloniaCadEditor.Harness.Agents;

namespace AvaloniaCadEditor.Harness.Tools.Query;

/// <summary>
/// Pergunta ao Inspetor (outro modelo, normalmente local) e recebe só o resumo: as consultas
/// pesadas acontecem no contexto dele, não no do Editor.
/// </summary>
public sealed class InspectTool : ICadTool
{
    private readonly InspectorAgent _inspector;

    public InspectTool(InspectorAgent inspector)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    /// <summary>O último relatório (para a UI e a medição).</summary>
    public InspectorReport? LastReport { get; private set; }

    public string Name => "inspect";

    public string Description =>
        "Delega uma pergunta de geometria ao Inspetor, que examina a cena com as ferramentas de leitura (vistas, cotas, " +
        "caixas) e devolve um resumo curto. Use para perguntas que exigiriam várias consultas, ex.: 'em que coordenadas " +
        "do código ficam os furos de fixação da placa_a e qual a distância entre eles?'.";

    public bool IsReadOnly => true;

    public JsonObject ParametersSchema => ToolSchema.Object(
        ToolSchema.String("question", "A pergunta, com os nomes dos alvos quando souber."));

    public async Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        string question = new ToolArgs(args).String("question");
        string request =
            $"### PERGUNTA DO EDITOR\n{question}\n\n" +
            $"Arquivo ativo: {context.ActiveFile}" +
            (context.Context.SelectedFeatureName is { } selected ? $" · seleção: {selected}" : "") +
            ". Use list_targets para ver os nomes.";

        LastReport = await _inspector.InspectAsync(request, context, cancellationToken);
        return LastReport.Summary;
    }
}
