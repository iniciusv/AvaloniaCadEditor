using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// O fluxo inteiro, pelas ViewModels: duplo-clique numa instância de peça, clique num furo vermelho,
/// e então o arquivo da peça aberto com a cadeia destacada e o inspector mostrando só o que afeta
/// aquele furo — editável no valor padrão do record.
/// </summary>
public class PartSolidSelectionTests
{
    private const string CantoneiraRecord = """
        public sealed record Cantoneira(
            double Espessura = 6,
            double Largura = 26.5,
            double AlturaFurosZ = 16.625,
            double DistanciaFuro1Y = 21.5,
            double DistanciaFuro2Y = 6.5,
            double DiametroFuro = 3.4) : Part
        {
            protected override IFeature Build()
            {
                var raioFuro = DiametroFuro / 2;
                var corpo = new BoxFeature(new Point3D(0, -Largura, 0), Espessura, Largura, 30) { Name = "corpo" };
                var furo_placa_1 = new CylinderFeature(new Point3D(-1, -DistanciaFuro1Y, AlturaFurosZ), Vector3D.UnitX, raioFuro, Espessura + 2) { Name = "furo_placa_1", IsTransientTool = true };
                var furo_placa_2 = new CylinderFeature(new Point3D(-1, -DistanciaFuro2Y, AlturaFurosZ), Vector3D.UnitX, raioFuro, Espessura + 2) { Name = "furo_placa_2", IsTransientTool = true };
                return BooleanFeature.CreateDifference("cantoneira", corpo, furo_placa_1, furo_placa_2);
            }
        }
        """;

    private const string SceneCode = """
        var cant_a = Add(new Cantoneira(), at: Placement.At(0, 0, 0), name: "cant_a");
        var cant_b = Add(new Cantoneira(DistanciaFuro2Y: 8), at: Placement.At(100, 0, 0), name: "cant_b");
        """;

