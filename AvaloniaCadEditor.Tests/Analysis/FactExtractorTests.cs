using System.Linq;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Analysis;

public class FactExtractorTests
{
    private sealed record PecaDeTeste : Part
    {
        protected override IFeature Build() => throw new System.NotSupportedException();
    }

    private static IFeature Bloco()
    {
        var corpo = new BoxFeature(Point3D.Origin, 40, 16, 17) { Name = "corpo" };
        var furo = new CylinderFeature(new Point3D(10, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo", IsTransientTool = true };
        return BooleanFeature.CreateDifference("bloco", corpo, furo);
    }

    private static AnalysisSubject Sujeito(IFeature feature, Placement? toFrame = null) =>
        new(feature, toFrame ?? Placement.Identity, "peca");

    [Fact]
    public void Booleana_CorpoEMaterial_FerramentaEVazio()
    {
        var facts = FactExtractor.Extract([Sujeito(Bloco())]);

        facts.Should().HaveCount(2);
        facts.Single(f => f.Name == "corpo").Role.Should().Be(PrimitiveRole.Body);
        var furo = facts.Single(f => f.Name == "furo");
        furo.Role.Should().Be(PrimitiveRole.Void);
        furo.Kind.Should().Be(PrimitiveKind.Cylinder);
        furo.Radius.Should().Be(1.7);
    }

    [Fact]
    public void Referencial_AplicaOPlacementAosFatos()
    {
        var facts = FactExtractor.Extract([Sujeito(Bloco(), Placement.At(100, 0, 26, rotationZDegrees: 180))]);

        var furo = facts.Single(f => f.Name == "furo");
        furo.BaseCenter.Should().Be(new Point3D(90, -16, 31));
        furo.Axis.Should().Be(new Vector3D(0, 1, 0));
        var corpo = facts.Single(f => f.Name == "corpo");
        corpo.BoundsMin.Should().Be(new Point3D(60, -16, 26));
        corpo.BoundsMax.Should().Be(new Point3D(100, 0, 43));
    }

    [Fact]
    public void PartInstanceAninhada_GanhaNomeQualificadoEAcumulaOPlacement()
    {
        var suporte = new PartInstance<PecaDeTeste>(new PecaDeTeste(), Bloco(), Placement.At(50, 0, 0)) { Name = "suporte" };
        var placa = new BoxFeature(new Point3D(0, 0, -5), 200, 50, 5) { Name = "placa" };
        var montagem = BooleanFeature.CreateUnion("montagem", placa, suporte);

        var facts = FactExtractor.Extract([Sujeito(montagem, Placement.At(0, 0, 10))]);

        facts.Select(f => f.Name).Should().BeEquivalentTo("placa", "suporte/corpo", "suporte/furo");
        facts.Single(f => f.Name == "suporte/furo").BaseCenter.Should().Be(new Point3D(60, 16, 15));
    }

    [Fact]
    public void TransformedFeature_AplicaOPlacementELevaONomeDaCopia()
    {
        var furo = new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, 2, 10) { Name = "furo" };
        var copia = furo.Transformed(Placement.At(30, 0, 0), "furo_copia");

        var facts = FactExtractor.Extract([Sujeito(furo), Sujeito(copia)]);

        facts.Select(f => f.Name).Should().BeEquivalentTo("furo", "furo_copia");
        facts.Single(f => f.Name == "furo_copia").BaseCenter.Should().Be(new Point3D(30, 0, 0));
    }

    [Fact]
    public void BooleanaAninhada_FerramentaDentroDeFerramentaContinuaVazio()
    {
        var corpo = new BoxFeature(Point3D.Origin, 40, 40, 10) { Name = "corpo" };
        var rasgo_a = new BoxFeature(new Point3D(5, 5, -1), 5, 30, 12) { Name = "rasgo_a" };
        var rasgo_b = new BoxFeature(new Point3D(30, 5, -1), 5, 30, 12) { Name = "rasgo_b" };
        var rasgos = BooleanFeature.CreateUnion("rasgos", rasgo_a, rasgo_b);

        var facts = FactExtractor.Extract([Sujeito(BooleanFeature.CreateDifference("peca", corpo, rasgos))]);

        facts.Where(f => f.Name.StartsWith("rasgo")).Should().OnlyContain(f => f.Role == PrimitiveRole.Void);
    }

    [Fact]
    public void ExtrusaoCircular_ViraCilindro_EPoligonalViraPrisma()
    {
        var circulo = new CircleProfile(new Point3D(5, 5, 0), 3);
        var quadrado = new PolygonProfile(new[] { new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 10, 0), new Point3D(0, 10, 0) });

        var facts = FactExtractor.Extract([
            Sujeito(new ExtrudeFeature(circulo, 8, Vector3D.UnitZ, "pino")),
            Sujeito(new ExtrudeFeature(quadrado, 4, Vector3D.UnitZ, "base"))]);

        var pino = facts.Single(f => f.Name == "pino");
        pino.Kind.Should().Be(PrimitiveKind.Cylinder);
        pino.Radius.Should().Be(3);
        pino.Height.Should().Be(8);
        var baseFato = facts.Single(f => f.Name == "base");
        baseFato.Kind.Should().Be(PrimitiveKind.Prism);
        baseFato.Profile.Should().HaveCount(4);
        baseFato.BoundsMax.Should().Be(new Point3D(10, 10, 4));
    }

    [Fact]
    public void MesmaPrimitivaRegistradaDuasVezes_ViraUmFatoSo()
    {
        var corpo = new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "corpo" };
        var furo = new CylinderFeature(new Point3D(5, 5, -1), Vector3D.UnitZ, 1, 12) { Name = "furo", IsTransientTool = true };
        var bloco = BooleanFeature.CreateDifference("bloco", corpo, furo);

        // Como o script real: operandas e booleana registradas separadamente.
        var facts = FactExtractor.Extract([Sujeito(corpo), Sujeito(furo), Sujeito(bloco)]);

        facts.Should().HaveCount(2);
    }
}
