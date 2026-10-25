using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using static AvaloniaCadEditor.Tests.Features.TestMeshes;

namespace AvaloniaCadEditor.Tests.Features;

public class MeshFacesTests
{
    [Fact]
    public void RegiaoDoTopoDaCaixa_SaoOsDoisTriangulosDoTopo()
    {
        var graph = MeshEdgeGraph.Build(Box(0, 0, 10, 10, 0, 5));
        int seed = MeshFaces.FindSeed(graph, new Point3D(3, 7, 5), new Vector3D(0, 0, 1));

        var region = MeshFaces.Region(graph, seed);

        region.Should().HaveCount(2);
        region.Should().OnlyContain(t => graph.Normal(t).Z > 0.99);
    }

    [Fact]
    public void RegiaoDaLateralDoCilindro_CobreALateralInteira_SemAsTampas()
    {
        var graph = MeshEdgeGraph.Build(Cylinder(0, 0, 3, 0, 5));
        int seed = MeshFaces.FindSeed(graph, new Point3D(3, 0, 2), new Vector3D(1, 0, 0));

        var region = MeshFaces.Region(graph, seed);

        region.Should().HaveCount(64, "32 facetas, cada uma partida em dois triângulos");
        region.Should().OnlyContain(t => System.Math.Abs(graph.Normal(t).Z) < 1e-6);
    }

    [Fact]
    public void SementeReencontraAFace_NaMalhaRetesselada()
    {
        var antes = MeshEdgeGraph.Build(Cylinder(0, 0, 3, 0, 5, sides: 32));
        var depois = MeshEdgeGraph.Build(Cylinder(0, 0, 3, 0, 5, sides: 48));
        var ponto = new Point3D(0, 3, 2.5);
        var normal = new Vector3D(0, 1, 0);

        MeshFaces.FindSeed(antes, ponto, normal).Should().NotBe(MeshFaces.NotFound);
        int seed = MeshFaces.FindSeed(depois, ponto, normal);

        MeshFaces.Region(depois, seed).Should().HaveCount(96);
    }

    [Fact]
    public void SementeSemNormalParecida_NaoAcha()
    {
        var graph = MeshEdgeGraph.Build(Box(0, 0, 10, 10, 0, 5));

        MeshFaces.FindSeed(graph, new Point3D(5, 5, 5), new Vector3D(1, 1, 1).Normalize() * -1)
            .Should().Be(MeshFaces.NotFound);
    }
}
