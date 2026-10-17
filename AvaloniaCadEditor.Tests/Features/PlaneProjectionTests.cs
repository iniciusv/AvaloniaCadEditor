using System;
using System.Linq;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Geometry;
using FluentAssertions;
using static AvaloniaCadEditor.Tests.Features.TestMeshes;

namespace AvaloniaCadEditor.Tests.Features;

public class PlaneProjectionTests
{
    private static readonly WorkPlane Iso = new(new Vector3D(1, 1, 1), Point3D.Origin);

    private static PlaneReference[] ProjectFace(TriangleMesh mesh, Point3D seed, Vector3D normal, WorkPlane plane)
    {
        var graph = MeshEdgeGraph.Build(mesh);
        int triangle = MeshFaces.FindSeed(graph, seed, normal);
        triangle.Should().NotBe(MeshFaces.NotFound);
        return PlaneProjection.Edges(graph, MeshFaces.Region(graph, triangle), plane).ToArray();
    }

    [Fact]
    public void TopoDaCaixaAcimaDoPlano_ViraRetanguloDeQuatroCantos()
    {
        var refs = ProjectFace(Box(0, 0, 10, 6, 5, 8), new Point3D(5, 3, 8), new Vector3D(0, 0, 1), WorkPlane.XY);

        var loop = refs.Should().ContainSingle().Subject;
        loop.Kind.Should().Be(PlaneReferenceKind.Projected);
        loop.Closed.Should().BeTrue();
        loop.Points.Should().HaveCount(4, "a diagonal do leque é suave e não aparece");
        loop.Points.Should().Contain(p => Math.Abs(p.X - 10) < 1e-6 && Math.Abs(p.Y - 6) < 1e-6);
    }

    [Fact]
    public void FaceLateralPerpendicularAoPlano_ViraUmSegmento()
    {
        var refs = ProjectFace(Box(0, 0, 10, 6, 5, 8), new Point3D(5, 0, 6), new Vector3D(0, -1, 0), WorkPlane.XY);

        var segment = refs.Should().ContainSingle().Subject;
        segment.Closed.Should().BeFalse();
        segment.Points.Should().HaveCount(2, "as bordas de cima e de baixo projetam na mesma linha, e as verticais viram ponto");
        segment.Points.Min(p => p.X).Should().BeApproximately(0, 1e-6);
        segment.Points.Max(p => p.X).Should().BeApproximately(10, 1e-6);
        segment.Points.Should().OnlyContain(p => Math.Abs(p.Y) < 1e-6);
    }

    [Fact]
    public void LateralDeCilindroComEixoNaNormal_ViraCirculo()
    {
        var refs = ProjectFace(Cylinder(1, 2, 3, 5, 8), new Point3D(4, 2, 6), new Vector3D(1, 0, 0), WorkPlane.XY);

        var circle = refs.Should().ContainSingle().Subject;
        circle.IsCircle.Should().BeTrue();
        circle.Center!.Value.X.Should().BeApproximately(1, 1e-3);
        circle.Center!.Value.Y.Should().BeApproximately(2, 1e-3);
        circle.Radius!.Value.Should().BeApproximately(3, 1e-2);
    }

    [Fact]
    public void LateralDeCilindroComEixoNoPlano_ViraRetangulo_PelasBordasESilhuetas()
    {
        // Plano XZ: X local = X, Y local = Z. O cilindro (eixo Z) é visto de lado.
        var refs = ProjectFace(Cylinder(0, 0, 3, 5, 8), new Point3D(0, -3, 6), new Vector3D(0, -1, 0), WorkPlane.XZ);

        var loop = refs.Should().ContainSingle().Subject;
        loop.Closed.Should().BeTrue();
        loop.Points.Should().HaveCount(4);
        loop.Points.Select(p => Math.Abs(p.X)).Should().OnlyContain(x => Math.Abs(x - 3) < 1e-3, "as silhuetas ficam em x = ±r");
        loop.Points.Select(p => p.Y).Should().OnlyContain(y => Math.Abs(y - 5) < 1e-6 || Math.Abs(y - 8) < 1e-6);
    }

    [Fact]
    public void CaixaEmVistaIsometrica_TodasAsArestas_DaHexagonoEOsSeisRaios()
    {
        var refs = PlaneProjection.Edges(MeshEdgeGraph.Build(Box(0, 0, 10, 10, 0, 10)), null, Iso);

        refs.Should().HaveCount(12, "as 12 arestas da caixa, inclusive as 3 escondidas, cada uma entre nós de grau 3 ou 6");
        refs.Should().OnlyContain(r => r.Points.Count == 2 && !r.Closed);
        refs.SelectMany(r => r.Points)
            .Select(p => (Math.Round(p.X, 3), Math.Round(p.Y, 3)))
            .Distinct().Should().HaveCount(7, "6 cantos do hexágono e o centro, onde caem os dois vértices da diagonal");
    }

    [Fact]
    public void ContornoDeMolduraComFuroPassante_DaLacoExternoEFuro()
    {
        var moldura = Combine(
            Box(0, 0, 10, 2, 0, 3), Box(0, 8, 10, 10, 0, 3),
            Box(0, 2, 2, 8, 0, 3), Box(8, 2, 10, 8, 0, 3));

        var refs = PlaneProjection.Outline(MeshEdgeGraph.Build(moldura), WorkPlane.XY, new ClipperPolygonUnion());

        refs.Should().HaveCount(2);
        refs.Should().OnlyContain(r => r.Closed && r.Points.Count == 4, "as caixas encostadas se fundem e os vértices das emendas saem como colineares");
        refs.SelectMany(r => r.Points).Should().Contain(p => Math.Abs(p.X - 8) < 1e-6 && Math.Abs(p.Y - 8) < 1e-6, "canto do furo");
    }

    [Fact]
    public void ContornoDeCilindro_EhCirculo()
    {
        var refs = PlaneProjection.Outline(MeshEdgeGraph.Build(Cylinder(0, 0, 3, 0, 5)), WorkPlane.XY, new ClipperPolygonUnion());

        var circle = refs.Should().ContainSingle().Subject;
        circle.IsCircle.Should().BeTrue();
        circle.Radius!.Value.Should().BeApproximately(3, 1e-2);
    }
}
