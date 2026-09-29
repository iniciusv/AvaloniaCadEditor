using System;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>Fase 3: ir para a definição (F12), referências (Shift+F12) e renomear (F2) entre arquivos.</summary>
public class CodeNavigationTests
{
    private const string ChapaFile = """
        public sealed record Chapa(double Largura, double Comprimento = 40, double Espessura = 5) : Part
        {
            protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura);
        }
        """;

    private const string SceneFile =
        "var a = Add(new Chapa(100, Espessura: 8));\n" +
        "var b = Add(a.Part with { Espessura = 4 }, at: Placement.At(0, 0, a.Part.Espessura));\n";

    private static ScriptDocument[] Docs() => new[]
    {
        new ScriptDocument("Cena.cs", SceneFile),
        new ScriptDocument("Pecas/Chapa.cs", ChapaFile.ReplaceLineEndings("\n"))
    };

    private static int At(string text, string word, int nth = 1)
    {
        int index = -1;
        for (int i = 0; i < nth; i++) index = text.IndexOf(word, index + 1, StringComparison.Ordinal);
        return index;
    }

    private static AvaloniaCadEditor.UI.ViewModels.CadViewModel CreateViewModel() =>
        TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new PureMathGeometryKernel());

    // ------------------------------------------------------------ Navegador

    [Fact]
    public void Referencias_ParametroDoRecord_IncluiArgumentoNomeadoWithEPropriedade()
    {
        var found = new RoslynCodeNavigator().FindReferences(Docs(), "Cena.cs", At(SceneFile, "Espessura"))!;

        found.SymbolName.Should().Be("Espessura");
        found.IsInScene.Should().BeTrue();
        found.Definitions.Should().ContainSingle().Which.FilePath.Should().Be("Pecas/Chapa.cs");
        found.Occurrences.Select(o => $"{o.FilePath}:{o.Line}").Should().BeEquivalentTo(
            "Cena.cs:1", "Cena.cs:2", "Cena.cs:2", "Pecas/Chapa.cs:1", "Pecas/Chapa.cs:3");
    }

    [Fact]
    public void Referencias_SimboloDaApi_NaoEDaCena()
    {
        var found = new RoslynCodeNavigator().FindReferences(Docs(), "Cena.cs", At(SceneFile, "Placement") + 2)!;

        found.SymbolName.Should().Be("Placement");
        found.IsInScene.Should().BeFalse();
        found.Definitions.Should().BeEmpty();
        found.Occurrences.Should().ContainSingle();
    }

    [Fact]
    public async Task Renomear_ParametroEmDoisArquivos_ContinuaCompilando()
    {
        var result = new RoslynCodeNavigator().Rename(Docs(), "Cena.cs", At(SceneFile, "Espessura"), "Grossura");

        result.Occurrences.Should().Be(5);
        result.ChangedDocuments["Cena.cs"].Should().Be(
            "var a = Add(new Chapa(100, Grossura: 8));\n" +
            "var b = Add(a.Part with { Grossura = 4 }, at: Placement.At(0, 0, a.Part.Grossura));\n");
        result.ChangedDocuments["Pecas/Chapa.cs"].Should().Contain("double Grossura = 5").And.Contain("Comprimento, Grossura)");

        var renamed = new[]
        {
            new ScriptDocument("Cena.cs", result.ChangedDocuments["Cena.cs"]),
            new ScriptDocument("Pecas/Chapa.cs", result.ChangedDocuments["Pecas/Chapa.cs"])
        };
        var evaluation = await new RoslynSceneEvaluator().EvaluateAsync(renamed);
        evaluation.Success.Should().BeTrue(because: evaluation.ErrorMessage);
    }

    [Fact]
    public void Renomear_TipoPeloConstrutor_RenomeiaDeclaracaoEUsos()
    {
        var result = new RoslynCodeNavigator().Rename(Docs(), "Cena.cs", At(SceneFile, "Chapa"), "Placa");

        result.ChangedDocuments["Cena.cs"].Should().StartWith("var a = Add(new Placa(100, Espessura: 8));");
        result.ChangedDocuments["Pecas/Chapa.cs"].Should().StartWith("public sealed record Placa(");
    }

    [Fact]
    public void Renomear_ConflitoOuApi_ERecusadoSemAlterarNada()
    {
        var navigator = new RoslynCodeNavigator();

        Action conflito = () => navigator.Rename(Docs(), "Cena.cs", At(SceneFile, "a "), "b");
        Action api = () => navigator.Rename(Docs(), "Cena.cs", At(SceneFile, "Placement"), "Posicao");
        Action invalido = () => navigator.Rename(Docs(), "Cena.cs", At(SceneFile, "a "), "class");

        conflito.Should().Throw<InvalidOperationException>().WithMessage("*quebraria*");
        api.Should().Throw<InvalidOperationException>().WithMessage("*API*");
        invalido.Should().Throw<InvalidOperationException>().WithMessage("*não é um nome válido*");
    }

    // ------------------------------------------------------------ Editor e inspector

    [Fact]
    public async Task Editor_F2Renomeia_EF12AbreADefinicaoEmOutroArquivo()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        folder.Write("Pecas/Chapa.cs", ChapaFile);
        folder.Write("Cena.cs", SceneFile);
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        var editor = vm.ScriptEditor;

        editor.GoToDefinitionAt(At(SceneFile, "Chapa")).Should().BeTrue();
        editor.ActiveFile.Should().Be("Pecas/Chapa.cs");
        editor.RevealLine!.Line.Should().Be(1);

        editor.ActiveFile = "Cena.cs";
        editor.FindReferencesAt(At(SceneFile, "Espessura")).Should().Be(5);
        editor.References.Should().HaveCount(5);

        editor.BeginRenameAt(At(SceneFile, "Espessura")).Should().BeTrue();
        editor.RenameText = "Grossura";
        (await editor.ConfirmRenameAsync()).Should().BeNull();

        editor.IsRenaming.Should().BeFalse();
        editor.HasErrors.Should().BeFalse(editor.ErrorSummary);
        System.IO.File.ReadAllText(folder.FullPath("Pecas/Chapa.cs")).Should().Contain("double Grossura = 5");
        editor.GeneratedCSharpCode.Should().Contain("a.Part.Grossura");
    }

    [Fact]
    public async Task Inspector_EditarOrigemDaPeca_ReescreveOAt()
    {
        var vm = CreateViewModel();
        vm.ScriptEditor.GeneratedCSharpCode = "var a = Add(new Chapa(100));\n\n" + ChapaFile;
        await vm.ScriptEditor.RunCodeAsync();
        vm.SelectionService.SelectFeature(vm.Sketch.Features.OfType<PartInstance>().Single());

        vm.PropertyInspector.CenterX = 50;

        vm.ScriptEditor.GeneratedCSharpCode.Should().StartWith("var a = Add(new Chapa(100), at: Placement.At(50, 0, 0));");
        vm.SelectionService.SelectedObject.Should().BeAssignableTo<PartInstance>()
            .Which.Placement.Translation.X.Should().Be(50);
    }
}
