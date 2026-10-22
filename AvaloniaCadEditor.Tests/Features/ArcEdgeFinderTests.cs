using System;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Features;

/// <summary>
/// Arcos reconhecidos nas arestas vivas da malha: cada um vira um anel de chanfro só, em vez de um corte por
/// segmento. Um polígono de verdade não pode passar por arco.
/// </summary>
public class ArcEdgeFinderTests
{
    private static System.Collections.Generic.IReadOnlyList<CircularEdge> Arcos(IFeature feature, int profileSegments = 48)
    {
        var settings = new TessellationSettings { ProfileSegments = profileSegments };
        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(feature, settings);
        var classification = EdgeDetector.ClassifyEdges(mesh.Positions, mesh.Indices, settings.CreaseAngle);
        return ArcEdgeFinder.Find(mesh.Positions, classification, ArcEdgeFinder.MaxStepFor(profileSegments));
    }

    private static BoxFeature Placa() => new(Point3D.Origin, 40, 30, 10) { Name = "placa" };

    [Theory]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(51)]
    public void FuroPassante_DuasBocasCirculoInteiro_LadoFuro(int profileSegments)
    {
        var furo = new CylinderFeature(new Point3D(20, 15, -1), Vector3D.UnitZ, 3, 12) { Name = "furo" };
        var furada = BooleanFeature.CreateDifference("furada", Placa(), furo);

        var arcos = Arcos(furada, profileSegments);

        arcos.Should().HaveCount(2);
        arcos.Should().AllSatisfy(a =>
        {
            a.IsHole.Should().BeTrue();
            a.IsFullCircle.Should().BeTrue();
            a.Radius.Should().BeApproximately(3, 1e-3);
            a.Center.X.Should().BeApproximately(20, 1e-3);
            a.Center.Y.Should().BeApproximately(15, 1e-3);
            a.EdgeIndices.Should().HaveCount(profileSegments);
        });
        // O eixo é a normal da face plana, para fora do material: +Z em cima, −Z embaixo.
        arcos.Single(a => a.Center.Z > 5).Axis.Z.Should().BeApproximately(1, 1e-6);
        arcos.Single(a => a.Center.Z < 5).Axis.Z.Should().BeApproximately(-1, 1e-6);
    }

    [Fact]
    public void Ressalto_SoOTopoEArco_ComOMaterialDentro()
    {
        var ressalto = new CylinderFeature(new Point3D(20, 15, 10), Vector3D.UnitZ, 5, 4) { Name = "ressalto" };
        var corpo = BooleanFeature.CreateUnion("corpo", Placa(), ressalto);

        var arcos = Arcos(corpo);

        // A base do ressalto é côncava (encontra o topo da placa por dentro): não entra.
        var topo = arcos.Should().ContainSingle().Subject;
        topo.IsHole.Should().BeFalse();
        topo.Radius.Should().BeApproximately(5, 1e-3);
        topo.Center.Z.Should().BeApproximately(14, 1e-3);
    }

    [Fact]
    public void FuroNaBorda_ArcoDeMeiaVolta()
    {
        // Centro do furo sobre a face y = 0: na placa sobra meio círculo.
        var furo = new CylinderFeature(new Point3D(20, 0, -1), Vector3D.UnitZ, 4, 12) { Name = "furo" };
        var furada = BooleanFeature.CreateDifference("furada", Placa(), furo);

        var arcos = Arcos(furada);

        arcos.Should().HaveCount(2);
        double passo = 2 * Math.PI / 48;
        arcos.Should().AllSatisfy(a =>
        {
            a.IsHole.Should().BeTrue();
            a.IsFullCircle.Should().BeFalse();
            a.SweepRadians.Should().BeInRange(Math.PI - 2 * passo, Math.PI + 2 * passo);
        });
    }

    [Fact]
    public void PrismaSextavado_NaoEArco()
    {
        var vertices = Enumerable.Range(0, 6)
            .Select(k => new Point3D(10 * Math.Cos(k * Math.PI / 3), 10 * Math.Sin(k * Math.PI / 3), 0))
            .ToArray();
        var sextavado = new PrismFeature(vertices, height: 5, plane: new WorkPlane(Vector3D.UnitZ, Point3D.Origin)) { Name = "sextavado" };

        Arcos(sextavado).Should().BeEmpty();
    }

    [Fact]
    public void FuroInclinado_NaoEArcoDeFacePlana()
    {
        var eixo = new Vector3D(0, 1, 1).Normalize();
        var furo = new CylinderFeature(new Point3D(20, 5, -5), eixo, 3, 30) { Name = "furo" };
        var furada = BooleanFeature.CreateDifference("furada", Placa(), furo);

        // A boca é uma elipse e a parede não é paralela à normal da face: fica para o corte por segmento.
        Arcos(furada).Should().BeEmpty();
    }
}
