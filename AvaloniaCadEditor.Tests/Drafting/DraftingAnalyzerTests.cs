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
        DraftingAnalyzer.Analyze(new DraftingInput(root, Placement.Identity, view, Point3D.Origin, max))!;

    [Fact]
    public void Front_FurosEspelhados_ViramCirculosComDiametroAgrupadoESimetria()
    {
        var sheet = Sheet(Bloco(), StandardView.Front, new Point3D(40, 16, 17));

        sheet.Entities.OfType<ViewCircle>().Select(c => c.SourceName).Should().BeEquivalentTo("furo_a", "furo_b");
        sheet.Dimensions.Should().ContainSingle(d => d.Kind == DimensionKind.Diameter)
            .Which.Text.Should().Be("2× Ø3,4");

        // Simétrica em U: só a primeira metade é cotada (10), mais a largura total (40).
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal).Select(d => d.Value)
            .Should().Equal(10, 40);
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Vertical).Select(d => d.Value)
            .Should().Equal(5, 17);
        sheet.CenterLines.Should().Contain(l => l.A.U == 20 && l.B.U == 20, "a linha de simetria passa no meio");
    }

    [Fact]
    public void Front_FileirasFicamForaDaPeca_EAMaiorMaisLonge()
    {
        var sheet = Sheet(Bloco(), StandardView.Front, new Point3D(40, 16, 17));

        var horizontais = sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal).ToList();
        horizontais.Should().OnlyContain(d => d.LinePosition < sheet.PartMin.V);
        horizontais.Last().LinePosition.Should().BeLessThan(horizontais.First().LinePosition);
    }

    [Fact]
    public void Top_MesmosFurosVistosDeLado_ViramRetangulosComDiametroNaLateral()
    {
        var sheet = Sheet(Bloco(), StandardView.Top, new Point3D(40, 16, 17));

        sheet.Entities.OfType<ViewRect>().Where(r => r.Role == EntityRole.Void)
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
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal).Select(d => d.Value).Should().Equal(12, 40);
        sheet.Dimensions.Single(d => d.Kind == DimensionKind.Diameter).Text.Should().Be("Ø3");
    }

    [Fact]
    public void Iso_NaoTemDesenho()
    {
        DraftingAnalyzer.Analyze(new DraftingInput(Bloco(), Placement.Identity, StandardView.Isometric, Point3D.Origin, new Point3D(1, 1, 1)))
            .Should().BeNull();
    }

    [Fact]
    public void CotaManual_EntreAncoras_MedeADistancia()
    {
        var sheet = Sheet(Bloco(), StandardView.Front, new Point3D(40, 16, 17));
        var a = sheet.FindAnchor("furo_a.centro")!;
        var b = sheet.FindAnchor("furo_b.centro")!;

        var cota = AutoDimensioner.Linear(a, b, DimensionKind.Horizontal, -10);

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
    public async Task BlocoFixacaoBelt_Front_FurosDeFixacaoEPressaoAgrupados()
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
            .Should().Contain(new[] { "furo_fixacao", "furo_fixacao_espelhado", "furo_pressao_correia", "furo_pressao_correia_espelhado" });
        // furo_fixacao e furo_pressao_correia têm o mesmo raio (1,7): os quatro viram um grupo.
        sheet.Dimensions.Should().Contain(d => d.Kind == DimensionKind.Diameter && d.Text == "4× Ø3,4");
        sheet.Dimensions.Should().Contain(d => d.Kind == DimensionKind.Diameter && d.Text == "2× Ø7,2");
        sheet.CenterLines.Should().NotBeEmpty();
        sheet.Warnings.Should().BeEmpty();

        // A emenda das duas metades espelhadas (meio da peça) não vira cota: é a linha de simetria.
        double meio = (sheet.PartMin.U + sheet.PartMax.U) / 2;
        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Horizontal && !d.Text.StartsWith("Ø") && !d.Text.Contains('×'))
            .Should().NotContain(d => System.Math.Abs(d.To.U - meio) < 1e-3);
        // O alojamento hexagonal é cotado pela largura (a chave da porca M3).
        sheet.Dimensions.Should().Contain(d => d.Text == "2× 5,83");

        // Na vista lateral os furos espelhados se sobrepõem: uma posição só, sem "2×".
        var lateral = DraftingAnalyzer.Analyze(DraftingInput.ForIsolated(bloco, frame, positions, StandardView.Right)!)!;
        lateral.Dimensions.Should().Contain(d => d.Text == "Ø6");
    }
}
