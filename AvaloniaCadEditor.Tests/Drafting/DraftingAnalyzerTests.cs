using AvaloniaCadEditor.Analysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Isolation;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Drafting;

public class DraftingAnalyzerTests
{
    private sealed record PecaDeTeste : Part
    {
        protected override IFeature Build() => throw new System.NotSupportedException();
    }

    /// <summary>
    /// Bloco 40 (X) × 16 (Y) × 17 (Z) com dois furos espelhados em Y (vistos de frente) e um furo
    /// vertical em Z (visto de lado no Front), à esquerda.
    /// </summary>
    private static IFeature Bloco()
    {
        var corpo = new BoxFeature(Point3D.Origin, 40, 16, 17) { Name = "corpo" };
        var furo_a = new CylinderFeature(new Point3D(10, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_a", IsTransientTool = true };
        var furo_b = new CylinderFeature(new Point3D(30, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_b", IsTransientTool = true };
        return BooleanFeature.CreateDifference("bloco", corpo, furo_a, furo_b);
    }

    private static DraftingSheet Sheet(IFeature root, StandardView view, Point3D max) =>
        DraftingAnalyzer.Analyze(DraftingInput.ForRoot(root, Placement.Identity, view, Point3D.Origin, max))!;

    [Fact]
    public void Front_FurosEspelhados_ViramCirculosComDiametroAgrupadoESimetria()
    {
        var sheet = Sheet(Bloco(), StandardView.Front, new Point3D(40, 16, 17));

        sheet.Entities.OfType<ViewCircle>().Select(c => c.SourceName).Should().BeEquivalentTo("furo_a", "furo_b");
        sheet.Dimensions.Should().ContainSingle(d => d.Kind == DimensionKind.Diameter)
            .Which.Text.Should().Be("2× Ø3,4");

        // Simétrica em U: o par é cotado pela distância entre os furos (20), centrada na simetria,
        // em vez de cada furo a partir da borda; por fora, a largura total (40).
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal).Select(d => d.Value)
            .Should().BeEquivalentTo(new[] { 20.0, 40.0 });
        sheet.Dimensions.Single(d => d.Kind == DimensionKind.Horizontal && d.Value == 20).Level.Should().Be(1);
        sheet.Dimensions.Single(d => d.Kind == DimensionKind.Horizontal && d.Value == 40).Level.Should().Be(3);
        // Em V não há simetria: a cadeia da esquerda vai da borda à altura dos furos (5) e fecha na outra
        // borda com o elo de referência (12); por fora, a altura total (17), nível 3.
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Vertical).Select(d => (d.Text, d.Level))
            .Should().BeEquivalentTo(new[] { ("5", 1), ("(12)", 1), ("17", 3) });
        sheet.CenterLines.Should().Contain(l => l.A.U == 20 && l.B.U == 20, "a linha de simetria passa no meio");
    }

    [Fact]
    public void Front_FileirasFicamForaDaPeca_EAMaiorMaisLonge()
    {
        var sheet = Sheet(Bloco(), StandardView.Front, new Point3D(40, 16, 17));

        var horizontais = sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal).ToList();
        horizontais.Should().OnlyContain(d => d.LinePosition < sheet.PartMin.V);
        var total = horizontais.Single(d => d.Level == 3);
        horizontais.Where(d => d.Level < 3).Should().OnlyContain(d => d.LinePosition > total.LinePosition, "o total fica por fora");
    }

    [Fact]
    public void Top_MesmosFurosVistosDeLado_ViramRetangulosComDiametroNaLateral()
    {
        var sheet = Sheet(Bloco(), StandardView.Top, new Point3D(40, 16, 17));

        sheet.Entities.OfType<ViewRect>().Where(r => r.Role == PrimitiveRole.Void)
            .Should().HaveCount(2).And.OnlyContain(r => r.Axis == CenterAxis.Vertical);
        sheet.Dimensions.Should().Contain(d => d.Text == "2× Ø3,4" && d.Kind == DimensionKind.Horizontal);
    }

    [Fact]
    public void FuroAssimetrico_SemSimetria_CotaTodasAsPosicoes()
    {
        var corpo = new BoxFeature(Point3D.Origin, 40, 16, 17) { Name = "corpo" };
        var furo = new CylinderFeature(new Point3D(12, 16, 5), new Vector3D(0, -1, 0), 1.5, 20) { Name = "furo", IsTransientTool = true };
        var sheet = Sheet(BooleanFeature.CreateDifference("bloco", corpo, furo), StandardView.Front, new Point3D(40, 16, 17));

        sheet.CenterLines.Should().NotContain(l => l.A.U == 20 && l.B.U == 20);
        // Borda → furo (12), fechando na outra borda com a referência (28); por fora, o total.
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal).Select(d => d.Text).Should().BeEquivalentTo("12", "(28)", "40");
        sheet.Dimensions.Single(d => d.Kind == DimensionKind.Diameter).Text.Should().Be("Ø3");
    }

