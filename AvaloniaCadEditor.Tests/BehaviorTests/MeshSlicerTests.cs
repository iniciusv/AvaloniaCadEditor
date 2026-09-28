using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines.MultiAxis;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Testes unitários do fatiador multi-eixo (<see cref="MeshSlicer"/>) usado pelo
/// <see cref="AvaloniaCadEditor.Infrastructure.Engines.BaseProfileCutsAlgorithm"/>.
/// </summary>
public class MeshSlicerTests
{
    [Fact]
    public void Slice_Cubo_GeraUmUnicoLoopComQuatroVertices()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 10, length: 10, height: 10);
        var mesh = kernel.BuildFeatureMesh(box);

        var loops = MeshSlicer.Slice(mesh, AxisFrame.Z, level: 5.0);

        loops.Should().HaveCount(1);
        loops[0].Should().HaveCount(4);
    }

    [Fact]
    public void FindCapLevels_Cubo_EncontraApenasBaseETopo()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 10, length: 10, height: 10);
        var mesh = kernel.BuildFeatureMesh(box);

        var levels = MeshSlicer.FindCapLevels(mesh, AxisFrame.Z, tolerance: 0.05);

        levels.Should().HaveCount(2);
        levels[0].Should().BeApproximately(0.0, 0.05);
        levels[1].Should().BeApproximately(10.0, 0.05);
    }

    [Fact]
    public void Slice_PlacaComFuro_GeraContornoExternoEUmFuro()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var plate = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 5);
        var hole = new CylinderFeature(new Point3D(10, 10, -1), new Vector3D(0, 0, 1), radius: 3, height: 7);
        var plateWithHole = BooleanFeature.CreateDifference("Placa", plate, hole);
        var mesh = kernel.BuildFeatureMesh(plateWithHole);

        var loops = MeshSlicer.Slice(mesh, AxisFrame.Z, level: 2.5);
        var regions = MeshSlicer.BuildRegions(loops);

        regions.Should().HaveCount(1);
        regions[0].Holes.Should().HaveCount(1);

        var fit = LoopFitting.FitCircle(regions[0].Holes[0]);
        fit.Radius.Should().BeApproximately(3.0, 0.2);
    }

    [Fact]
    public void Prismaticity_CuboSimples_EhAltaEmTodosOsEixos()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 10, length: 10, height: 10);
        var mesh = kernel.BuildFeatureMesh(box);

        foreach (var axis in AxisFrame.All)
        {
            MeshSlicer.Prismaticity(mesh, axis).Should().BeGreaterThan(0.99);
        }
    }
}
