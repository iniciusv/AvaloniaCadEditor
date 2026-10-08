using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Drafting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Drafting;

public class DensityTests
{
    private const double Gap = 3;
    private const double Tol = 3e-3;
    private static readonly P2 PartMin = new(0, 0);
    private static readonly P2 PartMax = new(40, 20);

    /// <summary>Cota horizontal embaixo da peça, com as pernas saindo da borda de baixo (v = 0).</summary>
    private static DraftDimension H(string key, double a, double b) =>
        new(key, DimensionKind.Horizontal, new P2(a, 0), new P2(b, 0), 0, b - a, "1", [key]);

    private static LayoutGroup G(int level, params DraftDimension[] dims) => new(dims, DimensionSide.Below, level);

    [Fact]
    public void ParesAninhados_AMenorFicaPorDentro_ESemCruzamentos()
    {
        // Os pares simétricos do BlocoFixacaoBelt: 32, 29, 19,77 e 11, todos centrados em 20,5.
        var groups = new[] { G(1, H("32", 4.5, 36.5)), G(1, H("29", 6, 35)), G(1, H("19,77", 10.62, 30.38)), G(1, H("11", 15, 26)) };

        var placed = DimensionLayout.Place(groups, PartMin, PartMax, Gap);

        placed.OrderByDescending(d => d.LinePosition).Select(d => d.Key)
            .Should().Equal("11", "19,77", "29", "32");
        DimensionCrossings.Find(placed, Gap, Tol).Should().BeEmpty();
    }

    [Fact]
    public void Cruzamento_PernaQueAtravessaUmaCotaInterna()
    {
        var interna = H("interna", 0, 30) with { LinePosition = -3, Side = DimensionSide.Below };
        var externa = H("externa", 10, 35) with { LinePosition = -6, Side = DimensionSide.Below };

        var crossings = DimensionCrossings.Find([interna, externa], Gap, Tol);

        // A perna da externa em u = 10 desce de v = 0 até v = -6 e passa pela interna (0..30) em v = -3.
        crossings.Should().ContainSingle().Which.Should().Be(new DimensionCrossings.Crossing(1, 0));
    }

    [Fact]
    public void Cadeia_PernaQueEncostaNaPontaNaoCruza()
    {
        var interna = H("interna", 0, 10) with { LinePosition = -3, Side = DimensionSide.Below };
        var externa = H("externa", 10, 35) with { LinePosition = -6, Side = DimensionSide.Below };

        DimensionCrossings.Find([interna, externa], Gap, Tol).Should().BeEmpty();
    }

    [Fact]
    public void Politica_EscondeALocalizacaoPrimeiro_ENuncaOTotal()
    {
        var cadeia = G(1, H("cadeia", 0, 30));
        var localizacao = G(2, H("loc", 0, 12));     // perna em 12 cruza a cadeia (0..30), que fica por dentro
        var total = G(3, H("total", 0, 40));

        var (placed, suppressed) = DensityPolicy.Apply([cadeia, localizacao, total], PartMin, PartMax, Gap, Tol);

        suppressed.Select(s => s.Dimension.Key).Should().Equal("loc");
        suppressed[0].Reason.Should().Be(SuppressionReason.Crossings);
        suppressed[0].Dimension.Level.Should().Be(2);
        placed.Select(d => d.Key).Should().BeEquivalentTo("cadeia", "total");
    }

    [Fact]
    public void Politica_NivelForcadoNaoESconde()
    {
        var groups = new[] { G(1, H("cadeia", 0, 30)), G(2, H("loc", 0, 12)), G(3, H("total", 0, 40)) };

        var (placed, suppressed) = DensityPolicy.Apply(groups, PartMin, PartMax, Gap, Tol,
            new DraftingOptions { ForcedLevels = new HashSet<int> { 2 } });

        suppressed.Should().BeEmpty();
        placed.Should().Contain(d => d.Key == "loc");
    }

    [Fact]
    public void Politica_FileirasDemais_EscondeAMaisExternaDoNivel1()
    {
        // Quatro cadeias que se sobrepõem (uma fileira cada) + o total: 5 fileiras, limite 3.
        var groups = new[]
        {
            G(1, H("a", 0, 5)), G(1, H("b", 0, 10)), G(1, H("c", 0, 15)), G(1, H("d", 0, 20)), G(3, H("total", 0, 40))
        };

        var (placed, suppressed) = DensityPolicy.Apply(groups, PartMin, PartMax, Gap, Tol,
            new DraftingOptions { MaxRowsPerSide = 3, MaxCrossingsPerSide = int.MaxValue });

        suppressed.Select(s => s.Dimension.Key).Should().BeEquivalentTo("d", "c");
        suppressed.Should().OnlyContain(s => s.Reason == SuppressionReason.Rows);
        placed.Should().Contain(d => d.Key == "total");
    }
}
