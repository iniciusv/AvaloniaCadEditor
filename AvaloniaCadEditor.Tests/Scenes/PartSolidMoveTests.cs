using System;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Mover um sólido de dentro de uma peça (grupo aberto com duplo-clique na instância): o deslocamento
/// vai para a declaração do sólido no <c>Build()</c>, já que ali não existe <c>AddFeature</c> para reescrever.
/// </summary>
public class PartSolidMoveTests
{
    private const string BoolOpRecord = """
        public sealed record BoolOp4f04() : Part
        {
            protected override IFeature Build()
            {
                var box_bf48 = new BoxFeature(baseCorner: new Point3D(-2, 27, 0), width: 23, length: 12, height: 10)
                {
                    Name = "box_bf48"
                };
                var cylinder_c61e = new CylinderFeature(globalBaseCenter: new Point3D(29, 22, 0), globalDirection: new Vector3D(0, 0, 1), radius: 14.31782, height: 27.5)
                {
                    Name = "cylinder_c61e"
                };
                var boolOp_4f04 = new BooleanFeature(name: "boolOp_4f04", operation: BooleanOperationType.Subtract, baseFeature: box_bf48, toolFeatures: new IFeature[] { cylinder_c61e });
                return boolOp_4f04;
            }
        }
        """;

    [Fact]
    public void TranslatePartSolid_EnvolveADeclaracaoEmTransformed()
    {
        string updated = PartSourceEditor.TranslatePartSolid(BoolOpRecord, "BoolOp4f04", "cylinder_c61e", 10, 0, -2.5);

        int start = updated.IndexOf("var cylinder_c61e", StringComparison.Ordinal);
        int end = updated.IndexOf("var boolOp_4f04", StringComparison.Ordinal);
        updated[start..end].Should().Contain("Name = \"cylinder_c61e\"")
            .And.Contain("}.Transformed(Placement.Translate(new Vector3D(10, 0, -2.5)));");
        updated.Split(".Transformed(").Length.Should().Be(2, "só o cilindro muda; a caixa e a booleana ficam como estavam");
        updated.Should().Contain("toolFeatures: new IFeature[] { cylinder_c61e }", "a booleana continua usando a variável");
    }

    [Fact]
    public void TranslatePartSolid_SegundoMovimento_SomaNoMesmoTranslate()
    {
        string once = PartSourceEditor.TranslatePartSolid(BoolOpRecord, "BoolOp4f04", "cylinder_c61e", 10, 0, 0);
        string twice = PartSourceEditor.TranslatePartSolid(once, "BoolOp4f04", "cylinder_c61e", 5, 3, 0);

        twice.Should().Contain(".Transformed(Placement.Translate(new Vector3D(15, 3, 0)))");
        twice.Split(".Transformed(").Length.Should().Be(2, "o segundo movimento não empilha outro Transformed");
    }

    [Fact]
    public void TranslatePartSolid_VariavelComOutroNome_AchaPeloLiteral()
    {
        const string record = """
            public sealed record Bloco() : Part
            {
                protected override IFeature Build()
                {
                    var corpo = new BoxFeature(Point3D.Origin, 20, 20, 10) { Name = "corpo" };
                    var x = new CylinderFeature(new Point3D(15, 5, 0), Vector3D.UnitZ, 1.5, 10) { Name = "furo_literal" };
                    return BooleanFeature.CreateDifference("bloco", corpo, x);
                }
            }
            """;

        string updated = PartSourceEditor.TranslatePartSolid(record, "Bloco", "furo_literal", 0, 4, 0);

        updated.Should().Contain("""{ Name = "furo_literal" }.Transformed(Placement.Translate(new Vector3D(0, 4, 0)));""");
        updated.Should().Contain("""var corpo = new BoxFeature(Point3D.Origin, 20, 20, 10) { Name = "corpo" };""");
    }

