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

namespace AvaloniaCadEditor.Tests.Drafting;

/// <summary>
/// A PlacaSuporteCarrinhoY real usa 9 FuroPorca (<c>.Instance(...)</c>). No desenho dela cada uma é uma
/// caixa-preta: localizada pelo eixo e identificada por uma chamada, sem as medidas internas (furo,
/// chave da porca, canal), que são parâmetros da FuroPorca.
/// </summary>
public class ComponentDrawingTests
{
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

    private static async Task<DraftingSheet> Desenho(StandardView vista)
    {
        var project = SceneProject.Load(AiCoreFolder());
        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());
        result.Success.Should().BeTrue(result.ErrorMessage);

        var placa = result.Features.OfType<PartInstance>().First(f => f.PartTypeName == "PlacaSuporteCarrinhoY");
        var positions = new OpenCascadeGeometryKernel().BuildFeatureMesh(placa).Positions;
        var frame = PartFrameResolver.Resolve(placa, positions)!;
        return DraftingAnalyzer.Analyze(DraftingInput.ForIsolated(placa, frame, positions, vista)!)!;
    }

    [Fact]
    public async Task Top_FuroPorcaViraChamada_SemMedidasInternas()
    {
        var sheet = await Desenho(StandardView.Top);
        var todas = sheet.Dimensions.Concat(sheet.Suppressed.Select(s => s.Dimension)).ToList();

        // Chamadas dos componentes, pelo rótulo padrão (tipo + enums).
        sheet.Dimensions.Should().Contain(d => d.Kind == DimensionKind.Callout && d.Text == "3× FuroPorca M3 Axial");
        sheet.Dimensions.Should().Contain(d => d.Kind == DimensionKind.Callout && d.Text.EndsWith("FuroPorca M3 Lateral"));

        // Nada medido por dentro: nem a chave da porca, nem o furo, nem nada vindo de "instancia/sólido".
        todas.Should().NotContain(d => d.Text.Contains("5,83"));
        todas.Should().NotContain(d => d.SourceNames.Any(s => s.Contains('/')));

        // Os furos de fixação do carrinho são furos do próprio sólido: continuam com o Ø deles.
        sheet.Dimensions.Should().Contain(d => d.Kind == DimensionKind.Diameter && d.Text == "3× Ø3,4" &&
                                               d.SourceNames.All(s => s.StartsWith("furo_fixacao_carrinho")));

        // As marcas de componente localizam pela instância (o que o clique revela no código).
        todas.Should().Contain(d => d.SourceNames.Contains("porca_inferior_2"));
    }

    [Fact]
    public async Task Front_FixacoesLateraisSaoLocalizadasPelaAlturaDoEixo()
    {
        var sheet = await Desenho(StandardView.Front);

        // As 6 fixações laterais têm o eixo na mesma altura: uma cota só, de todas elas.
        sheet.Dimensions.Should().Contain(d => d.Kind == DimensionKind.Vertical &&
                                               d.SourceNames.Count(s => s.StartsWith("fixacao_")) == 6);
        DimensionCrossings.Find(sheet.Dimensions.ToList(), sheet.Gap, 3e-3).Should().BeEmpty();
    }
}
