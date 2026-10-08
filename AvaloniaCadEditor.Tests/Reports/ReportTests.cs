using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.ReferenceGuides;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ReferenceGuides;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AvaloniaCadEditor.Tests.Reports;

/// <summary>
/// Os relatórios copiáveis (guias e cotas) são o que o usuário cola numa conversa junto do código da
/// peça: precisam dizer o que cada linha é, onde está e de quais sólidos veio — pelo nome do código.
/// </summary>
public class ReportTests(ITestOutputHelper output)
{
    /// <summary>Bloco 40 × 16 × 17 com dois furos espelhados em Y, como nos testes do desenho.</summary>
    private static IFeature Bloco()
    {
        var corpo = new BoxFeature(Point3D.Origin, 40, 16, 17) { Name = "corpo" };
        var furo_a = new CylinderFeature(new Point3D(10, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_a", IsTransientTool = true };
        var furo_b = new CylinderFeature(new Point3D(30, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_b", IsTransientTool = true };
        return BooleanFeature.CreateDifference("bloco", corpo, furo_a, furo_b);
    }

    [Fact]
    public void Guias_DizemEixoCoordenadaESolidosPeloNome()
    {
        var set = new ReferenceGuideService().Analyze(new List<IFeature> { Bloco() });
        var text = ReferenceGuideReport.Format(set);
        output.WriteLine(text);

        set.Lines.Should().OnlyContain(l => l.SourceNames.Count == l.SourceFeatureIds.Count);
        text.Should().Contain("## Eixo comum de furos (ciano)");
        text.Should().Contain("Z = 5", "os dois furos estão na mesma altura");
        text.Should().Contain("plano ⟂ X: X = 20 · global");
        text.Should().Contain("furo_a").And.Contain("furo_b");
    }

    [Fact]
    public void UmaGuiaSo_TemCabecalhoESoElaMesma()
    {
        var set = new ReferenceGuideService().Analyze(new List<IFeature> { Bloco() });
        var symmetry = set.Lines.Single(l => l.Kind == GuideKind.Symmetry);

        var text = ReferenceGuideReport.FormatLine(symmetry);
        output.WriteLine(text);

        text.Should().StartWith("# Guia de referência — Simetria");
        text.Split('\n').Count(l => l.StartsWith("- ")).Should().Be(1);
    }

    [Fact]
    public void Guias_MarcamAsDaFeatureSelecionada()
    {
        var bloco = Bloco();
        var set = new ReferenceGuideService().Analyze(new List<IFeature> { bloco });

        ReferenceGuideReport.Format(set, highlightedId: bloco.Id, highlightedName: bloco.Name)
            .Should().Contain("# selecionada: bloco").And.Contain("[destacada]");
    }

    [Fact]
    public void Cotas_SaemEmCadeiasPorNivelComOsEixosDaVista()
    {
        var sheet = DraftingAnalyzer.Analyze(DraftingInput.ForRoot(Bloco(), Placement.Identity, StandardView.Front, Point3D.Origin, new Point3D(40, 16, 17)))!;
        var text = DraftingReport.Format(sheet, sheet.Dimensions, "bloco", "Bloco");
        output.WriteLine(text);

        text.Should().Contain("vista Front");
        text.Should().Contain("U → direita = +X da peça, V → cima = +Z");
        text.Should().Contain("### Nível 1 — Cadeias e tamanhos");
        text.Should().Contain("### Nível 3 — Totais");
        text.Should().Contain("furo_a · vazio · círculo Ø3.4 centro (10, 5)");
        text.Should().Contain("2× Ø3,4");
    }

    [Fact]
    public void Cotas_CabecalhoDizOndeFicaAOrigemDoCodigo()
    {
        var sheet = DraftingAnalyzer.Analyze(DraftingInput.ForRoot(Bloco(), Placement.Identity, StandardView.Top, Point3D.Origin, new Point3D(40, 16, 17)))!;

        DraftingReport.Format(sheet, sheet.Dimensions, "bloco", codeOrigin: new Point3D(20, 8, 0))
            .Should().Contain("# origem do código (0, 0, 0) na folha: U 20, V 8");
    }

    [Fact]
    public void Guias_FuroComRebaixoNaoViraFileiraSozinho_EMembrosContamUmaVez()
    {
        var corpo = new BoxFeature(Point3D.Origin, 40, 16, 10) { Name = "corpo" };
        var furo = new CylinderFeature(new Point3D(10, 8, -1), Vector3D.UnitZ, 1.7, 12) { Name = "furo", IsTransientTool = true };
        var rebaixo = new CylinderFeature(new Point3D(10, 8, 7), Vector3D.UnitZ, 3, 4) { Name = "rebaixo", IsTransientTool = true };
        var outro = new CylinderFeature(new Point3D(30, 8, -1), Vector3D.UnitZ, 1.7, 12) { Name = "outro", IsTransientTool = true };
        var set = new ReferenceGuideService().Analyze(new List<IFeature> { BooleanFeature.CreateDifference("bloco", corpo, furo, rebaixo, outro) });
        output.WriteLine(ReferenceGuideReport.Format(set));

        var rows = set.Lines.Where(l => l.Kind == GuideKind.HoleRow).ToList();
        rows.Should().ContainSingle("só a linha y = 8 tem dois furos distintos; x = 10 é um furo com rebaixo")
            .Which.MemberNames.Should().Equal("furo+rebaixo", "outro");
    }

    [Fact]
    public void CadeiaClicada_MostraAFileiraEOsSolidosMedidos()
    {
        var sheet = DraftingAnalyzer.Analyze(DraftingInput.ForRoot(Bloco(), Placement.Identity, StandardView.Front, Point3D.Origin, new Point3D(40, 16, 17)))!;
        var between = sheet.Dimensions.Single(d => d.Kind == DimensionKind.Horizontal && d.Value == 20);

        var text = DraftingReport.FormatChain(sheet, sheet.Dimensions, between, "bloco");
        output.WriteLine(text);

        text.Should().Contain("## Cadeia clicada — nível 1");
        text.Should().Contain("▶ 20");
        text.Should().Contain("## Sólidos medidos").And.Contain("furo_a").And.Contain("furo_b");
    }
}