    [Fact]
    public void TranslatePartSolid_SolidoInlineEmOutraExpressao_RecusaEmVezDeMoverAPecaInteira()
    {
        const string record = """
            public sealed record Bloco() : Part
            {
                protected override IFeature Build()
                {
                    var corpo = new BoxFeature(Point3D.Origin, 20, 20, 10) { Name = "corpo" };
                    var bloco = BooleanFeature.CreateDifference("bloco", corpo, new CylinderFeature(new Point3D(5, 5, 0), Vector3D.UnitZ, 1.5, 10) { Name = "furo" });
                    return bloco;
                }
            }
            """;

        var act = () => PartSourceEditor.TranslatePartSolid(record, "Bloco", "furo", 1, 0, 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*furo*");
    }

    [Fact]
    public void TranslatePartSolid_VariavelUsadaComMembro_Recusa()
    {
        const string record = """
            public sealed record Bloco() : Part
            {
                protected override IFeature Build()
                {
                    var corpo = new BoxFeature(Point3D.Origin, 20, 20, 10) { Name = "corpo" };
                    var furo = new CylinderFeature(new Point3D(5, 5, 0), Vector3D.UnitZ, 1.5, 10) { Name = "furo" };
                    var furo_2 = new CylinderFeature(new Point3D(15, 5, 0), Vector3D.UnitZ, furo.Radius, 10) { Name = "furo_2" };
                    return BooleanFeature.CreateDifference("bloco", corpo, furo, furo_2);
                }
            }
            """;

        var act = () => PartSourceEditor.TranslatePartSolid(record, "Bloco", "furo", 1, 0, 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*furo*");
    }

    [Fact]
    public async Task TranslatePartSolid_CenaExecutada_FerramentaDaPecaMudaDeLugarEMantemONome()
    {
        var documents = new[]
        {
            new ScriptDocument(ScriptDocument.DefaultSceneFile, """var boolOp_4f04 = Add(new BoolOp4f04(), name: "boolOp_4f04");"""),
            new ScriptDocument("Pecas/BoolOp4f04.cs", PartSourceEditor.TranslatePartSolid(BoolOpRecord, "BoolOp4f04", "cylinder_c61e", 10, 0, 0))
        };

        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var instance = result.Features.OfType<PartInstance>().Single();
        var tool = ((BooleanFeature)instance.Definition).ToolFeatures.Should().ContainSingle().Which
            .Should().BeOfType<TransformedFeature>().Subject;
        tool.Name.Should().Be("cylinder_c61e", "o sólido segue rastreável pelo nome depois de movido");
        tool.Placement.Translation.Should().Be(new Vector3D(10, 0, 0));
    }

    [Fact]
    public void RotatePartSolid_EnvolveADeclaracao_ESegundoGiroNoMesmoEixoSomaOAngulo()
    {
        var pivot = new Point3D(29, 22, 0);

        string once = PartSourceEditor.RotatePartSolid(BoolOpRecord, "BoolOp4f04", "cylinder_c61e", pivot, Vector3D.UnitZ, 30);
        string twice = PartSourceEditor.RotatePartSolid(once, "BoolOp4f04", "cylinder_c61e", pivot, Vector3D.UnitZ, 15);

        once.Should().Contain("}.Transformed(Placement.RotationAbout(new Point3D(29, 22, 0), Vector3D.UnitZ, 30));");
        twice.Should().Contain("}.Transformed(Placement.RotationAbout(new Point3D(29, 22, 0), Vector3D.UnitZ, 45));");
        twice.Split(".Transformed(").Length.Should().Be(2);
    }

    [Fact]
    public void RotatePartSolid_DepoisDeMover_EncadeiaOGiroDepoisDoDeslocamento()
    {
        string moved = PartSourceEditor.TranslatePartSolid(BoolOpRecord, "BoolOp4f04", "cylinder_c61e", 10, 0, 0);

        string rotated = PartSourceEditor.RotatePartSolid(moved, "BoolOp4f04", "cylinder_c61e", new Point3D(39, 22, 0), Vector3D.UnitZ, 90);

        rotated.Should().Contain(
            ".Transformed(Placement.Translate(new Vector3D(10, 0, 0)).Then(Placement.RotationAbout(new Point3D(39, 22, 0), Vector3D.UnitZ, 90)));");
    }

    [Fact]
    public async Task RotatePartSolid_CenaExecutada_FerramentaDaPecaGiraEMantemONome()
    {
        var documents = new[]
        {
            new ScriptDocument(ScriptDocument.DefaultSceneFile, """var boolOp_4f04 = Add(new BoolOp4f04(), name: "boolOp_4f04");"""),
            new ScriptDocument("Pecas/BoolOp4f04.cs",
                PartSourceEditor.RotatePartSolid(BoolOpRecord, "BoolOp4f04", "cylinder_c61e", new Point3D(0, 0, 0), Vector3D.UnitY, 90))
        };

        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var tool = ((BooleanFeature)result.Features.OfType<PartInstance>().Single().Definition).ToolFeatures
            .Should().ContainSingle().Which.Should().BeOfType<TransformedFeature>().Subject;
        tool.Name.Should().Be("cylinder_c61e");
        tool.Placement.HasRotation.Should().BeTrue();
    }

    /// <summary>
    /// A premissa do CadViewModel: girar o sólido em torno de (p, a) no mundo equivale a girá-lo, dentro
    /// da peça, em torno de P⁻¹(p) e P⁻¹(a) — mesmo com a instância deslocada e girada.
    /// </summary>
    [Fact]
    public void GiroNoMundo_EquivaleAGiroNoReferencialDaPecaComPivoEEixoConvertidos()
    {
        var instancia = Placement.At(100, 50, 0, rotationZDegrees: 90);
        var pivoMundo = new Point3D(110, 60, 5);
        var eixoMundo = Vector3D.UnitX;

        var paraLocal = instancia.Inverse();
        var giroLocal = Placement.RotationAbout(paraLocal.Apply(pivoMundo), paraLocal.ApplyVector(eixoMundo), 37);
        var giroMundo = Placement.RotationAbout(pivoMundo, eixoMundo, 37);

        var ponto = new Point3D(3, -7, 11);
        var esperado = giroMundo.Apply(instancia.Apply(ponto));
        var obtido = instancia.Apply(giroLocal.Apply(ponto));

        obtido.X.Should().BeApproximately(esperado.X, 1e-9);
        obtido.Y.Should().BeApproximately(esperado.Y, 1e-9);
        obtido.Z.Should().BeApproximately(esperado.Z, 1e-9);
    }

    [Fact]
    public void RotateTool_ComSelectionOverride_GiraOSolidoDadoENaoASelecaoDoSketch()
    {
        var sketch = new Sketch();
        sketch.AddFeature(new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "instancia", IsSelected = true });
        var solidoDaPeca = new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = "furo" };

        var rotateTool = new RotateTool { SelectionOverride = () => new IFeature[] { solidoDaPeca } };
        IFeature[]? rotated = null;
        rotateTool.FeaturesRotated += (_, _, _, features) => rotated = features.ToArray();

        rotateTool.OnMouseDown(sketch, new Point3D(0, 0));
        rotateTool.DraggedFeatures.Should().ContainSingle().Which.Should().BeSameAs(solidoDaPeca);
        rotateTool.OnMouseDown(sketch, new Point3D(10, 0));
        rotateTool.OnMouseDown(sketch, new Point3D(0, 10));

        rotated.Should().ContainSingle().Which.Should().BeSameAs(solidoDaPeca);
    }

