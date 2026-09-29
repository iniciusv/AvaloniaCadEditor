using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>O editor abre a pasta da cena, grava no disco e reage a edições externas (ex.: uma IA).</summary>
public class SceneProjectViewModelTests
{
    private static AvaloniaCadEditor.UI.ViewModels.CadViewModel CreateViewModel() =>
        TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new PureMathGeometryKernel());

    [Fact]
    public async Task OpenProject_CarregaArquivosEExecutaACena()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        var vm = CreateViewModel();

        await vm.ScriptEditor.OpenProjectAsync(folder.Root);

        vm.ScriptEditor.IsProjectOpen.Should().BeTrue();
        vm.ScriptEditor.ProjectFiles.Should().Equal("Cena.cs", "Pecas/Medidas.cs");
        vm.ScriptEditor.ActiveFile.Should().Be("Cena.cs");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("AddFeature(base_1)");
        vm.Sketch.Features.Should().ContainSingle(f => f.Name == "base_1");
    }

    [Fact]
    public async Task TrocarDeArquivo_PreservaEdicoesEmMemoriaEGravaAoExecutar()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);

        vm.ScriptEditor.ActiveFile = "Pecas/Medidas.cs";
        vm.ScriptEditor.GeneratedCSharpCode = vm.ScriptEditor.GeneratedCSharpCode.Replace("= 10;", "= 25;");
        vm.ScriptEditor.ActiveFile = "Cena.cs";
        await vm.ScriptEditor.RunCodeAsync();

        File.ReadAllText(folder.FullPath("Pecas/Medidas.cs")).Should().Contain("= 25;");
        vm.Sketch.Features.OfType<BoxFeature>().Single().Height.Should().Be(25);
    }

    [Fact]
    public async Task EdicaoExterna_RecarregaEReexecuta()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);

        // Simula uma IA externa criando uma peça nova e usando-a na cena.
        folder.Write("Pecas/Pino.cs",
            "public static class Pino\n{\n" +
            "    public static void Em(double x) =>\n" +
            "        AddFeature(new CylinderFeature(new Point3D(x, 0, 0), new Vector3D(0, 0, 1), 2, 20) { Name = \"pino\" });\n" +
            "}\n");
        File.AppendAllText(folder.FullPath("Cena.cs"), "Pino.Em(30);\n");

        await vm.ScriptEditor.ReloadFromDiskAsync();

        vm.ScriptEditor.ProjectFiles.Should().Contain("Pecas/Pino.cs");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("Pino.Em(30);", "o arquivo ativo mudou no disco");
        vm.Sketch.Features.Select(f => f.Name).Should().BeEquivalentTo("base_1", "pino");

        // Selecionar a feature leva o editor ao arquivo onde ela foi registrada.
        vm.ScriptEditor.RevealFeature(vm.Sketch.Features.Single(f => f.Name == "pino"));
        vm.ScriptEditor.ActiveFile.Should().Be("Pecas/Pino.cs");
    }

    [Fact]
    public async Task CodigoGeradoPorFerramenta_VaiParaOArquivoDeEntrada()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        vm.ScriptEditor.ActiveFile = "Pecas/Medidas.cs";

        vm.ScriptEditor.AppendCode("var box_2 = new BoxFeature(baseCorner: new Point3D(100, 0, 0), width: 5, length: 5, height: 5) { Name = \"box_2\" };\nAddFeature(box_2);\n");
        await vm.ScriptEditor.RunCodeAsync();

        vm.ScriptEditor.ActiveFile.Should().Be("Cena.cs");
        File.ReadAllText(folder.FullPath("Cena.cs")).Should().Contain("box_2");
        File.ReadAllText(folder.FullPath("Pecas/Medidas.cs")).Should().NotContain("box_2");
        vm.Sketch.Features.Should().HaveCount(2);
    }
}
