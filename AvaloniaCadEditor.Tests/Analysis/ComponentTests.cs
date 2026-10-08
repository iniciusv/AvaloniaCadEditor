using System.Linq;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Analysis;

/// <summary>Peças aninhadas (<c>.Instance(...)</c>) viram componentes: os fatos sabem a que instância
/// pertencem e a análise acha a referência (o eixo) e as fileiras de componentes.</summary>
public class ComponentTests
{
    public enum Rosca { M3, M4 }

    /// <summary>Uma "FuroPorca" mínima: furo vertical + alojamento quadrado, coaxiais, já no lugar.</summary>
    public sealed record Fixacao(double X, double Y, Rosca Rosca = Rosca.M3) : Part
    {
        protected override IFeature Build()
        {
            var furo = new CylinderFeature(new Point3D(X, Y, -1), Vector3D.UnitZ, 1.7, 12) { Name = "furo" };
            var alojamento = new BoxFeature(new Point3D(X - 3, Y - 3, -1), 6, 6, 4) { Name = "alojamento" };
            return BooleanFeature.CreateUnion("fixacao", furo, alojamento);
        }
    }

    public sealed record FixacaoRotulada(double X) : Part
    {
        public override string? DrawingLabel => "Fixação especial";

        protected override IFeature Build() =>
            new CylinderFeature(new Point3D(X, 5, -1), Vector3D.UnitZ, 1.5, 12) { Name = "furo" };
    }

    private static IFeature Placa(params IFeature[] ferramentas) =>
        BooleanFeature.CreateDifference("placa", new BoxFeature(Point3D.Origin, 100, 20, 10) { Name = "corpo" }, ferramentas);

    private static System.Collections.Generic.IReadOnlyList<PrimitiveFact> Fatos(IFeature raiz) =>
        FactExtractor.Extract([new AnalysisSubject(raiz, Placement.Identity, "placa")]);

    [Fact]
    public void FatosDentroDeInstance_SabemOComponente_ERotuloPadraoTrazOsEnums()
    {
        var fatos = Fatos(Placa(new Fixacao(10, 10).Instance("fix_1")));

        var membros = fatos.Where(f => f.Component != null).ToList();
        membros.Select(f => f.Name).Should().BeEquivalentTo("fix_1/furo", "fix_1/alojamento");
        membros.Should().OnlyContain(f => f.Component!.Name == "fix_1" && f.Component.Label == "Fixacao M3");
        fatos.Single(f => f.Name == "corpo").Component.Should().BeNull();
    }

    [Fact]
    public void DrawingLabelDaPeca_VenceOPadrao()
    {
        var fatos = Fatos(Placa(new FixacaoRotulada(10).Instance("esp_1")));

        fatos.Single(f => f.Component != null).Component!.Label.Should().Be("Fixação especial");
    }

    [Fact]
    public void Referencia_EOEixoDoFuro()
    {
        var relacoes = RelationAnalyzer.Analyze(Fatos(Placa(new Fixacao(10, 10).Instance("fix_1"))));

        var componente = relacoes.Components.Should().ContainSingle().Subject;
        componente.Axis.Should().Be(Vector3D.UnitZ);
        componente.AxisPoint.Should().Be(new Point3D(10, 10, -1));
        componente.Principal!.Name.Should().Be("fix_1/furo");
    }

    [Fact]
    public void TresComponentesAlinhados_FormamFileiraDeComponentes_SemOFuroAvulso()
    {
        var avulso = new CylinderFeature(new Point3D(80, 10, -1), Vector3D.UnitZ, 1.7, 12) { Name = "avulso", IsTransientTool = true };
        var relacoes = RelationAnalyzer.Analyze(Fatos(Placa(
            new Fixacao(10, 10).Instance("fix_1"),
            new Fixacao(30, 10).Instance("fix_2"),
            new Fixacao(50, 10).Instance("fix_3"),
            avulso)));

        var fileira = relacoes.LinearPatterns.Should().ContainSingle(p => p.ComponentLabel != null).Subject;
        fileira.ComponentLabel.Should().Be("Fixacao M3");
        fileira.Count.Should().Be(3);
        fileira.Pitch.Should().BeApproximately(20, 1e-9);
        fileira.AllMembers.Select(f => f.Name).Should().NotContain("avulso");
        // O furo avulso de mesmo Ø não forma fileira com os furos de dentro dos componentes.
        relacoes.LinearPatterns.Where(p => p.ComponentLabel == null).Should().BeEmpty();
    }
}
