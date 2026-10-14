using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Application.Isolation;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Isolation;

public class PartFrameTests
{
    private sealed record PecaDeTeste : Part
    {
        protected override IFeature Build() => throw new System.NotSupportedException();
    }

    // Corpo com coordenadas locais negativas, como o BlocoFixacaoBelt: X de -20 a 20, Y de 10 a 26, Z de 0 a 17.
    private static BoxFeature Corpo() => new(new Point3D(-20, 10, 0), 40, 16, 17) { Name = "corpo" };

    private static float[] WorldPositions(IFeature feature) =>
        feature.GetVertices().SelectMany(p => new[] { (float)p.X, (float)p.Y, (float)p.Z }).ToArray();

    [Fact]
    public void Inverse_DesfazRotacaoETranslacao()
    {
        var placement = Placement.At(12, -7, 26, rotationZDegrees: 180).Then(Placement.Rotation(Vector3D.UnitX, 30));
        var p = new Point3D(3, 4, 5);

        var back = placement.Inverse().Apply(placement.Apply(p));

        back.Should().Be(p);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(180, true)]
    [InlineData(37, true)]
    public void IsRotationAboutZOnly_AceitaRotacaoEmZ(double degrees, bool expected)
    {
        Placement.At(1, 2, 3, rotationZDegrees: degrees).IsRotationAboutZOnly.Should().Be(expected);
    }

    [Fact]
    public void IsRotationAboutZOnly_RecusaRotacaoForaDeZ()
    {
        Placement.Rotation(Vector3D.UnitX, 90).IsRotationAboutZOnly.Should().BeFalse();
    }

    [Fact]
    public void Resolve_InstanciaGirada180EmZ_OrigemNoCantoMinimoLocalEVolumePositivo()
    {
        var instancia = new PartInstance<PecaDeTeste>(new PecaDeTeste(), Corpo(), Placement.At(5, 0, 26, rotationZDegrees: 180))
        {
            Name = "bloco_fixacao_belt"
        };

        var frame = PartFrameResolver.Resolve(instancia, WorldPositions(instancia));

        frame.Should().NotBeNull();
        frame!.OwnerName.Should().Be("bloco_fixacao_belt");
        // O canto mínimo LOCAL (-20, 10, 0) vira a origem do referencial.
        frame.Origin.Should().Be(instancia.ToWorld(new Point3D(-20, 10, 0)));
        // Eixos da peça: girada 180°, o X da peça aponta para -X do mundo.
        frame.XAxis.Should().Be(new Vector3D(-1, 0, 0));
        frame.ZAxis.Should().Be(Vector3D.UnitZ);

        var noReferencial = instancia.GetVertices().Select(frame.ToFrame).ToList();
        noReferencial.Min(p => p.X).Should().BeApproximately(0, 1e-6);
        noReferencial.Min(p => p.Y).Should().BeApproximately(0, 1e-6);
        noReferencial.Min(p => p.Z).Should().BeApproximately(0, 1e-6);
        noReferencial.Max(p => p.X).Should().BeApproximately(40, 1e-6);
        noReferencial.Max(p => p.Y).Should().BeApproximately(16, 1e-6);
        noReferencial.Max(p => p.Z).Should().BeApproximately(17, 1e-6);
    }

    [Fact]
    public void Resolve_InstanciaEspelhadaEmZ_ReferencialSegueOMundoComZParaCima()
    {
        // Como o bloco_ficacao_belt_vao_correia da cena: girado em Z e depois espelhado em Z.
        var placement = Placement.At(5, 0, 26, rotationZDegrees: 90).Then(Placement.MirrorZ()).Then(Placement.Translate(new Vector3D(0, 0, 41)));
        var instancia = new PartInstance<PecaDeTeste>(new PecaDeTeste(), Corpo(), placement) { Name = "espelhada" };

        var frame = PartFrameResolver.Resolve(instancia, WorldPositions(instancia));

        frame.Should().NotBeNull();
        frame!.XAxis.X.Should().BeApproximately(0, 1e-9);
        frame.XAxis.Y.Should().BeApproximately(1, 1e-9);
        frame.ZAxis.Should().Be(Vector3D.UnitZ);

        var noReferencial = instancia.GetVertices().Select(frame.ToFrame).ToList();
        noReferencial.Min(p => p.X).Should().BeApproximately(0, 1e-6);
        noReferencial.Min(p => p.Y).Should().BeApproximately(0, 1e-6);
        noReferencial.Min(p => p.Z).Should().BeApproximately(0, 1e-6);
        noReferencial.Max(p => p.X).Should().BeApproximately(40, 1e-6);
        noReferencial.Max(p => p.Y).Should().BeApproximately(16, 1e-6);
        noReferencial.Max(p => p.Z).Should().BeApproximately(17, 1e-6);
    }

