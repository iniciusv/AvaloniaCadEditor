using System.Text.Json;
using System.Text.Json.Nodes;

namespace AvaloniaCadEditor.Harness.Tools.Query;

public sealed class ListTargetsTool : ICadTool
{
    public string Name => "list_targets";

    public string Description =>
        "Lista as features de topo da cena (instâncias de peça e sólidos soltos) com o tipo e a caixa no mundo. " +
        "Use para descobrir os nomes aceitos pelas outras ferramentas.";

    public bool IsReadOnly => true;

    public JsonObject ParametersSchema => ToolSchema.Object();

    public async Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        var evaluation = await context.EvaluateAsync(cancellationToken);
        if (!evaluation.Success) return $"Erro: a cena não executa com as alterações atuais: {evaluation.ErrorMessage}";
        return context.Inspection.ListTargets(evaluation.Features);
    }
}
