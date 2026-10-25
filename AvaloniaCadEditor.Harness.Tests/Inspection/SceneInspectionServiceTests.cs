using System.Runtime.CompilerServices;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Harness.Inspection;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit.Abstractions;

namespace AvaloniaCadEditor.Harness.Tests.Inspection;

/// <summary>
/// Headless sobre a cena AiCore do repo (como os testes de Drafting): avaliador Roslyn e kernel OCCT
/// reais. A placa B do carrinho Y é a mesma de PlacaSuporteDrawingTests.
/// Atenção: a cena muda no disco quando o app edita, então nomes de sólidos podem precisar de ajuste.
/// </summary>
public class SceneInspectionServiceTests(ITestOutputHelper output)
{
    private const string Placa = "placa_suporte_carrinho_y_b";

    private static readonly Lazy<Task<IReadOnlyList<IFeature>>> Scene = new(LoadScene);

    private static async Task<IReadOnlyList<IFeature>> LoadScene()
    {
        var project = SceneProject.Load(AiCoreFolder());
        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());
        result.Success.Should().BeTrue(result.ErrorMessage);
        return result.Features;
    }

    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile!)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore"))) dir = dir.Parent;
        dir.Should().NotBeNull("este teste lê a cena AiCore do repo");
        return Path.Combine(dir!.FullName, "AiCore");
    }

    private static async Task<(SceneInspectionService Service, InspectionTarget Target)> PrepareAsync(string name = Placa)
    {
        var features = await Scene.Value;
        var resolved = TargetResolver.Resolve(features, name);
        resolved.Error.Should().BeNull();
        var service = new SceneInspectionService(new OpenCascadeGeometryKernel());
        return (service, service.Prepare(resolved.Feature!));
    }

    [Fact]
    public async Task VistaTopDaPlaca_ListaOsFurosComONomeEODiametroExato()
    {
        var (service, target) = await PrepareAsync();

        string text = service.DescribeView(target, StandardView.Top);
        output.WriteLine(text);

        text.Should().StartWith($"# Vista Top — {Placa} (PlacaSuporteCarrinhoY)");
        text.Should().Contain("referencial da peça");
        text.Should().Contain("[furo_fixacao_carrinho_2]");
        text.Should().MatchRegex(@"\[furo_fixacao_carrinho_2\] · mundo: entrada \([-\d.]+, [-\d.]+, [-\d.]+\) → ", "o furo traz o eixo no mundo");
        text.Should().Contain("[furo_fixacao_carrinho_3]");
        text.Should().MatchRegex(@"C\d+ Ø[\d.]+ centro \([\d.]+, 36\.5\) \[furo_fixacao_carrinho_2\]",
            "o eixo comum dos furos 2 e 3 fica em V = 36,5 (ver PlacaSuporteDrawingTests)");
    }

    [Fact]
    public async Task Vista_RespeitaOOrcamento()
    {
        var (service, target) = await PrepareAsync();

        string text = service.DescribeView(target, StandardView.Front, new ViewTextOptions { MaxChars = 1500 });
        output.WriteLine(text);

        text.Length.Should().BeLessThanOrEqualTo(1500);
        text.Should().Contain("## Omitidas pelo orçamento");
    }

    [Fact]
    public async Task Vista_EDeterministicaEntreServicos()
    {
        var (a, targetA) = await PrepareAsync();
        var (b, targetB) = await PrepareAsync();

        a.DescribeView(targetA, StandardView.Right).Should().Be(b.DescribeView(targetB, StandardView.Right));
    }

    [Fact]
    public async Task VistaIso_SaiSemErro()
    {
        var (service, target) = await PrepareAsync();

        string text = service.DescribeView(target, StandardView.Isometric);
        output.WriteLine(text);

        text.Should().StartWith($"# Vista Isometric — {Placa}");
        text.Should().NotContain("## Erro");
        text.Should().Contain("## Visíveis — ");
    }

    [Fact]
    public async Task Cotas_SaoOMesmoTextoDoCopiarDaFolha()
    {
        var (service, target) = await PrepareAsync();
        var placa = (PartInstance)target.Feature;

        string text = service.DescribeDimensions(target, StandardView.Top);

        var sheet = DraftingAnalyzer.Analyze(DraftingInput.ForIsolated(placa, target.Frame, target.WorldMesh.Positions, StandardView.Top)!)!;
        text.Should().Be(DraftingReport.Format(sheet, sheet.Dimensions, placa.Name, placa.PartTypeName, codeOrigin: target.CodeOrigin));
        text.Should().Contain("origem do código (0, 0, 0) na folha");
    }

    [Fact]
    public async Task CotasNaIso_ExplicaQueNaoHa()
    {
        var (service, target) = await PrepareAsync();

        service.DescribeDimensions(target, StandardView.Isometric).Should().Contain("não tem cotas");
    }

    [Fact]
    public async Task Caixa_TemReferencialDaPecaEMundo()
    {
        var (service, target) = await PrepareAsync();

        string text = service.DescribeBoundingBox(target);
        output.WriteLine(text);

        text.Should().Contain("- referencial da peça: min (0, 0, 0)");
        text.Should().Contain("- mundo: min (");
    }

    [Fact]
    public async Task AlvoInexistente_DevolveAListaDeAlvos()
    {
        var features = await Scene.Value;

        var resolved = TargetResolver.Resolve(features, "nao_existe");

        resolved.Feature.Should().BeNull();
        resolved.Error.Should().Contain("Nenhuma feature de topo se chama 'nao_existe'").And.Contain(Placa);
    }

    [Fact]
    public async Task AlvoPeloTipo_SoQuandoHaUmaInstancia()
    {
        var features = await Scene.Value;

        // Há duas placas (a e b) do mesmo tipo: o tipo sozinho é ambíguo.
        TargetResolver.Resolve(features, "PlacaSuporteCarrinhoY").Error.Should().Contain("instâncias");
    }

    [Fact]
    public async Task ListaDeAlvos_TemAsPlacasComCaixaNoMundo()
    {
        var features = await Scene.Value;
        var service = new SceneInspectionService(new OpenCascadeGeometryKernel());

        string text = service.ListTargets(features);
        output.WriteLine(text);

        text.Should().Contain($"- {Placa} (PlacaSuporteCarrinhoY): mundo min (");
    }
}