    [Fact]
    public void Resolve_FeatureSolta_SemReferencial()
    {
        var corpo = Corpo();

        PartFrameResolver.Resolve(corpo, WorldPositions(corpo)).Should().BeNull();
    }

    [Fact]
    public void Resolve_RotacaoForaDeZ_SemReferencial()
    {
        var instancia = new PartInstance<PecaDeTeste>(new PecaDeTeste(), Corpo(), Placement.Rotation(Vector3D.UnitX, 90));

        PartFrameResolver.Resolve(instancia, WorldPositions(instancia)).Should().BeNull();
    }

    [Fact]
    public void Isolamento_SobreviveAReexecucao_ERecalculaOReferencial()
    {
        var vm = TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new OpenCascadeGeometryKernel());

        var peca = new PartInstance<PecaDeTeste>(new PecaDeTeste(), Corpo(), Placement.At(100, 0, 0)) { Name = "peca" };
        var outra = new BoxFeature(new Point3D(300, 0, 0), 10, 10, 10) { Name = "outra" };
        vm.Sketch.AddFeature(peca);
        vm.Sketch.AddFeature(outra);
        peca.IsSelected = true;

        vm.OnKeyDown("H", false);

        vm.IsIsolating.Should().BeTrue();
        outra.IsVisible.Should().BeFalse();
        vm.DisplayFrame!.Origin.Should().Be(new Point3D(80, 10, 0));

        // Reexecução: o código recria todas as features (Ids novos); a peça foi movida pelo arrasto.
        var pecaNova = new PartInstance<PecaDeTeste>(new PecaDeTeste(), Corpo(), Placement.At(150, 0, 0)) { Name = "peca" };
        var outraNova = new BoxFeature(new Point3D(300, 0, 0), 10, 10, 10) { Name = "outra" };
        vm.Sketch.ClearFeatures();
        vm.Sketch.AddFeature(pecaNova);
        vm.Sketch.AddFeature(outraNova);
        vm.ReapplyIsolation();

        vm.IsIsolating.Should().BeTrue();
        pecaNova.IsVisible.Should().BeTrue();
        outraNova.IsVisible.Should().BeFalse();
        vm.DisplayFrame!.Origin.Should().Be(new Point3D(130, 10, 0), "o referencial acompanha a peça movida");

        // Sair do isolamento reexibe o que foi escondido nesta execução e devolve a grade ao mundo.
        vm.OnKeyDown("H", false);

        vm.IsIsolating.Should().BeFalse();
        outraNova.IsVisible.Should().BeTrue();
        vm.DisplayFrame.Should().BeNull();
    }

    [Fact]
    public void Isolamento_PecaRemovidaDoCodigo_EncerraOIsolamento()
    {
        var vm = TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new OpenCascadeGeometryKernel());

        var peca = new PartInstance<PecaDeTeste>(new PecaDeTeste(), Corpo(), Placement.Identity) { Name = "peca" };
        vm.Sketch.AddFeature(peca);
        peca.IsSelected = true;
        vm.OnKeyDown("H", false);

        vm.Sketch.ClearFeatures();
        vm.Sketch.AddFeature(new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "outra" });
        vm.ReapplyIsolation();

        vm.IsIsolating.Should().BeFalse();
        vm.DisplayFrame.Should().BeNull();
    }
}
