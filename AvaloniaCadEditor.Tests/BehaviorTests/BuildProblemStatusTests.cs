using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Application.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Uma primitiva degenerada devolve malha VAZIA, não erro: o sólido some da viewport e, sem este
/// caminho, a explicação morre dentro do kernel. Os testes do kernel provam que o motivo fica
/// registrado; estes provam que ele chega à barra de status, que é o que o usuário vê.
/// </summary>
public class BuildProblemStatusTests
{
    private static CadViewModelFixture Montar()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var mockEngine = new Mock<IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        mockSnap.Setup(s => s.GetSnappedPoint(It.IsAny<Sketch>(), It.IsAny<Point3D>(), It.IsAny<Point3D?>(), It.IsAny<TriangleMesh>()))
                .Returns<Sketch, Point3D, Point3D?, TriangleMesh>((s, p, t, m) => p);

        var vm = TestUtils.CreateCadViewModel(mockEngine.Object, mockSnap.Object, null, kernel, new SelectionService());
        return new CadViewModelFixture(vm, kernel);
    }

    private readonly record struct CadViewModelFixture(UI.ViewModels.CadViewModel Vm, OpenCascadeGeometryKernel Kernel);

    [Fact]
    public void VarreduraComCaminhoInvalido_ExplicaNaBarraDeStatus()
    {
        var (vm, _) = Montar();

        var tubo = new SweepFeature(
            SweepSection.Circle(5),
            SweepPath.FromPolyline(
                new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(150, 0, 0), new Point3D(150, 90, 0) },
                cornerRadius: 60),
            name: "tubo_ruim");
        vm.Sketch.AddFeature(tubo);

        vm.RefreshSolidMesh(skipCodeGeneration: true);

        vm.StatusText.Should().Contain("tubo_ruim").And.Contain("não cabe");
    }

    [Fact]
    public void CenaSaudavel_NaoMexeNaBarraDeStatus()
    {
        // Só sobrescreve quando HÁ problema: quem chamou pode ter acabado de escrever a sua mensagem.
        var (vm, _) = Montar();
        vm.StatusText = "mensagem de quem chamou";

        vm.Sketch.AddFeature(new BoxFeature(Point3D.Origin, 20, 20, 20));
        vm.RefreshSolidMesh(skipCodeGeneration: true);

        vm.StatusText.Should().Be("mensagem de quem chamou");
    }

    [Fact]
    public void ProblemaResolvido_ParaDeAparecer()
    {
        // Se o problema ficasse pendurado no kernel, a barra mentiria para sempre depois do conserto.
        var (vm, kernel) = Montar();

        var tubo = new SweepFeature(
            SweepSection.Circle(5),
            SweepPath.FromPolyline(new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) }, 3),
            name: "tubo");
        vm.Sketch.AddFeature(tubo);
        vm.RefreshSolidMesh(skipCodeGeneration: true);
        vm.StatusText.Should().Contain("tubo");

        tubo.Path = SweepPath.FromPolyline(new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) }, 30);
        vm.MeshManager.InvalidateFeatureAndDependents(vm.Sketch, tubo.Id);
        vm.StatusText = "depois do conserto";
        vm.RefreshSolidMesh(skipCodeGeneration: true);

        kernel.BuildProblems.Should().BeEmpty();
        vm.StatusText.Should().Be("depois do conserto");
    }
}
