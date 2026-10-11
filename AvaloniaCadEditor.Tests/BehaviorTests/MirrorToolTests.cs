using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Espelhar pelo menu do botão direito: "pelo centro da seleção" em X/Y/Z, ou a <see cref="MirrorTool"/>,
/// em que o plano segue o cursor (com preview e guias) e um clique confirma.
/// </summary>
public class MirrorToolTests
{
    private static readonly BoxFeature Caixa = new(new Point3D(10, 0, 0), 5, 5, 5) { Name = "caixa" };

    // ---------------------------------------------------------------- MirrorTool

    [Fact]
    public void PlanoSegueOCursor_EOPreviewEspelhaPorEle()
    {
        var tool = new MirrorTool(new[] { Caixa }, copy: false);

        tool.OnMouseMove(new Sketch(), new Point3D(50, 0, 0));

        tool.DraggedFeatures.Should().ContainSingle();
        var mirrored = Vector3.Transform(new Vector3(60, 3, 4), tool.PreviewTransform);
        mirrored.X.Should().BeApproximately(40, 1e-4f);
        mirrored.Y.Should().BeApproximately(3, 1e-4f);
        mirrored.Z.Should().BeApproximately(4, 1e-4f);
    }

    [Fact]
    public void Clique_ConfirmaComOPlanoDoCursor()
    {
        var tool = new MirrorTool(new[] { Caixa }, copy: false);
        (Point3D Point, Vector3D Normal)? mirrored = null;
        tool.FeaturesMirrored += (p, n, _) => mirrored = (p, n);

        tool.OnMouseMove(new Sketch(), new Point3D(50, 7, 0));
        tool.OnMouseDown(new Sketch(), new Point3D(50, 7, 0));

        mirrored.Should().Be((new Point3D(50, 0, 0), Vector3D.UnitX), "só a coordenada do eixo define o plano");
    }

    [Fact]
    public void Setas_TrocamOEixo_EValorDigitadoPosicionaOPlano()
    {
        var tool = new MirrorTool(new[] { Caixa }, copy: true);
        (Point3D Point, Vector3D Normal)? copied = null;
        tool.FeaturesCopied += (p, n, _) => copied = (p, n);

        tool.OnKeyDown(new Sketch(), CadKey.ArrowUp);
        tool.OnValueEntered(new Sketch(), 7);

        copied.Should().Be((new Point3D(0, 0, 7), Vector3D.UnitZ));
    }

    [Fact]
    public void Ctrl_AlternaEntreCopiarEMover()
    {
        var tool = new MirrorTool(new[] { Caixa }, copy: true);
        bool moved = false;
        tool.FeaturesMirrored += (_, _, _) => moved = true;

        tool.OnKeyDown(new Sketch(), CadKey.Control);
        tool.OnMouseDown(new Sketch(), new Point3D(0, 0, 0));

        tool.IsCopyMode.Should().BeFalse();
        moved.Should().BeTrue();
    }

    [Fact]
    public void Guias_DesenhamOPlanoEALigacaoAteOCentroEspelhado()
    {
        var tool = new MirrorTool(new[] { Caixa }, copy: false);
        tool.OnMouseMove(new Sketch(), new Point3D(0, 0, 0));

        var guides = tool.GetGuideSegments().ToList();

        // Todo o retângulo do plano fica em X = 0 (plano YZ).
        guides.Where(s => s.R == 1f && s.G == 0.3f)
            .Should().Contain(s => s.From.X == 0 && s.To.X == 0);
        // Amarelo: centro da seleção (12.5, 2.5, 2.5) → plano → centro espelhado (-12.5, 2.5, 2.5).
        var yellow = guides.Where(s => s.R == 1f && s.G == 1f).ToList();
        yellow.Should().HaveCount(2);
        yellow[0].From.Should().Be(new Point3D(12.5, 2.5, 2.5));
        yellow[1].To.Should().Be(new Point3D(-12.5, 2.5, 2.5));
    }

    [Fact]
    public void SemMouse_NaoHaPreviewNemGuias()
    {
        var tool = new MirrorTool(new[] { Caixa }, copy: false);

        tool.DraggedFeatures.Should().BeEmpty();
        tool.GetGuideSegments().Should().BeEmpty();
        tool.PreviewTransform.Should().Be(Matrix4x4.Identity);
    }

    // ---------------------------------------------------------------- Pelo centro da seleção (CadViewModel)

    private const string ChapaRecord = """
        public sealed record Chapa(double Largura = 80, double Comprimento = 40, double Espessura = 5) : Part
        {
            protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura);
        }
        """;

    private static async Task<AvaloniaCadEditor.UI.ViewModels.CadViewModel> ViewModelCom(string code, string selected)
    {
        var vm = TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new PureMathGeometryKernel());
        vm.ScriptEditor.GeneratedCSharpCode = code;
        await vm.ScriptEditor.RunCodeAsync();
        vm.SelectionService.SelectById(vm.Sketch, vm.Sketch.Features.Single(f => f.Name == selected).Id);
        return vm;
    }

    [Fact]
    public async Task EspelharPeloCentro_SolidoSolto_GanhaTransformedComOPlanoDoCentro()
    {
        var vm = await ViewModelCom(
            "var caixa = new BoxFeature(new Point3D(10, 0, 0), 5, 5, 5) { Name = \"caixa\" };\nAddFeature(caixa);\n", "caixa");

        vm.CanMirrorSelection.Should().BeTrue();
        vm.MirrorSelectionAtCenter(Vector3D.UnitX, copy: false);

        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("AddFeature(caixa.Transformed(Placement.MirrorX(atX: 12.5)));");
    }

    [Fact]
    public async Task EspelharCopiaPeloCentro_Instancia_DuplicaOAddComOEspelho()
    {
        var vm = await ViewModelCom(
            "var chapa = Add(new Chapa(), at: Placement.At(100, 0, 0), name: \"chapa\");\n\n" + ChapaRecord, "chapa");

        vm.MirrorSelectionAtCenter(Vector3D.UnitY, copy: true);

        vm.ScriptEditor.GeneratedCSharpCode.Should()
            .Contain("var chapa = Add(new Chapa(), at: Placement.At(100, 0, 0), name: \"chapa\");")
            .And.Contain("Add(new Chapa(), at: Placement.At(100, 0, 0).Then(Placement.MirrorY(atY: 20)));");
    }

    [Fact]
    public async Task EscolherPlano_AbreAFerramentaSemPerderASelecao()
    {
        var vm = await ViewModelCom(
            "var caixa = new BoxFeature(new Point3D(10, 0, 0), 5, 5, 5) { Name = \"caixa\" };\nAddFeature(caixa);\n", "caixa");

        vm.StartMirrorTool(copy: false);

        vm.CurrentTool.Should().BeOfType<MirrorTool>();
        vm.Sketch.Features.Single(f => f.Name == "caixa").IsSelected.Should().BeTrue();
        vm.CanMirrorSelection.Should().BeFalse("com uma ferramenta em andamento o menu não abre");
    }
}
