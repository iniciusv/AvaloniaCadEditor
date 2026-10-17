using System;
using System.Linq;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using static AvaloniaCadEditor.Tests.Features.TestMeshes;

namespace AvaloniaCadEditor.Tests.Features;

public class PlaneSectionTests
{
    [Fact]
    public void FaceNoPlano_ViraUmLacoCoplanarDeQuatroCantos_ESemCorte()
    {
        var refs = PlaneSection.Compute(Box(0, 0, 10, 10, -5, 0), WorkPlane.XY);

        var coplanar = refs.Should().ContainSingle(r => r.Kind == PlaneReferenceKind.Coplanar).Subject;
        coplanar.Closed.Should().BeTrue();
        coplanar.Points.Should().HaveCount(4, "as diagonais do leque da tampa não são arestas vivas");
        refs.Should().NotContain(r => r.Kind == PlaneReferenceKind.Section);
    }

    [Fact]
    public void SolidoAtravessando_DaUmCorteRetangular_SemVerticeDaDiagonal()
    {
        var refs = PlaneSection.Compute(Box(0, 0, 10, 4, -5, 5), WorkPlane.XY);

        var section = refs.Should().ContainSingle().Subject;
        section.Kind.Should().Be(PlaneReferenceKind.Section);
        section.Closed.Should().BeTrue();
        section.Points.Should().HaveCount(4, "o cruzamento da diagonal de cada parede cai no meio da aresta e sai como colinear");
        section.Points.Should().Contain(p => Math.Abs(p.X - 10) < 1e-6 && Math.Abs(p.Y - 4) < 1e-6);
        section.IsCircle.Should().BeFalse();
    }

    [Fact]
    public void SolidoSoEncostando_NaoRepeteOContornoComoCorte()
    {
        var acima = PlaneSection.Compute(Box(0, 0, 10, 10, 0, 5), WorkPlane.XY);

        acima.Should().ContainSingle(r => r.Kind == PlaneReferenceKind.Coplanar);
        acima.Should().NotContain(r => r.Kind == PlaneReferenceKind.Section);
    }

    [Fact]
    public void CilindroAtravessando_DaCorteCircularComCentroERaio()
    {
        var refs = PlaneSection.Compute(Cylinder(2, 1, 3, -1, 1), WorkPlane.XY);

        var section = refs.Should().ContainSingle().Subject;
        section.IsCircle.Should().BeTrue();
        section.Center!.Value.X.Should().BeApproximately(2, 1e-4);
        section.Center!.Value.Y.Should().BeApproximately(1, 1e-4);
        section.Radius!.Value.Should().BeApproximately(3, 1e-2);
    }

    [Fact]
    public void BocaDeCilindroNoPlano_EhCirculoCoplanar()
    {
        var refs = PlaneSection.Compute(Cylinder(0, 0, 2, -3, 0), WorkPlane.XY);

        var mouth = refs.Should().ContainSingle().Subject;
        mouth.Kind.Should().Be(PlaneReferenceKind.Coplanar);
        mouth.IsCircle.Should().BeTrue();
        mouth.Radius!.Value.Should().BeApproximately(2, 1e-2);
    }

    [Fact]
    public void DoisSolidosComFacesNoPlano_DaoDoisLacosCoplanares()
    {
        var mesh = TriangleMesh.Combine(new[] { Box(0, 0, 10, 10, -5, 0), Box(20, 0, 30, 10, -2, 0) }, includeFeatureNormals: false);

        var refs = PlaneSection.Compute(mesh, WorkPlane.XY);

        refs.Where(r => r.Kind == PlaneReferenceKind.Coplanar).Should().HaveCount(2);
    }

    [Fact]
    public void DistanciaAteAReferencia_ContaOTrechoDeFechamentoDoLaco()
    {
        var quadrado = new PlaneReference(PlaneReferenceKind.Coplanar,
            new[] { new Point3D(0, 0), new Point3D(10, 0), new Point3D(10, 10), new Point3D(0, 10) }, Closed: true);
        var aberto = quadrado with { Closed = false };

        quadrado.DistanceTo(new Point3D(-2, 5)).Should().BeApproximately(2, 1e-9, "o lado x = 0 fecha o laço");
        aberto.DistanceTo(new Point3D(-2, 5)).Should().BeApproximately(Math.Sqrt(4 + 25), 1e-9);
        quadrado.DistanceTo(new Point3D(5, 3)).Should().BeApproximately(3, 1e-9);
    }

    [Fact]
    public void PlanoInclinadoNaOrigem_CortaEmCoordenadasLocaisDoPlano()
    {
        // Plano YZ deslocado para x = 5: X local = Y global, Y local = Z global.
        var plane = new WorkPlane(new Point3D(5, 0, 0), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0));

        var refs = PlaneSection.Compute(Box(0, 0, 10, 4, -2, 3), plane);

        var section = refs.Should().ContainSingle().Subject;
        section.Points.Should().HaveCount(4);
        section.Points.Select(p => p.X).Should().OnlyContain(x => Math.Abs(x) < 1e-6 || Math.Abs(x - 4) < 1e-6);
        section.Points.Select(p => p.Y).Should().OnlyContain(y => Math.Abs(y + 2) < 1e-6 || Math.Abs(y - 3) < 1e-6);
    }
}
