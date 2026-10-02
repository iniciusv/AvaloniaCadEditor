using System.Collections.Generic;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.Ai.Context;

/// <summary>
/// Cena de teste com três peças, um arquivo auxiliar (Medidas) e uma booleana. As features vêm de uma
/// avaliação real, para que a origem (FeatureOrigin) seja a mesma que o app vê.
/// </summary>
internal static class ContextTestScene
{
    private static ScriptDocument Part(string name, string body = "new BoxFeature(Point3D.Origin, Largura, 10, 10) { Name = \"corpo\" }") =>
        new($"Pecas/{name}.cs",
            $"public sealed record {name}(double Largura = 10) : Part\n{{\n    protected override IFeature Build() => {body};\n}}\n");

    public static IReadOnlyList<ScriptDocument> Documents() => new[]
    {
        new ScriptDocument("Cena.cs",
            "var a = Add(new PecaA(), name: \"a_1\");\n" +
            "var b = Add(new PecaB(), at: Placement.At(50, 0, 0), name: \"b_1\");\n" +
            "var c = Add(new PecaC(), at: Placement.At(100, 0, 0), name: \"c_1\");\n" +
            "var furo_1 = new CylinderFeature(new Point3D(5, 5, 0), Vector3D.UnitZ, 2, 10) { Name = \"furo_1\", IsTransientTool = true };\n" +
            "AddFeature(furo_1);\n" +
            "var corte_1 = BooleanFeature.CreateDifference(\"corte_1\", a, furo_1);\n" +
            "AddFeature(corte_1);\n"),
        Part("PecaA"),
        Part("PecaB", "new BoxFeature(Point3D.Origin, Largura, 10, Medidas.Espessura) { Name = \"corpo\" }"),
        Part("PecaC"),
        new ScriptDocument("Pecas/Medidas.cs", "public static class Medidas\n{\n    public const double Espessura = 4;\n}\n"),
    };

    public static async Task<IReadOnlyList<IFeature>> EvaluateAsync(IReadOnlyList<ScriptDocument> documents)
    {
        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        result.Success.Should().BeTrue(because: result.ErrorMessage);
        return result.Features;
    }
}
