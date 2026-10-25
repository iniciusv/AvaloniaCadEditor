using System.Text.Json;
using System.Text.Json.Nodes;
using AvaloniaCadEditor.Domain.Entities;

namespace AvaloniaCadEditor.Harness.Tools.Query;

/// <summary>As cotas automáticas da vista — o mesmo texto do "Copiar" da folha de cotas.</summary>
public sealed class DimensionsTool : ICadTool
{
    public string Name => "get_dimensions";

    public string Description =>
        "Cotas automáticas do alvo numa vista ortogonal (não há cotas na Isometric): cadeias de cotas, diâmetros, " +
        "sólidos na vista e linhas de centro, no mesmo referencial de get_view. Use para medir distâncias entre furos, " +
        "bordas e alinhamentos sem calcular a partir do código.";

    public bool IsReadOnly => true;

    public JsonObject ParametersSchema => ToolSchema.Object(
        ToolSchema.String("target", TargetLookup.TargetDescription, required: false),
        ToolSchema.String("view", "Vista ortogonal (padrão Top).", required: false,
            values: ToolArgs.ViewNames.Where(v => v != "Isometric")));

    public async Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        var a = new ToolArgs(args);
        var view = a.View("view", StandardView.Top);

        var (target, error) = await TargetLookup.PrepareAsync(context, a.OptionalString("target"), cancellationToken);
        return target == null ? error! : context.Inspection.DescribeDimensions(target, view);
    }
}
