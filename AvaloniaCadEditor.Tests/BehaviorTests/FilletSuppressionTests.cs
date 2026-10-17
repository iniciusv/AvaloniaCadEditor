using System;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Chanfros/fillets desligados por enquanto no esboço: o kernel devolve o sólido base (quinas vivas para o snap),
/// e religar reaproveita as malhas já construídas.
/// </summary>
public class FilletSuppressionTests
{
    private static readonly Point3D Quina = new(10, 10, 10);

    private static FilletFeature CaixaChanfrada() =>
        new("chanfrado", new BoxFeature(Point3D.Origin, 10, 10, 10), 1.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };

    private static bool TemVertice(TriangleMesh mesh, Point3D p)
    {
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (Math.Abs(mesh.Positions[i * 3] - p.X) < 1e-3 && Math.Abs(mesh.Positions[i * 3 + 1] - p.Y) < 1e-3 && Math.Abs(mesh.Positions[i * 3 + 2] - p.Z) < 1e-3)
                return true;
        }
        return false;
    }

    private static SceneMeshManager MeshManager(OpenCascadeGeometryKernel kernel)
    {
        var settings = new Mock<ITessellationSettingsProvider>();
        settings.Setup(s => s.GetSettings()).Returns(new TessellationSettings());
        return new SceneMeshManager(kernel, settings.Object);
    }

    [Fact]
    public void ChanfroSuprimido_DevolveASolidoBase_ComAsQuinasVivas()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var chanfrado = CaixaChanfrada();

        TemVertice(kernel.BuildFeatureMesh(chanfrado), Quina).Should().BeFalse("o chanfro corta a quina");

        kernel.SuppressFillets = true;
        TemVertice(kernel.BuildFeatureMesh(chanfrado), Quina).Should().BeTrue();
    }

    [Fact]
    public void ChanfroDentroDeBooleana_TambemESuprimido()
    {
        var kernel = new OpenCascadeGeometryKernel { SuppressFillets = true };
        var furo = new CylinderFeature(new Point3D(5, 5, -1), new Vector3D(0, 0, 1), 2, 12);
        var furado = BooleanFeature.CreateDifference("furado", CaixaChanfrada(), furo);

        TemVertice(kernel.BuildFeatureMesh(furado), Quina).Should().BeTrue();
    }

    [Fact]
    public void Religar_DevolveAMesmaMalhaDeAntes_SemReconstruir()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var meshes = MeshManager(kernel);
        var sketch = new Sketch();
        var chanfrado = CaixaChanfrada();
        sketch.AddFeature(chanfrado);

        var original = meshes.GetFeatureMesh(sketch, chanfrado.Id, null);
        meshes.SuppressFillets = true;
        var suprimida = meshes.GetFeatureMesh(sketch, chanfrado.Id, null);
        int construidas = kernel.MeshBuildCount;
        meshes.SuppressFillets = false;
        var religada = meshes.GetFeatureMesh(sketch, chanfrado.Id, null);

        TemVertice(suprimida, Quina).Should().BeTrue();
        religada.Should().BeSameAs(original);
        kernel.MeshBuildCount.Should().Be(construidas, "a versão com chanfro continuava em cache");
    }

    [Fact]
    public void SairDoEsboco_ReligaOsChanfros()
    {
        var geometryEngine = new MathGeometryEngine();
        var vm = TestUtils.CreateCadViewModel(geometryEngine, new OSnapService(geometryEngine), geometryKernel: new OpenCascadeGeometryKernel());
        vm.EnterSketchMode(vm.Sketch.WorkPlanes[0]);

        vm.IsFilletSuppressed = true;
        vm.MeshManager.SuppressFillets.Should().BeTrue();

        vm.ExitSketchMode();
        vm.IsFilletSuppressed.Should().BeFalse();
        vm.MeshManager.SuppressFillets.Should().BeFalse();
    }
}
