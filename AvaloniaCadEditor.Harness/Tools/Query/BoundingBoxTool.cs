using System.Text.Json;
using System.Text.Json.Nodes;

namespace AvaloniaCadEditor.Harness.Tools.Query;

public sealed class BoundingBoxTool : ICadTool
{
    public string Name => "get_bounding_box";

    public string Description =>
        "Caixa delimitadora do alvo no referencial da peça e no mundo, e onde fica o (0, 0, 0) do código. " +
        "Use antes de posicionar ou girar algo em relação a esta peça.";

    public bool IsReadOnly => true;

    public JsonObject ParametersSchema => ToolSchema.Object(
        ToolSchema.String("target", TargetLookup.TargetDescription, required: false));

    public async Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        var (target, error) = await TargetLookup.PrepareAsync(context, new ToolArgs(args).OptionalString("target"), cancellationToken);
        return target == null ? error! : context.Inspection.DescribeBoundingBox(target);
    }
}
