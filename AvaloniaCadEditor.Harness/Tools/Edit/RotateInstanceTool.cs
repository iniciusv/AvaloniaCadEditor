using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Harness.Tools.Edit;

public sealed class RotateInstanceTool : ICadTool
{
    public string Name => "rotate_instance";

    public string Description =>
        "Gira uma instância de peça (ou feature de topo) em torno de um eixo, somando a rotação ao posicionamento dela " +
        "no código, como girar na tela. O pivô padrão é o centro da caixa da peça no mundo. Responde com a caixa nova.";

    public bool IsReadOnly => false;

    public JsonObject ParametersSchema => ToolSchema.Object(
        ToolSchema.String("target", TargetLookup.TargetDescription),
        ToolSchema.String("axis", "Eixo do mundo: X, Y ou Z (sinal opcional, ex.: -Z)."),
        ToolSchema.Number("degrees", "Ângulo em graus (regra da mão direita)."),
        ToolSchema.Vector("pivot", "Ponto do eixo no mundo (mm). Padrão: centro da caixa da peça.", required: false));

    public async Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        var a = new ToolArgs(args);
        string target = a.String("target");
        var axis = a.Axis("axis");
        double degrees = a.Number("degrees");
        if (Math.Abs(degrees) < 1e-9) return "Erro: degrees é zero.";

        Point3D pivot;
        if (a.OptionalVector("pivot") is { } p)
        {
            pivot = new Point3D(p.X, p.Y, p.Z);
        }
        else
        {
            var (prepared, error) = await TargetLookup.PrepareAsync(context, target, cancellationToken);
            if (prepared == null) return error!;
            pivot = Center(prepared.WorldMesh.Positions);
        }

        string verb = string.Create(CultureInfo.InvariantCulture,
            $"girada {degrees:0.###}° em torno de ({axis.X:0.###}, {axis.Y:0.###}, {axis.Z:0.###}) pelo pivô ({pivot.X:0.###}, {pivot.Y:0.###}, {pivot.Z:0.###})");
        return await PlacementEdit.ApplyAsync(context, target, verb, (feature, source, line) =>
            PlacementEdit.IsInstance(feature)
                ? PartSourceEditor.RotateInstance(source, line, pivot, axis, degrees)
                : PartSourceEditor.RotateFeature(source, line, pivot, axis, degrees),
            cancellationToken);
    }

    private static Point3D Center(float[] positions)
    {
        if (positions.Length < 3) return Point3D.Origin;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (int i = 0; i + 2 < positions.Length; i += 3)
        {
            minX = Math.Min(minX, positions[i]); maxX = Math.Max(maxX, positions[i]);
            minY = Math.Min(minY, positions[i + 1]); maxY = Math.Max(maxY, positions[i + 1]);
            minZ = Math.Min(minZ, positions[i + 2]); maxZ = Math.Max(maxZ, positions[i + 2]);
        }
        return new Point3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
    }
}
