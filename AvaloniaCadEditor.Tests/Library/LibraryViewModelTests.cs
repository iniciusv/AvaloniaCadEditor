using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Library;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Scenes;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Library;

/// <summary>A aba Biblioteca: lista, busca, filtro, detalhe e Inserir, que copia a peça para a cena.</summary>
public class LibraryViewModelTests
{
    private const string Trilho = """
        /// <summary>Trilho de guia linear MGN9 (9 × 6,5 mm). Só o comprimento varia.</summary>
        public sealed record TrilhoTeste(double Comprimento = 200) : Part
        {
            protected override IFeature Build() => new BoxFeature(Point3D.Origin, 9, Comprimento, 6.5) { Name = "trilho" };
        }
        """;

    private const string Carrinho = """
        /// <summary>Carrinho que anda sobre o TrilhoTeste.</summary>
        public sealed record CarrinhoTeste(double Largura = 20) : Part
        {
            public static CarrinhoTeste Curto => new(Largura: 15);

            protected override IFeature Build()
            {
                var corpo = new BoxFeature(Point3D.Origin, Largura, 28, 10) { Name = "corpo" };
                var trilho_guia = new TrilhoTeste(30).Instance("trilho_guia");
                return BooleanFeature.CreateDifference("carrinho", corpo, trilho_guia);
            }
        }
        """;

    private static CadViewModel CreateViewModel() =>
        TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new PureMathGeometryKernel());

    private static (LibraryViewModel Library, MemoryStore Store) CreateLibrary(CadViewModel vm)
    {
        var store = new MemoryStore(
            new ScriptDocument("Guias lineares/TrilhoTeste.cs", Trilho),
            new ScriptDocument("Guias lineares/CarrinhoTeste.cs", Carrinho));
        return (new LibraryViewModel(vm.ScriptEditor, store, uiInvoker: a => a()), store);
    }

    [Fact]
    public async Task Lista_AgrupaPorOrigemECategoria_EBuscaFiltra()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        var (library, _) = CreateLibrary(vm);

        library.Rows.OfType<LibraryCategoryRow>().Select(r => r.Title).Should().Equal("Guias lineares");
        library.Rows.OfType<LibraryItemViewModel>().Select(i => i.Name).Should().Equal("CarrinhoTeste", "TrilhoTeste");
        library.Selected!.Name.Should().Be("CarrinhoTeste", "o primeiro item já vem escolhido");

        library.SearchText = "mgn9";
        library.Rows.OfType<LibraryItemViewModel>().Select(i => i.Name).Should().Equal("TrilhoTeste");

        library.SearchText = "nada disso";
        library.Rows.Should().BeEmpty();
        library.EmptyMessage.Should().Contain("Nenhuma peça");

        library.SearchText = string.Empty;
        library.IsFilterScene = true;
        library.Rows.Should().BeEmpty("a cena do modelo não declara peças");
        library.EmptyMessage.Should().Contain("A cena não declara peças");
    }

    [Fact]
    public async Task Detalhe_ModeloProntoEParametroMudamOCodigo()
    {
        var vm = CreateViewModel();
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        var (library, _) = CreateLibrary(vm);
        var carrinho = library.Selected!;

        carrinho.CodePreview.Should().Be("// copia Guias lineares/CarrinhoTeste.cs para Pecas/CarrinhoTeste.cs\nAdd(new CarrinhoTeste(), at: Placement.At(0, 0, 0));");

        carrinho.ChoosePresetCommand.Execute("Curto");
        carrinho.Parameters.Single().Value.Should().Be("15");
        carrinho.PartExpression.Should().Be("CarrinhoTeste.Curto");
        carrinho.Presets.Single().IsChosen.Should().BeTrue();

        carrinho.Parameters.Single().Value = "18";
        carrinho.PartExpression.Should().Be("new CarrinhoTeste(Largura: 18)");

        carrinho.Parameters.Single().Value = "x";
        carrinho.ValidationError.Should().Contain("não é um número");
    }

    [Fact]
    public async Task Inserir_DaBiblioteca_CopiaAPecaEADependenciaParaACena_ECtrlZDesfazTudo()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        var (library, _) = CreateLibrary(vm);
        string entryBefore = File.ReadAllText(folder.FullPath("Cena.cs"));

        library.Selected!.Name.Should().Be("CarrinhoTeste");
        library.InsertCommand.Execute(null);
        await WaitUntil(() => File.Exists(folder.FullPath("Pecas/TrilhoTeste.cs")) && vm.Sketch.Features.OfType<PartInstance>().Any());

        File.ReadAllText(folder.FullPath("Pecas/CarrinhoTeste.cs")).Should().Be(Carrinho);
        File.ReadAllText(folder.FullPath("Pecas/TrilhoTeste.cs")).Should().Be(Trilho, "a dependência vai junto");
        File.ReadAllText(folder.FullPath("Cena.cs")).Should().Contain("Add(new CarrinhoTeste(), at: Placement.At(");
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        vm.StatusText.Should().Contain("copiado da biblioteca").And.Contain("Pecas/TrilhoTeste.cs");

        // A peça agora é da cena: aparece em "Nesta cena", e a da biblioteca ganha a marca "na cena".
        library.Items.Should().Contain(i => i.Item.Source == PartLibrarySource.Scene && i.Name == "CarrinhoTeste");
        library.Items.Single(i => i.Item.Source == PartLibrarySource.Library && i.Name == "CarrinhoTeste").Tag.Should().Be("na cena");

        vm.ScriptEditor.Undo();
        await WaitUntil(() => !vm.Sketch.Features.OfType<PartInstance>().Any());
        vm.ScriptEditor.GeneratedCSharpCode.TrimEnd().Should().Be(entryBefore.TrimEnd());
        vm.ScriptEditor.GetSceneDocuments().Single(d => d.FilePath == "Pecas/CarrinhoTeste.cs").Text.Should().BeEmpty();

        // Inserir de novo depois do desfazer ocupa os arquivos vazios que sobraram.
        library.InsertCommand.Execute(null);
        await WaitUntil(() => vm.Sketch.Features.OfType<PartInstance>().Any());
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
    }

    [Fact]
    public async Task Inserir_DaBibliotecaSemCenaAberta_PedeUmaCena()
    {
        var vm = CreateViewModel();
        var (library, _) = CreateLibrary(vm);

        library.InsertCommand.Execute(null);
        await WaitUntil(() => vm.StatusText.Contains("Abra ou crie uma cena"));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("A condição não ficou verdadeira em 30 s.");
            await Task.Delay(20);
        }
    }

    private sealed class MemoryStore : IPartLibraryStore
    {
        private readonly List<ScriptDocument> _documents;
        public MemoryStore(params ScriptDocument[] documents) => _documents = documents.ToList();
        public string RootPath { get; private set; } = "C:/Biblioteca";
        public IReadOnlyList<ScriptDocument> ReadDocuments() => _documents;
        public void Write(string relativePath, string text)
        {
            _documents.RemoveAll(d => d.FilePath == relativePath);
            _documents.Add(new ScriptDocument(relativePath, text));
        }
        public void SetRootPath(string folder) => RootPath = folder;
    }
}