    [Fact]
    public void MoveTool_ComSelectionOverride_ArrastaOSolidoDadoENaoASelecaoDoSketch()
    {
        var sketch = new Sketch();
        var instanciaSelecionada = new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "instancia", IsSelected = true };
        sketch.AddFeature(instanciaSelecionada);
        var solidoDaPeca = new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = "furo" };

        var moveTool = new MoveTool { SelectionOverride = () => new IFeature[] { solidoDaPeca } };
        IFeature[]? moved = null;
        moveTool.FeaturesMoved += (_, features) => moved = features.ToArray();

        moveTool.OnMouseDown(sketch, new Point3D(0, 0));
        moveTool.DraggedFeatures.Should().ContainSingle().Which.Should().BeSameAs(solidoDaPeca);
        moveTool.OnMouseDown(sketch, new Point3D(5, 0));

        moved.Should().ContainSingle().Which.Should().BeSameAs(solidoDaPeca);
    }

    [Fact]
    public void MoveTool_SelectionOverrideDevolvendoNull_UsaASelecaoDoSketch()
    {
        var sketch = new Sketch();
        var selecionada = new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "caixa", IsSelected = true };
        sketch.AddFeature(selecionada);

        var moveTool = new MoveTool { SelectionOverride = () => null };
        IFeature[]? moved = null;
        moveTool.FeaturesMoved += (_, features) => moved = features.ToArray();

        moveTool.OnMouseDown(sketch, new Point3D(0, 0));
        moveTool.OnMouseDown(sketch, new Point3D(5, 0));

        moved.Should().ContainSingle().Which.Should().BeSameAs(selecionada);
    }
}
