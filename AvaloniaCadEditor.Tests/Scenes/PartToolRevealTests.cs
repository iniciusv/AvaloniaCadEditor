using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Duplo-clique numa instÃ¢ncia de peÃ§a mostra os sÃ³lidos-ferramenta (vazios) da definiÃ§Ã£o no lugar da
/// instÃ¢ncia; clicar num deles leva Ã  declaraÃ§Ã£o no cÃ³digo da peÃ§a.
/// </summary>
public class PartToolRevealTests
{
    private const string BlocoRecord = """
        public sealed record Bloco(double Largura = 20) : Part
        {
            protected override IFeature Build()
            {
                const double diametro = 3;
                var corpo = new BoxFeature(Point3D.Origin, Largura, 20, 10) { Name = "corpo" };
                var furo_1 = new CylinderFeature(new Point3D(5, 5, 0), Vector3D.UnitZ, diametro / 2, 10) { Name = "furo_1", IsTransientTool = true };
                var x = new CylinderFeature(new Point3D(15, 5, 0), Vector3D.UnitZ, diametro / 2, 10) { Name = "furo_literal", IsTransientTool = true };
                return BooleanFeature.CreateDifference("bloco", corpo, furo_1, x);
            }
        }
        """;

    private static ScriptDocument[] Documents() => new[]
    {
        new ScriptDocument(ScriptDocument.DefaultSceneFile, "var b = Add(new Bloco());"),
        new ScriptDocument("Pecas/Bloco.cs", BlocoRecord)
    };

    [Fact]
    public void FindToolDeclaration_AchaAVariavelComOMesmoNome()
    {
        var location = PartSourceEditor.FindToolDeclaration(Documents(), "Bloco", "furo_1")!;

        location.Should().NotBeNull();
        location.FilePath.Should().Be("Pecas/Bloco.cs");
        location.Line.Should().Be(7);
        BlocoRecord.Substring(location.Start, location.Length).Should().Be("furo_1");
        location.LineText.Should().StartWith("var furo_1 = new CylinderFeature");
    }

    [Fact]
    public void FindToolDeclaration_SemVariavel_AchaOLiteralDoNome()
    {
        var location = PartSourceEditor.FindToolDeclaration(Documents(), "Bloco", "furo_literal")!;

        location.Should().NotBeNull();
        location.Line.Should().Be(8);
        BlocoRecord.Substring(location.Start, location.Length).Should().Be("\"furo_literal\"");
    }

    [Theory]
    [InlineData("Bloco", "nao_existe")]
    [InlineData("OutraPeca", "furo_1")]
    public void FindToolDeclaration_NomeOuPecaInexistente_DevolveNull(string type, string tool)
    {
        PartSourceEditor.FindToolDeclaration(Documents(), type, tool).Should().BeNull();
    }

    private sealed record PecaDeTeste : Part
    {
        protected override IFeature Build() => throw new System.NotSupportedException();
    }

    [Fact]
    public void GrupoDeInstancia_MostraFerramentasNoLugarDaInstancia_EORaycastAsAcerta()
    {
        var corpo = new BoxFeature(Point3D.Origin, 20, 20, 10) { Name = "corpo" };
        var furo = new CylinderFeature(new Point3D(10, 10, 0), Vector3D.UnitZ, 3, 10) { Name = "furo", IsTransientTool = true };
        var definicao = BooleanFeature.CreateDifference("bloco", corpo, furo);
        var instancia = new PartInstance<PecaDeTeste>(new PecaDeTeste(), definicao, Placement.At(100, 0, 0)) { Name = "bloco_1" };

        var vm = TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new OpenCascadeGeometryKernel());
        vm.Sketch.AddFeature(instancia);

        vm.ActiveBooleanGroupName = "bloco_1";

        var ferramenta = vm.ActiveToolFeatures.Should().ContainSingle().Subject;
        ferramenta.Should().BeOfType<TransformedFeature>().Which.Source.Should().BeSameAs(furo);
        vm.ActiveToolFeatures.Should().ContainSingle().Which.Should().BeSameAs(ferramenta, "o envoltÃ³rio Ã© reaproveitado (Id estÃ¡vel)");

        // Raio de cima no centro do furo, jÃ¡ deslocado pela instÃ¢ncia (x = 110): acerta a ferramenta.
        var ray = new Ray(new System.Numerics.Vector3(110, 10, 100), new System.Numerics.Vector3(0, 0, -1));
        var hit = vm.Raycast3D(ray);

        hit.Should().NotBeNull();
        hit!.Value.FeatureId.Should().Be(ferramenta.Id);
    }
}