    [Fact]
    public void Iso_NaoTemDesenho()
    {
        DraftingAnalyzer.Analyze(DraftingInput.ForRoot(Bloco(), Placement.Identity, StandardView.Isometric, Point3D.Origin, new Point3D(1, 1, 1)))
            .Should().BeNull();
    }

    [Fact]
    public void CotaManual_EntreAncoras_MedeADistancia()
    {
        var sheet = Sheet(Bloco(), StandardView.Front, new Point3D(40, 16, 17));
        var a = sheet.FindAnchor("furo_a.centro")!;
        var b = sheet.FindAnchor("furo_b.centro")!;

        var cota = DimensionFactory.Linear(a, b, DimensionKind.Horizontal, -10);

        cota.Value.Should().Be(20);
        cota.Text.Should().Be("20");
        cota.IsManual.Should().BeTrue();
        cota.SourceNames.Should().Equal("furo_a", "furo_b");
    }

    [Fact]
    public void Graficos_CotaHorizontal_TemLinhaSetasEChamadas()
    {
        var cota = new DraftDimension("k", DimensionKind.Horizontal, new P2(0, 0), new P2(10, 5), -3, 10, "10", []);

        var g = DimensionGraphics.Build(cota, 1);

        g.Segments.Should().Contain(s => s.A == new P2(0, -3) && s.B == new P2(10, -3), "linha de cota");
        g.Segments.Should().HaveCount(1 + 4 + 2, "linha + 4 traços de seta + 2 linhas de chamada");
        g.LabelAnchor.Should().Be(new P2(5, -3));
    }

