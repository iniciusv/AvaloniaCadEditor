using System;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Espelhar pela viewport reescreve o código como mover/girar: <c>x.Transformed(Placement.MirrorX(...))</c>
/// para sólidos soltos e de peça, <c>at: ....Then(Placement.MirrorX(...))</c> para instâncias. Espelhar de
/// novo pelo mesmo plano desfaz, em vez de empilhar dois espelhos.
/// </summary>
public class MirrorEditingTests
{
    // ---------------------------------------------------------------- Código do espelho

    [Theory]
    [InlineData(0, 0, 0, 1, 0, 0, "Placement.MirrorX()")]
    [InlineData(50, 7, 3, 1, 0, 0, "Placement.MirrorX(atX: 50)")]
    [InlineData(50, 7, 3, -1, 0, 0, "Placement.MirrorX(atX: 50)")]
    [InlineData(0, -2.5, 0, 0, 1, 0, "Placement.MirrorY(atY: -2.5)")]
    [InlineData(0, 0, 10, 0, 0, 2, "Placement.MirrorZ(atZ: 10)")]
    [InlineData(1, 2, 3, 1, 1, 0, "Placement.Mirror(new Point3D(1, 2, 3), new Vector3D(1, 1, 0))")]
    public void MirrorCall_UsaOAtalhoDoEixoQuandoPossivel(double px, double py, double pz, double nx, double ny, double nz, string expected)
    {
        PartSourceEditor.MirrorCall(new Point3D(px, py, pz), new Vector3D(nx, ny, nz)).Should().Be(expected);
    }

    // ---------------------------------------------------------------- Feature solta

    [Theory]
    // Ainda não é Transformed: envolve.
    [InlineData("AddFeature(prism);",
        "AddFeature(prism.Transformed(Placement.MirrorX(atX: 50)));")]
    // Outro placement: encadeia .Then.
    [InlineData("AddFeature(prism.Transformed(Placement.Translate(new Vector3D(5, 0, 0))));",
        "AddFeature(prism.Transformed(Placement.Translate(new Vector3D(5, 0, 0)).Then(Placement.MirrorX(atX: 50))));")]
    // O mesmo espelho sozinho: some o Transformed inteiro.
    [InlineData("AddFeature(prism.Transformed(Placement.MirrorX(50)));",
        "AddFeature(prism);")]
    // O mesmo espelho no fim de um .Then: some só o .Then.
    [InlineData("AddFeature(prism.Transformed(Placement.Translate(new Vector3D(5, 0, 0)).Then(Placement.MirrorX(atX: 50.0))));",
        "AddFeature(prism.Transformed(Placement.Translate(new Vector3D(5, 0, 0))));")]
    // Outro plano: não cancela.
    [InlineData("AddFeature(prism.Transformed(Placement.MirrorX(atX: 10)));",
        "AddFeature(prism.Transformed(Placement.MirrorX(atX: 10).Then(Placement.MirrorX(atX: 50))));")]
    public void MirrorFeature_EnvolveEncadeiaOuDesfaz(string code, string expected)
    {
        PartSourceEditor.MirrorFeature(code, 1, new Point3D(50, 0, 0), Vector3D.UnitX).Should().Be(expected);
    }

    [Fact]
    public void MirrorFeature_ComNomeDeCopia_NaoApagaOTransformed()
    {
        // Transformed(P, name: ...) é uma cópia nomeada: tirar o Transformed mudaria o nome do sólido.
        const string code = "AddFeature(prism.Transformed(Placement.MirrorX(), name: \"prism_copia1\"));";

        string result = PartSourceEditor.MirrorFeature(code, 1, Point3D.Origin, Vector3D.UnitX);

        result.Should().Be("AddFeature(prism.Transformed(Placement.MirrorX().Then(Placement.MirrorX()), name: \"prism_copia1\"));");
    }

