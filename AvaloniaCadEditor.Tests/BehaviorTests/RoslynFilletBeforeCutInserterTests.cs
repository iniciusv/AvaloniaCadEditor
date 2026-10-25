using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Chanfro de todas as arestas num sólido furado vai ANTES dos furos: depois deles, o caminho genérico
/// por malha faz uma booleana por segmento da boca de cada furo e a UI travava por minutos.
/// </summary>
public class RoslynFilletBeforeCutInserterTests
{
    private const string Fillet =
        "var fillet_1 = new FilletFeature(name: \"fillet_1\", baseFeature: corpo, radius: 2.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };\r\nAddFeature(fillet_1);";

    [Fact]
    public void SubtracaoNomeada_FilletEntraAntesEAViraABase()
    {
        var script =
            "var corpo = new BoxFeature(new Point3D(0,0,0), 20, 20, 20);\r\n" +
            "AddFeature(corpo);\r\n" +
            "// furos de fixação\r\n" +
            "var furos = new BooleanFeature(name: \"furos\", operation: BooleanOperationType.Subtract, baseFeature: corpo, toolFeatures: new IFeature[] { a, b });\r\n" +
            "AddFeature(furos);\r\n";

        bool ok = RoslynFilletBeforeCutInserter.TryInsert(script, "furos", "corpo", "fillet_1", Fillet, out var result);

        ok.Should().BeTrue();
        result.Should().Be(
            "var corpo = new BoxFeature(new Point3D(0,0,0), 20, 20, 20);\r\n" +
            "AddFeature(corpo);\r\n" +
            "var fillet_1 = new FilletFeature(name: \"fillet_1\", baseFeature: corpo, radius: 2.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };\r\n" +
            "AddFeature(fillet_1);\r\n" +
            "// furos de fixação\r\n" +
            "var furos = new BooleanFeature(name: \"furos\", operation: BooleanOperationType.Subtract, baseFeature: fillet_1, toolFeatures: new IFeature[] { a, b });\r\n" +
            "AddFeature(furos);\r\n");
    }

    [Fact]
    public void SubtracaoPosicional_TrocaOTerceiroArgumento()
    {
        var script = "var furos = new BooleanFeature(\"furos\", BooleanOperationType.Subtract, corpo, new IFeature[] { a });\r\nAddFeature(furos);\r\n";

        RoslynFilletBeforeCutInserter.TryInsert(script, "furos", "corpo", "fillet_1", Fillet, out var result).Should().BeTrue();

        result.Should().Contain("new BooleanFeature(\"furos\", BooleanOperationType.Subtract, fillet_1, new IFeature[] { a })");
        result.IndexOf("var fillet_1").Should().BeLessThan(result.IndexOf("var furos"));
    }

    [Fact]
    public void CreateDifference_TrocaABase()
    {
        var script = "var furos = BooleanFeature.CreateDifference(\"furos\", corpo, a, b);\r\nAddFeature(furos);\r\n";

        RoslynFilletBeforeCutInserter.TryInsert(script, "furos", "corpo", "fillet_1", Fillet, out var result).Should().BeTrue();

        result.Should().Contain("BooleanFeature.CreateDifference(\"furos\", fillet_1, a, b)");
    }

    [Fact]
    public void DentroDeBloco_FilletHerdaAIndentacao()
    {
        var script =
            "if (true)\n" +
            "{\n" +
            "    var furos = new BooleanFeature(name: \"furos\", operation: BooleanOperationType.Subtract, baseFeature: corpo, toolFeatures: new IFeature[] { a });\n" +
            "    AddFeature(furos);\n" +
            "}\n";

        RoslynFilletBeforeCutInserter.TryInsert(script, "furos", "corpo", "fillet_1", Fillet, out var result).Should().BeTrue();

        result.Should().Contain(
            "{\n" +
            "    var fillet_1 = new FilletFeature(name: \"fillet_1\", baseFeature: corpo, radius: 2.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };\n" +
            "    AddFeature(fillet_1);\n" +
            "    var furos =");
    }

    [Fact]
    public void BaseDiferenteDaEsperada_NaoMexe()
    {
        var script = "var furos = new BooleanFeature(name: \"furos\", operation: BooleanOperationType.Subtract, baseFeature: corpo.Transformed(p), toolFeatures: new IFeature[] { a });\r\n";

        RoslynFilletBeforeCutInserter.TryInsert(script, "furos", "corpo", "fillet_1", Fillet, out var result).Should().BeFalse();

        result.Should().Be(script);
    }

    [Fact]
    public void NomeDoFilletJaDeclarado_NaoMexe()
    {
        var script =
            "var fillet_1 = 3;\r\n" +
            "var furos = new BooleanFeature(name: \"furos\", operation: BooleanOperationType.Subtract, baseFeature: corpo, toolFeatures: new IFeature[] { a });\r\n";

        RoslynFilletBeforeCutInserter.TryInsert(script, "furos", "corpo", "fillet_1", Fillet, out _).Should().BeFalse();
    }

    [Fact]
    public void FilletTool_TodasAsArestasDeSolidoFurado_PedeFilletAntesDaSubtracaoMaisInterna()
    {
        var corpo = new BoxFeature(Point3D.Origin, 20, 20, 20) { Name = "corpo" };
        var furo1 = new CylinderFeature(new Point3D(5, 5, -1), new Vector3D(0, 0, 1), 2, 22) { Name = "furo1" };
        var furo2 = new CylinderFeature(new Point3D(15, 15, -1), new Vector3D(0, 0, 1), 2, 22) { Name = "furo2" };
        var furos = new BooleanFeature("furos", BooleanOperationType.Subtract, corpo, new IFeature[] { furo1 });
        var maisFuros = new BooleanFeature("mais_furos", BooleanOperationType.Subtract, furos, new IFeature[] { furo2 });
        var selection = new SelectionService();
        selection.SelectFeature(maisFuros);
        var tool = new FilletTool(selection, FilletMode.Bevel);

        string? code = null;
        (string CutVar, string BaseVar, string FilletVar, string FilletCode, string FallbackCode)? request = null;
        tool.CodeGenerated += (_, c) => code = c;
        tool.FilletBeforeCutRequested += (_, r) => request = r;

        tool.Execute(new AvaloniaCadEditor.Domain.Entities.Sketch());

        code.Should().BeNull();
        request.Should().NotBeNull();
        request!.Value.CutVar.Should().Be("furos", "o chanfro vai antes do primeiro furo, não só do último");
        request.Value.BaseVar.Should().Be("corpo");
        request.Value.FilletCode.Should().Contain("baseFeature: corpo").And.Contain("ApplyToAllEdges = true");
        request.Value.FallbackCode.Should().Contain("baseFeature: mais_furos");
    }

    [Fact]
    public void FilletTool_TodasAsArestasDeUniao_GeraCodigoComoAntes()
    {
        var uniao = BooleanFeature.CreateUnion("uniao", new BoxFeature(Point3D.Origin, 2, 2, 2), new BoxFeature(new Point3D(2, 0, 0), 2, 2, 2));
        var selection = new SelectionService();
        selection.SelectFeature(uniao);
        var tool = new FilletTool(selection, FilletMode.Bevel);

        string? code = null;
        bool requested = false;
        tool.CodeGenerated += (_, c) => code = c;
        tool.FilletBeforeCutRequested += (_, _) => requested = true;

        tool.Execute(new AvaloniaCadEditor.Domain.Entities.Sketch());

        requested.Should().BeFalse();
        code.Should().Contain("baseFeature: uniao");
    }
}