    private static AvaloniaCadEditor.UI.ViewModels.CadViewModel CreateViewModel() =>
        TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new PureMathGeometryKernel());

    /// <summary>Abre uma cena de dois arquivos e devolve a VM já com o grupo de cant_a aberto.</summary>
    private static async Task<AvaloniaCadEditor.UI.ViewModels.CadViewModel> OpenSceneAsync(SceneTestFolder folder)
    {
        folder.CreateScene();
        folder.Write("Pecas/Cantoneira.cs", CantoneiraRecord);
        folder.Write("Cena.cs", SceneCode);

        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);

        // Duplo-clique na instância: mostra os sólidos-ferramenta no lugar dela.
        vm.ActiveBooleanGroupName = "cant_a";
        return vm;
    }

    private static IFeature Tool(AvaloniaCadEditor.UI.ViewModels.CadViewModel vm, string name) =>
        vm.ActiveToolFeatures.Single(f => f is TransformedFeature w && w.Source.Name == name);

    [Fact]
    public async Task ClicarNoFuro_AbreOArquivoDaPeca_EDestacaACadeia()
    {
        using var folder = new SceneTestFolder();
        var vm = await OpenSceneAsync(folder);

        vm.RevealToolFeature(Tool(vm, "furo_placa_1"));

        vm.SelectedPartSolid.Should().NotBeNull();
        vm.SelectedPartSolid!.SolidName.Should().Be("furo_placa_1");
        vm.SelectedPartSolidOwner!.PartTypeName.Should().Be("Cantoneira");
        vm.ScriptEditor.ActiveFile.Should().Be("Pecas/Cantoneira.cs");

        // A declaração do furo e a de cada coisa que o alimenta, no arquivo aberto.
        string parte = vm.ScriptEditor.GeneratedCSharpCode;
        var destacado = vm.ScriptEditor.PartSolidSpans
            .Select(s => parte.Substring(s.Start, s.Length))
            .ToList();

        destacado.Should().Contain(t => t.StartsWith("var furo_placa_1 = new CylinderFeature"));
        destacado.Should().Contain("var raioFuro = DiametroFuro / 2;");
        destacado.Should().Contain("double DistanciaFuro1Y = 21.5");
        destacado.Should().Contain("double AlturaFurosZ = 16.625");

        // O que não alcança o furo fica sem destaque.
        destacado.Should().NotContain("double Largura = 26.5");
        destacado.Should().NotContain(t => t.Contains("DistanciaFuro2Y"));
    }

    [Fact]
    public async Task ClicarNoFuro_InspectorRanqueiaOsParametrosPorRelevancia()
    {
        using var folder = new SceneTestFolder();
        var vm = await OpenSceneAsync(folder);

        vm.RevealToolFeature(Tool(vm, "furo_placa_1"));

        var inspector = vm.PropertyInspector;
        inspector.TypeBadge.Should().Be("SÓLIDO · Cantoneira");
        inspector.Title.Should().Be("furo_placa_1");

        var hints = inspector.PartParameters.ToDictionary(p => p.Name, p => p.Hint);

        // Primeiro o que move só este furo...
        inspector.PartParameters[0].Name.Should().Be("DistanciaFuro1Y");
        hints["DistanciaFuro1Y"].Should().Be("move só furo_placa_1");

        // ...depois o que move este e outros...
        hints["AlturaFurosZ"].Should().Be("move também furo_placa_2");
        hints["DiametroFuro"].Should().Be("move também furo_placa_2");
        hints["Espessura"].Should().Be("move também corpo, furo_placa_2");

        // ...e por fim o que não o afeta, sem dica e no fim da lista.
        hints["Largura"].Should().BeNull();
        hints["DistanciaFuro2Y"].Should().BeNull();
        inspector.PartParameters.Select(p => p.Name).Last().Should().BeOneOf("Largura", "DistanciaFuro2Y");

        inspector.PartNote.Should().Contain("4 dos 6 parâmetros de Cantoneira afetam furo_placa_1");
        inspector.PartNote.Should().Contain("vale para todas as instâncias");
    }

    [Fact]
    public async Task EditarPeloInspector_GravaOPadraoNoRecord_EValeParaTodasAsInstancias()
    {
        using var folder = new SceneTestFolder();
        var vm = await OpenSceneAsync(folder);
        vm.RevealToolFeature(Tool(vm, "furo_placa_1"));

        double antesA = FuroY(vm, "cant_a");
        double antesB = FuroY(vm, "cant_b");

        var item = vm.PropertyInspector.PartParameters.Single(p => p.Name == "DistanciaFuro1Y");
        item.IsEditable.Should().BeTrue();
        item.Text = "19.5";
        await item.ApplyAsync();

        item.Error.Should().BeNull();
        // O padrão mudou no arquivo da PEÇA, não na chamada Add(...) da cena.
        folder.ReadPart().Should().Contain("double DistanciaFuro1Y = 19.5");
        folder.ReadScene().Should().NotContain("DistanciaFuro1Y");

        // E as duas instâncias andaram junto (2 mm em Y, no sinal do código: -DistanciaFuro1Y).
        FuroY(vm, "cant_a").Should().BeApproximately(antesA + 2, 1e-6);
        FuroY(vm, "cant_b").Should().BeApproximately(antesB + 2, 1e-6);

        // Depois do re-run a cena é outra: o furo continua selecionado e realçado.
        vm.SelectedPartSolid!.SolidName.Should().Be("furo_placa_1");
        vm.HighlightedToolId.Should().Be(Tool(vm, "furo_placa_1").Id);
    }

    [Fact]
    public async Task EditarUmParametroQueUmaInstanciaSobrepoe_Avisa()
    {
        using var folder = new SceneTestFolder();
        var vm = await OpenSceneAsync(folder);

        // cant_b passa DistanciaFuro2Y no Add(...), então ignora o padrão do record.
        vm.RevealToolFeature(Tool(vm, "furo_placa_2"));

        vm.PropertyInspector.PartNote.Should()
            .Contain("1 de 2 instâncias passam DistanciaFuro2Y na chamada Add(...); essas não mudam.");
    }

    [Fact]
    public async Task SairDoModoBooleano_LimpaODestaqueEOInspector()
    {
        using var folder = new SceneTestFolder();
        var vm = await OpenSceneAsync(folder);
        vm.RevealToolFeature(Tool(vm, "furo_placa_1"));
        vm.PropertyInspector.PartParameters.Should().NotBeEmpty();

        vm.ExitBooleanEditModeCommand.Execute(null);

        vm.SelectedPartSolid.Should().BeNull();
        vm.SelectedPartSolidOwner.Should().BeNull();
        vm.PropertyInspector.TypeBadge.Should().NotBe("SÓLIDO · Cantoneira");
    }

    [Fact]
    public async Task TrocarDeArquivoNoEditor_LimpaOsSpans_ParaNaoPintarOffsetsDoArquivoErrado()
    {
        using var folder = new SceneTestFolder();
        var vm = await OpenSceneAsync(folder);
        vm.RevealToolFeature(Tool(vm, "furo_placa_1"));
        vm.ScriptEditor.PartSolidSpans.Should().NotBeEmpty();

        vm.ScriptEditor.ActiveFile = "Cena.cs";

        vm.ScriptEditor.PartSolidSpans.Should().BeEmpty();
    }

    /// <summary>Y do centro do furo_placa_1 de uma instância, em coordenadas de mundo.</summary>
    private static double FuroY(AvaloniaCadEditor.UI.ViewModels.CadViewModel vm, string instanceName)
    {
        var instance = vm.Sketch.Features.OfType<PartInstance>().Single(i => i.Name == instanceName);
        var furo = (CylinderFeature)AvaloniaCadEditor.UI.ViewModels.CadViewModel
            .CollectToolFeatures(instance.Definition)
            .Single(f => f.Name == "furo_placa_1");
        return instance.ToWorld(furo.GlobalBaseCenter).Y;
    }
}

internal static class SceneTestFolderReads
{
    public static string ReadPart(this SceneTestFolder folder) =>
        System.IO.File.ReadAllText(folder.FullPath("Pecas/Cantoneira.cs"));

    public static string ReadScene(this SceneTestFolder folder) =>
        System.IO.File.ReadAllText(folder.FullPath("Cena.cs"));
}
