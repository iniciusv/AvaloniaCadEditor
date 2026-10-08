using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Isolation;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AvaloniaCadEditor.Tests.Drafting;

/// <summary>
/// O desenho da placa B do carrinho Y (espelhada), vista Top — o caso que guiou as regras das cadeias:
/// a malha 25 dos furos do carrinho não pode sair partida por uma porca no mesmo alinhamento, o
/// recorte do canto precisa estar cotado, e as FuroPorca laterais com profundidades diferentes não
/// podem virar "6× a mesma coisa". Folha = código + (16, 24).
/// </summary>
public class PlacaSuporteDrawingTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<(DraftingSheet Sheet, PartInstance Placa)>> Top = new(() => Draw("placa_suporte_carrinho_y_b", StandardView.Top));

    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile!)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore"))) dir = dir.Parent;
        dir.Should().NotBeNull("este teste lê a cena AiCore do repo");
        return Path.Combine(dir!.FullName, "AiCore");
    }

    private static async Task<(DraftingSheet, PartInstance)> Draw(string instance, StandardView view)
    {
        var project = SceneProject.Load(AiCoreFolder());
        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());
        result.Success.Should().BeTrue(result.ErrorMessage);

        var placa = result.Features.OfType<PartInstance>().Single(f => f.Name == instance);
        var positions = new OpenCascadeGeometryKernel().BuildFeatureMesh(placa).Positions;
        var frame = PartFrameResolver.Resolve(placa, positions)!;
        return (DraftingAnalyzer.Analyze(DraftingInput.ForIsolated(placa, frame, positions, view)!)!, placa);
    }

    private async Task<DraftingSheet> Sheet()
    {
        var (sheet, placa) = await Top.Value;
        output.WriteLine(DraftingReport.Format(sheet, sheet.Dimensions, placa.Name, placa.PartTypeName));
        return sheet;
    }

    private static DraftDimension[] All(DraftingSheet sheet) =>
        sheet.Dimensions.Concat(sheet.Suppressed.Select(s => s.Dimension)).ToArray();

    [Fact]
    public async Task MalhaDosFurosDoCarrinho_NaoEPartidaPelaPorca()
    {
        var sheet = await Sheet();

        // Horizontal: furo_2 (3,5) → furo_3 (28,5) é a malha 25; a porca_inferior_3 em 10,5 não entra no meio.
        var malha = All(sheet).Where(d => d.Kind == DimensionKind.Horizontal && d.SourceNames.Contains("furo_fixacao_carrinho_3")).ToList();
        malha.Should().ContainSingle().Which.Value.Should().BeApproximately(25, 1e-6);
        All(sheet).Should().NotContain(d => d.Kind == DimensionKind.Horizontal && Math.Abs(d.Value - 18) < 1e-6 && d.SourceNames.Contains("furo_fixacao_carrinho_3"));
    }

    [Fact]
    public async Task EixoComum_DosFurosEDaPorca_ApareceNaCota()
    {
        var sheet = await Sheet();

        // Vertical: o 25 de furo_1 → furo_2 termina em V = 36,5, o eixo de furo_3 e porca_inferior_3 também.
        var ate365 = All(sheet).Single(d => d.Kind == DimensionKind.Vertical && d.SourceNames.Contains("furo_fixacao_carrinho_2") && Math.Abs(d.Value - 25) < 1e-6);
        ate365.AlignedWith.Should().Contain(["furo_fixacao_carrinho_3", "porca_inferior_3"]);
    }

    [Fact]
    public async Task Recorte_SempreCotado()
    {
        var sheet = await Sheet();

        // Canto do recorte em U = 14 e V = 23,594: cadeias de contorno, nunca escondidas.
        sheet.Dimensions.Should().Contain(d => d.Kind == DimensionKind.Horizontal && Math.Abs(d.To.U - 14) < 1e-3 && d.SourceNames.Contains("corpo_chanfrado"));
        sheet.Dimensions.Should().Contain(d => d.Kind == DimensionKind.Vertical && Math.Abs(d.To.V - 23.594) < 1e-3 && d.SourceNames.Contains("corpo_chanfrado"));
        sheet.Suppressed.Should().NotContain(s => s.Dimension.SourceNames.Contains("corpo_chanfrado"));
    }

    /// <summary>
    /// As 6 laterais são a MESMA peça: a Profundidade da FuroPorca desloca o conjunto inteiro (o furo
    /// sempre começa 8 mm antes do alojamento), então ela diz onde a porca fica, não a forma — é posição,
    /// e não separa a chamada.
    /// </summary>
    [Fact]
    public async Task ChamadasDosComponentes_PelaForma()
    {
        var sheet = await Sheet();

        sheet.Dimensions.Where(d => d.Kind == DimensionKind.Callout).Select(d => d.Text)
            .Should().BeEquivalentTo("3× FuroPorca M3 Axial", "6× FuroPorca M3 Lateral");
    }
}
