using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AvaloniaCadEditor.Application.Scenes;

namespace AvaloniaCadEditor.Harness.Tools.Edit;

public sealed class TranslateInstanceTool : ICadTool
{
    public string Name => "translate_instance";

    public string Description =>
        "Move uma instância de peça (ou feature de topo) somando (dx, dy, dz) mm ao posicionamento dela no código, " +
        "como arrastar na tela. Não gira: se a peça tem de acompanhar outra girada, use rotate_instance antes. " +
        "Responde com a caixa nova. Use em vez de reescrever o arquivo.";

    public bool IsReadOnly => false;

    public JsonObject ParametersSchema => ToolSchema.Object(
        ToolSchema.String("target", TargetLookup.TargetDescription),
        ToolSchema.Number("dx", "Deslocamento em X (mm, mundo)."),
        ToolSchema.Number("dy", "Deslocamento em Y (mm, mundo)."),
        ToolSchema.Number("dz", "Deslocamento em Z (mm, mundo)."));

    public Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        var a = new ToolArgs(args);
        double dx = a.OptionalNumber("dx") ?? 0, dy = a.OptionalNumber("dy") ?? 0, dz = a.OptionalNumber("dz") ?? 0;
        if (dx == 0 && dy == 0 && dz == 0) return Task.FromResult("Erro: dx, dy e dz são todos zero.");

        string verb = string.Create(CultureInfo.InvariantCulture, $"movida em ({dx:0.###}, {dy:0.###}, {dz:0.###})");
        return PlacementEdit.ApplyAsync(context, a.String("target"), verb, (feature, source, line) =>
            PlacementEdit.IsInstance(feature)
                ? PartSourceEditor.TranslateInstance(source, line, dx, dy, dz)
                : PartSourceEditor.TranslateFeature(source, line, dx, dy, dz),
            cancellationToken);
    }
}
