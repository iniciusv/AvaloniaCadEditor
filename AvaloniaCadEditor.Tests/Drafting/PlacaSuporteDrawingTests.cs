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
    private static readonly Lazy<Task<(DraftingSheet Sheet, PartInstance Placa)>> TopA = new(() => Draw("placa_suporte_carrinho_y_a", StandardView.Top));

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

    /// <summary>Os textos de uma cadeia, da borda para a outra ponta.</summary>
    private static string[] Chain(DraftingSheet sheet, DimensionKind kind, DimensionSide side, int level) =>
        sheet.Dimensions.Where(d => d.Kind == kind && d.Side == side && d.Level == level)
            .OrderBy(d => kind == DimensionKind.Horizontal ? Math.Min(d.From.U, d.To.U) : Math.Min(d.From.V, d.To.V))
            .Select(d => d.Text).ToArray();

    [Fact]
    public async Task Vertical_ACadeiaDeCadaLadoDeEntrada_DePontaAPonta()
    {
        var sheet = await Sheet();

        // Lado por onde entram x_5/6/7 (à esquerda nesta placa espelhada): x_5, furo_1, x_6, o eixo comum
        // de furo_2/furo_3 (e porca_inferior_3) e x_7, fechando na outra ponta com a referência.
        Chain(sheet, DimensionKind.Vertical, DimensionSide.Left, 1).Should().Equal("6", "5,5", "14,5", "10,5", "6,5", "(5)");
        // Lado por onde entram x_1/x_2: o canto do recorte, x_2, x_1.
        Chain(sheet, DimensionKind.Vertical, DimensionSide.Right, 1).Should().Equal("23,59", "4,41", "15", "(5)");
        // As porcas que entram por baixo, numa cadeia à parte, por fora: porca_inferior_1 e 2.
        Chain(sheet, DimensionKind.Vertical, DimensionSide.Right, 2).Should().Equal("16", "18");
        sheet.Suppressed.Should().BeEmpty();
    }

    /// <summary>A placa A é a B espelhada: as mesmas cadeias, com os lados trocados.</summary>
    [Fact]
    public async Task PlacaA_DesenhoEspelhadoDaB()
    {
        var (sheet, placa) = await TopA.Value;
        output.WriteLine(DraftingReport.Format(sheet, sheet.Dimensions, placa.Name, placa.PartTypeName));

        Chain(sheet, DimensionKind.Vertical, DimensionSide.Right, 1).Should().Equal("6", "5,5", "14,5", "10,5", "6,5", "(5)");
        Chain(sheet, DimensionKind.Vertical, DimensionSide.Left, 1).Should().Equal("23,59", "4,41", "15", "(5)");
        Chain(sheet, DimensionKind.Vertical, DimensionSide.Left, 2).Should().Equal("16", "18");
        sheet.Suppressed.Should().BeEmpty();
    }

    [Fact]
    public async Task EixoComum_DosFurosEDaPorca_ApareceNaCota()
    {
        var sheet = await Sheet();

        // O ponto V = 36,5 da cadeia é o eixo comum de furo_2 e furo_3; a porca_inferior_3 também está nele.
        var ate365 = sheet.Dimensions.Single(d => d.Kind == DimensionKind.Vertical && Math.Abs(d.To.V - 36.5) < 1e-6);
        ate365.SourceNames.Should().Contain(["furo_fixacao_carrinho_2", "furo_fixacao_carrinho_3"]);
        ate365.AlignedWith.Should().Contain("porca_inferior_3");
    }

    [Fact]
    public async Task FuroPorcaDeitada_DistanciaDaFaceAteAPorca()
    {
        var sheet = await Sheet();
        double Depth(string name) => sheet.Dimensions.Single(d => d.Key == $"pf:{name}").Value;

        foreach (var x in new[] { "fixacao_x_5", "fixacao_x_6", "fixacao_x_7" }) Depth(x).Should().BeApproximately(5, 1e-6);
        foreach (var x in new[] { "fixacao_x_1", "fixacao_x_2" }) Depth(x).Should().BeApproximately(3, 1e-6);
        Depth("fixacao_y_2").Should().BeApproximately(4.084, 1e-6);
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
