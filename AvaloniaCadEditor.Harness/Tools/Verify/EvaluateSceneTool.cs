using System.Text.Json;
using System.Text.Json.Nodes;

namespace AvaloniaCadEditor.Harness.Tools.Verify;

/// <summary>Valida e executa a cena com as alterações feitas até agora no turno.</summary>
public sealed class EvaluateSceneTool : ICadTool
{
    public string Name => "evaluate_scene";

    public string Description =>
        "Valida (regras do CadScript) e executa a cena com as alterações feitas neste turno. Responde 'ok' com o " +
        "número de features, ou o erro de compilação/execução. Use depois de editar e antes de dar a resposta final.";

    public bool IsReadOnly => true;

    public JsonObject ParametersSchema => ToolSchema.Object();

    public async Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        var check = await context.CheckAsync(cancellationToken);
        string files = context.Changes.Count == 0 ? "nenhum arquivo alterado ainda" : "alterados: " + string.Join(", ", context.Changes.Select(c => c.FilePath));
        return check.IsValid
            ? $"ok — a cena executa com {check.Evaluation!.Features.Count} features ({files})."
            : $"Erro: {check.Error} ({files})";
    }
}
