using System.Linq;
using System.Numerics;
using Point = Avalonia.Point;
using Size = Avalonia.Size;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.UI.Drafting;
using AvaloniaCadEditor.UI.ReferenceGuides;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Drafting;

public class DraftingViewModelTests
{
    private sealed record PecaDeTeste : Part
    {
        protected override IFeature Build() => throw new System.NotSupportedException();
    }

    private static readonly Size Viewport = new(800, 600);

    private static (CadViewModel Cad, DraftingViewModel Drafting, CameraState Camera) Isolado(double rotacaoZ = 0)
    {
        var cad = TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new OpenCascadeGeometryKernel());
        var drafting = new DraftingViewModel(cad);
        cad.Drafting = drafting;

        var corpo = new BoxFeature(new Point3D(-20, 10, 0), 40, 16, 17) { Name = "corpo" };
        var furo_a = new CylinderFeature(new Point3D(-10, 26, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_a", IsTransientTool = true };
        var furo_b = new CylinderFeature(new Point3D(10, 26, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_b", IsTransientTool = true };
        var definicao = BooleanFeature.CreateDifference("bloco", corpo, furo_a, furo_b);
        var peca = new PartInstance<PecaDeTeste>(new PecaDeTeste(), definicao, Placement.At(100, 50, 26, rotationZDegrees: rotacaoZ)) { Name = "peca" };
        cad.Sketch.AddFeature(peca);
        cad.Sketch.AddFeature(new BoxFeature(new Point3D(500, 0, 0), 10, 10, 10) { Name = "outra" });
        peca.IsSelected = true;
        cad.OnKeyDown("H", false);

        drafting.IsVisible = true;
        drafting.SetView(StandardView.Front);

        // Câmera como o botão Front deixa: ortho, olhando a frente DA PEÇA, centrada nela.
        var camera = new CameraState { IsOrthographic = true, AspectRatio = (float)(Viewport.Width / Viewport.Height), Distance = 60 };
        camera.SetStandardView(StandardView.Front, (float)cad.DisplayFrame!.AzimuthOffset);
        var centro = cad.DisplayFrame.ToWorld(new Point3D(20, 8, 8.5));
        camera.Target = new Vector3((float)centro.X, (float)centro.Y, (float)centro.Z);
        return (cad, drafting, camera);
    }

    private static Point Tela(DraftingViewModel drafting, CameraState camera, string ancora) =>
        ScreenProjection.WorldToScreen(camera, drafting.ToWorld(drafting.Sheet!.FindAnchor(ancora)!.Point), Viewport)!.Value;

    [Theory]
    [InlineData(0)]
    [InlineData(180)]
    public void PecaIsolada_NaVistaFront_TemFolhaECotasQueBatemComACamera(double rotacaoZ)
    {
        var (_, drafting, camera) = Isolado(rotacaoZ);

        drafting.Sheet.Should().NotBeNull();
        drafting.MatchesCamera(camera).Should().BeTrue();
        drafting.Dimensions.Should().Contain(d => d.Text == "2× Ø3,4");

        camera.SetStandardView(StandardView.Top);
        drafting.MatchesCamera(camera).Should().BeFalse("girou a câmera para outra vista: as cotas somem");
    }

    [Fact]
    public void ModoCotar_DoisPontosEPosicao_CriaCotaManualHorizontal()
    {
        var (_, drafting, camera) = Isolado();
        drafting.IsPicking = true;

        drafting.HandleClick(Tela(drafting, camera, "furo_a.centro"), camera, Viewport).Should().BeTrue();
        drafting.HandleClick(Tela(drafting, camera, "furo_b.centro"), camera, Viewport).Should().BeTrue();
        drafting.PendingSecond.Should().NotBeNull();

        // Terceiro clique: abaixo dos dois pontos, no meio deles → cota horizontal.
        var a = Tela(drafting, camera, "furo_a.centro");
        var b = Tela(drafting, camera, "furo_b.centro");
        drafting.HandleClick(new Point((a.X + b.X) / 2, a.Y + 80), camera, Viewport);

        var manual = drafting.Dimensions.Should().ContainSingle(d => d.IsManual).Subject;
        manual.Kind.Should().Be(DimensionKind.Horizontal);
        manual.Value.Should().BeApproximately(20, 1e-6);
        drafting.PendingFirst.Should().BeNull();
    }

    [Fact]
    public void ModoCotar_MesmoCentroDuasVezes_CriaDiametro()
    {
        var (_, drafting, camera) = Isolado();
        drafting.IsPicking = true;

        var centro = Tela(drafting, camera, "furo_a.centro");
        drafting.HandleClick(centro, camera, Viewport);
        drafting.HandleClick(centro, camera, Viewport);

        drafting.Dimensions.Should().ContainSingle(d => d.IsManual && d.Kind == DimensionKind.Diameter)
            .Which.Text.Should().Be("Ø3,4");
    }

    [Fact]
    public void ModoCotar_CliqueNoTexto_EscondeAutomaticaERestaurarTrazDeVolta()
    {
        var (_, drafting, _) = Isolado();
        drafting.IsPicking = true;
        var diametro = drafting.Dimensions.Single(d => d.Text == "2× Ø3,4");

        drafting.OnLabelClicked(diametro);
        drafting.Dimensions.Should().NotContain(d => d.Key == diametro.Key);

        drafting.RestoreCommand.Execute(null);
        drafting.Dimensions.Should().Contain(d => d.Key == diametro.Key);
    }

    [Fact]
    public void CotaManual_SobreviveAReexecucao_ESomeAoSairDoIsolamento()
    {
        var (cad, drafting, camera) = Isolado();
        drafting.IsPicking = true;
        var centro = Tela(drafting, camera, "furo_a.centro");
        drafting.HandleClick(centro, camera, Viewport);
        drafting.HandleClick(centro, camera, Viewport);

        // Reexecução: features novas com os mesmos nomes.
        var features = cad.Sketch.Features.ToList();
        cad.Sketch.ClearFeatures();
        foreach (var f in features) { f.IsVisible = true; cad.Sketch.AddFeature(f); }
        cad.ReapplyIsolation();

        drafting.Dimensions.Should().Contain(d => d.IsManual);

        cad.OnKeyDown("H", false);
        drafting.Sheet.Should().BeNull();
        cad.Sketch.Features.First(f => f.Name == "peca").IsSelected = true;
        cad.OnKeyDown("H", false);
        drafting.Dimensions.Should().NotContain(d => d.IsManual, "cotas manuais são só da sessão de isolamento");
    }
}
