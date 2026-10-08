using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Analysis;

/// <summary>
/// Trava contra a falha silenciosa do <see cref="FactExtractor"/>: uma feature que ele não conhece
/// some das cotas e das guias sem erro nenhum. Toda feature concreta do Domain precisa estar tratada
/// (e produzir fatos de verdade) ou ignorada de propósito, com motivo.
/// </summary>
public class FactExtractorCoverageTests
{
    private sealed record PecaDeTeste : Part
    {
        protected override IFeature Build() => throw new NotSupportedException();
    }

    private static BoxFeature Caixa() => new(Point3D.Origin, 10, 10, 10) { Name = "caixa" };

    /// <summary>Uma instância mínima de cada tipo tratado. Tipo novo em HandledFeatureTypes → exemplo novo aqui.</summary>
    private static readonly Dictionary<Type, Func<IFeature>> Exemplos = new()
    {
        [typeof(BoxFeature)] = Caixa,
        [typeof(CylinderFeature)] = () => new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, 1, 5),
        [typeof(ConeFeature)] = () => new ConeFeature(Point3D.Origin, Vector3D.UnitZ, 2, 1, 5),
        [typeof(PrismFeature)] = () => new PrismFeature(new[] { new Point3D(0, 0, 0), new Point3D(5, 0, 0), new Point3D(0, 5, 0) }, 3),
        [typeof(ExtrudeFeature)] = () => new ExtrudeFeature(new CircleProfile(Point3D.Origin, 2), 4, Vector3D.UnitZ),
        [typeof(BooleanFeature)] = () => BooleanFeature.CreateUnion("uniao", Caixa()),
        [typeof(PartInstance)] = () => new PartInstance<PecaDeTeste>(new PecaDeTeste(), Caixa(), Placement.At(1, 0, 0)),
        [typeof(TransformedFeature)] = () => Caixa().Transformed(Placement.At(1, 0, 0)),
        [typeof(FilletFeature)] = () => new FilletFeature("fillet", Caixa(), 1)
    };

    [Fact]
    public void TodaFeatureConcreta_EstaTratadaOuIgnoradaDePropósito()
    {
        var naoDecididas = typeof(FeatureBase).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(FeatureBase).IsAssignableFrom(t))
            .Where(t => !FactExtractor.HandledFeatureTypes.Any(h => h.IsAssignableFrom(t)))
            .Where(t => !FactExtractor.IgnoredFeatureTypes.ContainsKey(t))
            .ToList();

        naoDecididas.Should().BeEmpty(
            "uma feature fora do FactExtractor some das cotas e das guias em silêncio: trate-a no Walk " +
            "(e em HandledFeatureTypes) ou registre o motivo em IgnoredFeatureTypes");
    }

    [Fact]
    public void TodoTipoTratado_ProduzFatos()
    {
        foreach (var tipo in FactExtractor.HandledFeatureTypes)
        {
            Exemplos.Should().ContainKey(tipo, $"{tipo.Name} está em HandledFeatureTypes e precisa de um exemplo neste teste");
            var fatos = FactExtractor.Extract([new AnalysisSubject(Exemplos[tipo](), Placement.Identity)]);
            fatos.Should().NotBeEmpty($"{tipo.Name} é declarado como tratado, então o percurso precisa gerar fatos dele");
        }
    }
}
