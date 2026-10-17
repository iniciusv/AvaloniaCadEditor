using System;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// <see cref="SweepPath.AroundCircles"/>: o caminho de uma correia por polias de raios diferentes.
/// O que importa é que cada reta seja tangente aos dois vizinhos no sentido pedido — conferido pela
/// continuidade do rumo de trecho em trecho e pelo raio de cada arco.
/// </summary>
public class SweepPathAroundCirclesTests
{
    private static readonly Vector3D Up = Vector3D.UnitZ;

    [Fact]
    public void VoltaDeMeiaPolia_RetaArcoReta_ComOComprimentoExato()
    {
        // Vai em +X por y = 10, chega no topo da polia (raio 10) e volta em −X por y = −10: indo em
        // +X por cima, a volta é horária.
        var caminho = SweepPath.AroundCircles(
            new Point3D(0, 10, 0),
            new[] { new SweepWrap(new Point3D(50, 0, 0), 10, Clockwise: true) },
            new Point3D(0, -10, 0),
            Up);

        caminho.Error.Should().BeNull();
        caminho.Segments.Select(s => s.IsArc).Should().Equal(false, true, false);
        caminho.Segments[0].LineVector.X.Should().BeApproximately(50, 1e-9);
        caminho.Segments[1].ArcAngle.Should().BeApproximately(Math.PI, 1e-9);
        caminho.Segments[1].ArcAxis.Z.Should().BeApproximately(-1, 1e-9, "horário visto de +Z");
        caminho.Segments[2].LineVector.X.Should().BeApproximately(-50, 1e-9);

        var fim = new SweepFeature(SweepSection.Rectangle(2, 6), caminho, upReference: Up).EndCenter;
        fim.X.Should().BeApproximately(0, 1e-9);
        fim.Y.Should().BeApproximately(-10, 1e-9);
    }

    [Fact]
    public void PoliaEntreDoisRolamentosDeSentidoOposto_TodasAsRetasSaoTangentes()
    {
        // Ômega: rolamento horário, polia anti-horária por baixo, rolamento horário — as duas retas
        // do meio são tangentes cruzadas, com raios diferentes dos dois lados.
        var rolamentoA = new SweepWrap(new Point3D(-16, 0, 5), 7.1, Clockwise: true);
        var polia = new SweepWrap(new Point3D(0, -16, 5), 8.0, Clockwise: false);
        var rolamentoB = new SweepWrap(new Point3D(16, 0, 5), 7.1, Clockwise: true);

        var caminho = SweepPath.AroundCircles(
            new Point3D(-60, 7.1, 5), new[] { rolamentoA, polia, rolamentoB }, new Point3D(60, 7.1, 5), Up);

        caminho.Error.Should().BeNull();
        caminho.Segments.Select(s => s.IsArc).Should().Equal(false, true, false, true, false, true, false);

        var estacoes = caminho.Stations(new WorkPlane(caminho.StartTangent, caminho.Start));
        for (int i = 0; i < caminho.Segments.Count; i++)
        {
            var trecho = caminho.Segments[i];
            if (!trecho.IsArc)
            {
                // Uma reta segue o rumo com que o trecho anterior terminou: sem canto vivo.
                trecho.LineVector.Normalize().DotProduct(estacoes[i].Normal).Should().BeApproximately(1, 1e-9, $"trecho {i}");
                continue;
            }

            var raio = estacoes[i].Origin.DistanceTo(trecho.ArcCenter);
            var esperado = new[] { rolamentoA, polia, rolamentoB }.Single(c => c.Center.DistanceTo(trecho.ArcCenter) < 1e-9).Radius;
            raio.Should().BeApproximately(esperado, 1e-9, $"o arco {i} começa sobre o seu círculo");
        }

        estacoes[^1].Origin.DistanceTo(new Point3D(60, 7.1, 5)).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void CorreiaVarrida_ViraUmSolidoSo_ComOVolumeDoComprimentoVezesASecao()
    {
        var caminho = SweepPath.AroundCircles(
            new Point3D(0, 10, 0),
            new[] { new SweepWrap(new Point3D(50, 0, 0), 10, Clockwise: true) },
            new Point3D(0, -10, 0),
            Up);
        var correia = new SweepFeature(SweepSection.Rectangle(2, 6), caminho, upReference: Up);

        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(correia);

        // Seção retangular centrada no caminho: Pappus é exato no arco.
        double comprimento = 50 + Math.PI * 10 + 50;
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(12 * comprimento, 12 * comprimento * 0.01);
        // Lateral (perímetro 16) + as duas tampas das pontas; uma junção não fundida deixaria mais duas.
        double area = 16 * comprimento + 2 * 12;
        MeshAreaCalculator.ComputeArea(mesh).Should().BeApproximately(area, area * 0.01);
    }

    [Theory]
    [InlineData("dentro", 0.0, 3.0, "Não passa reta tangente")]
    [InlineData("fora do plano", 4.0, 10.0, "do plano do caminho")]
    [InlineData("raio zero", 0.0, 0.0, "precisa ser positivo")]
    public void CaminhoInvalido_ExplicaOPorque_EDevolveMalhaVazia(string caso, double zCentro, double raio, string trecho)
    {
        // "dentro": o início (0, 0) fica a 2 do centro de um círculo de raio 3.
        var centro = caso == "dentro" ? new Point3D(2, 0, zCentro) : new Point3D(50, 0, zCentro);
        var caminho = SweepPath.AroundCircles(
            Point3D.Origin, new[] { new SweepWrap(centro, raio, Clockwise: false) }, new Point3D(0, -40, 0), Up);

        caminho.Error.Should().Contain(trecho);
        caminho.Segments.Should().BeEmpty();
        new OpenCascadeGeometryKernel().BuildFeatureMesh(new SweepFeature(SweepSection.Rectangle(2, 6), caminho)).VertexCount.Should().Be(0);
    }

    [Fact]
    public void SemCirculos_PedeFromPolyline()
    {
        SweepPath.AroundCircles(Point3D.Origin, Array.Empty<SweepWrap>(), new Point3D(10, 0, 0), Up)
            .Error.Should().Contain("FromPolyline");
    }
}