    /// <summary>O exemplo da conversa: bloco retangular com uma fileira de furos igualmente espaçados.</summary>
    [Fact]
    public void ChamadasDeComponente_ConfiguracoesDiferentesSaoSeparadas_ComOParametroQueMuda()
    {
        var placa = BooleanFeature.CreateDifference("placa", new BoxFeature(Point3D.Origin, 100, 20, 10) { Name = "corpo" },
            new AvaloniaCadEditor.Tests.Analysis.ComponentTests.FixacaoFunda(20, 4).Instance("a"),
            new AvaloniaCadEditor.Tests.Analysis.ComponentTests.FixacaoFunda(40, 4).Instance("b"),
            new AvaloniaCadEditor.Tests.Analysis.ComponentTests.FixacaoFunda(60, 6).Instance("c"));
        var sheet = Sheet(placa, StandardView.Top, new Point3D(100, 20, 10));

        // O X muda dentro de cada configuração (é posição); a Profundidade é o que as distingue.
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Callout).Select(d => d.Text)
            .Should().BeEquivalentTo("2× FixacaoFunda · Profundidade 4", "FixacaoFunda · Profundidade 6");
    }

    private static IFeature BlocoComFileira(params double[] xs)
    {
        var corpo = new BoxFeature(Point3D.Origin, 100, 16, 17) { Name = "corpo" };
        var furos = xs.Select((x, i) => (IFeature)new CylinderFeature(new Point3D(x, 16, 5), new Vector3D(0, -1, 0), 1.7, 20)
        {
            Name = $"furo_{i + 1}",
            IsTransientTool = true
        }).ToArray();
        return BooleanFeature.CreateDifference("bloco", corpo, furos);
    }

    [Fact]
    public void FileiraComPassoConstante_BordaAoPrimeiroEPassoCompactoNaMesmaFileira_TotalPorFora()
    {
        var sheet = Sheet(BlocoComFileira(10, 30, 50, 70), StandardView.Front, new Point3D(100, 16, 17));

        var horizontais = sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal).ToList();
        var borda = horizontais.Should().ContainSingle(d => d.Text == "10").Subject;
        var passo = horizontais.Should().ContainSingle(d => d.Text == "3× 20 (=60)").Subject;
        var total = horizontais.Should().ContainSingle(d => d.Text == "100").Subject;

        borda.Level.Should().Be(1);
        passo.LinePosition.Should().Be(borda.LinePosition, "a cadeia borda → 1º furo → passo fica numa linha só");
        total.Level.Should().Be(3);
        total.LinePosition.Should().BeLessThan(borda.LinePosition, "o total fica por fora");
        // Nenhuma cota da borda até o 2º, 3º ou 4º furo (o "(30)" é o elo de referência do 4º à outra borda).
        horizontais.Should().NotContain(d => (d.Value == 30 || d.Value == 50 || d.Value == 70) && !d.Text.StartsWith("("));
        horizontais.Should().ContainSingle(d => d.Text == "(30)").Which.LinePosition.Should().Be(borda.LinePosition);
    }

    [Fact]
    public void FileiraSimetrica_UmaCotaDoPadraoCentradaNaSimetria()
    {
        var sheet = Sheet(BlocoComFileira(20, 40, 60, 80), StandardView.Front, new Point3D(100, 16, 17));

        sheet.Dimensions.Should().ContainSingle(d => d.Text == "3× 20 (=60)");
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal).Select(d => d.Text)
            .Should().BeEquivalentTo(new[] { "3× 20 (=60)", "100" }, "os pares já estão no padrão; nada é cotado a partir da borda");
        sheet.CenterLines.Should().Contain(l => l.A.U == 50 && l.B.U == 50);
    }

    [Fact]
    public void Layout_CotasQueNaoSeSobrepoemDividemAFileira_ENivelMaiorFicaPorFora()
    {
        DraftDimension H(string key, double a, double b) =>
            new(key, DimensionKind.Horizontal, new P2(a, 0), new P2(b, 0), 0, b - a, "1", []);

        var placed = DimensionLayout.Place(
        [
            new LayoutGroup([H("a", 0, 10)], DimensionSide.Below, 1),
            new LayoutGroup([H("b", 40, 50)], DimensionSide.Below, 1),
            new LayoutGroup([H("c", 5, 45)], DimensionSide.Below, 1),
            new LayoutGroup([H("total", 0, 50)], DimensionSide.Below, 3)
        ], new P2(0, 0), new P2(50, 20), gap: 2);

        double Line(string key) => placed.Single(d => d.Key == key).LinePosition;
        Line("a").Should().Be(Line("b"), "intervalos disjuntos dividem a fileira");
        Line("c").Should().BeLessThan(Line("a"), "o que se sobrepõe sobe de fileira");
        Line("total").Should().BeLessThan(Line("c"), "um nível maior nunca fica por dentro de um menor");
    }

    // ---- Peça real da cena AiCore -------------------------------------------------------------

    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile!)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, "AiCore");
    }

    [Fact]
    public async Task BlocoFixacaoBelt_Front_FurosDeFixacaoAgrupados()
    {
        var project = SceneProject.Load(AiCoreFolder());
        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());
        result.Success.Should().BeTrue(result.ErrorMessage);

        var bloco = result.Features.OfType<PartInstance>().Single(f => f.Name == "bloco_fixacao_belt");
        var positions = new OpenCascadeGeometryKernel().BuildFeatureMesh(bloco).Positions;
        var frame = PartFrameResolver.Resolve(bloco, positions)!;

        var sheet = DraftingAnalyzer.Analyze(DraftingInput.ForIsolated(bloco, frame, positions, StandardView.Front)!)!;

        sheet.PartMin.U.Should().BeApproximately(0, 1e-3, "a origem do referencial é o canto mínimo");
        sheet.PartMin.V.Should().BeApproximately(0, 1e-3);
        sheet.Entities.OfType<ViewCircle>().Select(c => c.SourceName)
            .Should().Contain(new[] { "furo_fixacao", "furo_fixacao_espelhado" });
        // furo_fixacao é coaxial com o rebaixo_fixacao: uma chamada só com os dois diâmetros. A
        // correia é presa pela GarraCorreia do topo, sem parafuso de pressão: nenhum Ø3,4 sozinho.
        sheet.Dimensions.Should().Contain(d => d.Kind == DimensionKind.Diameter && d.Text == "2× Ø3,4 / Ø7,2");
        sheet.Dimensions.Should().NotContain(d => d.Kind == DimensionKind.Diameter && d.Text == "2× Ø3,4");
        // Simétrica: pares cotados entre si, menos fileiras que a cotagem a partir da borda (eram 8).
        sheet.Dimensions.Where(d => d.Side == DimensionSide.Below).Select(d => d.LinePosition).Distinct().Count()
            .Should().BeLessThan(8);
        sheet.CenterLines.Should().NotBeEmpty();
        sheet.Warnings.Should().BeEmpty();

        // A emenda das duas metades espelhadas (meio da peça) não vira cota: é a linha de simetria.
        double meio = (sheet.PartMin.U + sheet.PartMax.U) / 2;
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal && !d.Text.StartsWith("Ø") && !d.Text.Contains('×'))
            .Should().NotContain(d => System.Math.Abs(d.To.U - meio) < 1e-3);
        // Na vista lateral os furos espelhados se sobrepõem: uma posição só, sem "2×".
        var lateral = DraftingAnalyzer.Analyze(DraftingInput.ForIsolated(bloco, frame, positions, StandardView.Right)!)!;
        lateral.Dimensions.Should().NotBeEmpty();
        lateral.Dimensions.Should().NotContain(d => d.Text.Contains('×'));
    }
}