    [Fact]
    public void MirrorFeature_UsadaPorBooleana_EspelhaADeclaracao()
    {
        string code =
            "var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4);\n" +
            "AddFeature(tool);\n" +
            "var cut = BooleanFeature.CreateDifference(\"cut\", box, tool);\n";

        string result = PartSourceEditor.MirrorFeature(code, 2, Point3D.Origin, Vector3D.UnitX);

        result.Should().Contain("var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4).Transformed(Placement.MirrorX());");
        result.Should().Contain("AddFeature(tool);");
    }

    [Fact]
    public async Task MirrorFeature_CodigoGeradoExecutaEEspelhaOSolido()
    {
        const string code = """
            var caixa = new BoxFeature(new Point3D(10, 0, 0), 5, 5, 5) { Name = "caixa" };
            AddFeature(caixa);
            """;

        string mirrored = PartSourceEditor.MirrorFeature(code, 2, Point3D.Origin, Vector3D.UnitX);
        var result = await new RoslynSceneEvaluator().EvaluateAsync(new[] { new ScriptDocument(ScriptDocument.DefaultSceneFile, mirrored) });

        result.Success.Should().BeTrue(result.ErrorMessage);
        var caixa = result.Features.Single(f => f.Name == "caixa").Should().BeOfType<TransformedFeature>().Subject;
        caixa.Placement.IsMirrored.Should().BeTrue();
        caixa.GetVertices().Select(v => v.X).Should().OnlyContain(x => x >= -15 - 1e-9 && x <= -10 + 1e-9);
    }

    // ---------------------------------------------------------------- Instância

    [Theory]
    [InlineData("Add(new Chapa(80));",
        "Add(new Chapa(80), at: Placement.MirrorX());")]
    [InlineData("Add(new Chapa(80), at: Placement.At(100, 0, 0));",
        "Add(new Chapa(80), at: Placement.At(100, 0, 0).Then(Placement.MirrorX()));")]
    [InlineData("Add(new Chapa(80), at: Placement.At(100, 0, 0).Then(Placement.MirrorX()));",
        "Add(new Chapa(80), at: Placement.At(100, 0, 0));")]
    [InlineData("Add(new Chapa(80), at: Placement.MirrorX(), name: \"b\");",
        "Add(new Chapa(80), name: \"b\");")]
    // at: posicional seguido de name posicional: tirar o argumento deslocaria o nome; vira null.
    [InlineData("Add(new Chapa(80), Placement.MirrorX(), \"b\");",
        "Add(new Chapa(80), null, \"b\");")]
    public void MirrorInstance_AcrescentaEncadeiaOuDesfaz(string code, string expected)
    {
        PartSourceEditor.MirrorInstance(code, 1, Point3D.Origin, Vector3D.UnitX).Should().Be(expected);
    }

    // ---------------------------------------------------------------- Sólido de peça

    private const string ChapaComFuro = """
        public sealed record ChapaComFuro() : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(new Point3D(-50, 0, 0), 100, 20, 5) { Name = "corpo" };
                var furo = new CylinderFeature(new Point3D(30, 10, -1), Vector3D.UnitZ, 3, 7) { Name = "furo", IsTransientTool = true };
                return BooleanFeature.CreateDifference("chapa", corpo, furo);
            }
        }
        """;

    [Fact]
    public void MirrorPartSolid_EnvolveADeclaracao_EEspelharDeNovoDesfaz()
    {
        string once = PartSourceEditor.MirrorPartSolid(ChapaComFuro, "ChapaComFuro", "furo", Point3D.Origin, Vector3D.UnitX);
        string twice = PartSourceEditor.MirrorPartSolid(once, "ChapaComFuro", "furo", Point3D.Origin, Vector3D.UnitX);

        once.Should().Contain("{ Name = \"furo\", IsTransientTool = true }.Transformed(Placement.MirrorX());");
        twice.Should().Be(ChapaComFuro);
    }
}
