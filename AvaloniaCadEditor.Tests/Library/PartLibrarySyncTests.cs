using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Ai.Context;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Library;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Scenes;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Library;

/// <summary>Etapa 2: comparar cena e biblioteca, enviar, atualizar e citar no chat.</summary>
public class PartLibrarySyncTests
{
    private const string Espacador = """
        /// <summary>Espaçador cilíndrico.</summary>
        public sealed record Espacador(double Altura = 5) : Part
        {
            protected override IFeature Build() =>
                new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, 4, Altura) { Name = "corpo" };
        }
        """;

    private const string Suporte = """
        /// <summary>Suporte sobre um espaçador.</summary>
        public sealed record Suporte(double Largura = 30) : Part
        {
            protected override IFeature Build()
            {
                var base_suporte = new BoxFeature(Point3D.Origin, Largura, 20, 4) { Name = "base_suporte" };
                var espacador = new Espacador(6).Instance("espacador", at: Placement.At(10, 10, 4));
                return BooleanFeature.CreateUnion("suporte", base_suporte, espacador);
            }
        }
        """;

    private const string Entry = "AddFeature(new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = \"base_1\" });\n";

    private static List<ScriptDocument> Scene(params (string Path, string Text)[] files) =>
        new[] { new ScriptDocument("Cena.cs", Entry) }.Concat(files.Select(f => new ScriptDocument(f.Path, f.Text))).ToList();

    private static List<ScriptDocument> Library(params (string Path, string Text)[] files) =>
        files.Select(f => new ScriptDocument(f.Path, f.Text)).ToList();

    // ---------------------------------------------------------------- Comparar

    [Fact]
    public void Comparar_DizSeAPecaEstaSoNumLado_IgualOuAlterada()
    {
        var scene = Scene(("Pecas/Espacador.cs", Espacador.Replace("\r\n", "\n")), ("Pecas/Suporte.cs", Suporte.Replace("30", "35")));
        var library = Library(("Fixacao/Espacador.cs", Espacador.Replace("\r\n", "\n").Replace("\n", "\r\n")), ("Fixacao/Suporte.cs", Suporte),
            ("Perfis/Outra.cs", Espacador.Replace("Espacador", "Outra")));

        var states = PartLibrarySync.Compare(scene, library);

        states["Espacador"].Should().Be(PartLibraryState.Same, "só a quebra de linha muda");
        states["Suporte"].Should().Be(PartLibraryState.Altered);
        states["Outra"].Should().Be(PartLibraryState.LibraryOnly);

        PartLibrarySync.Compare(Scene(("Pecas/Espacador.cs", Espacador)), Library())["Espacador"].Should().Be(PartLibraryState.SceneOnly);
    }

    // ---------------------------------------------------------------- Enviar

    [Fact]
    public void Enviar_PecaNova_VaiComADependenciaParaAPastaDoProjeto()
    {
        var scene = Scene(("Pecas/Espacador.cs", Espacador), ("Pecas/Suporte.cs", Suporte));

        var send = PartLibrarySync.PlanSend(scene, Library(), "Suporte", "Projetos/Impressora", "Cena.cs");

        send.Refusal.Should().BeNull();
        send.Writes.Select(w => w.FilePath).Should().Equal("Projetos/Impressora/Suporte.cs", "Projetos/Impressora/Espacador.cs");
        send.Writes[0].Text.Should().Be(Suporte);
    }

    [Fact]
    public void Enviar_PecaAlterada_SubstituiOArquivoDaBiblioteca_MasNaoADependenciaDiferente()
    {
        string suporteNovo = Suporte.Replace("30", "35");
        string espacadorNovo = Espacador.Replace("4, Altura", "4.5, Altura");
        var scene = Scene(("Pecas/Espacador.cs", espacadorNovo), ("Pecas/Suporte.cs", suporteNovo));
        var library = Library(("Fixacao/Suporte.cs", Suporte), ("Fixacao/Espacador.cs", Espacador));

        var send = PartLibrarySync.PlanSend(scene, library, "Suporte", "Projetos/X", "Cena.cs");

        send.Refusal.Should().BeNull();
        send.Writes.Should().ContainSingle().Which.Should().Be(new ScriptDocument("Fixacao/Suporte.cs", suporteNovo),
            "a peça vai para o arquivo que já a declara na biblioteca, não para a pasta do projeto");
        send.Warnings.Should().ContainSingle().Which.Should().Contain("Espacador").And.Contain("ficou o da biblioteca");
    }

    [Fact]
    public void Enviar_PecaQueDependeDeTipoDoArquivoDeEntrada_Recusa()
    {
        var scene = new List<ScriptDocument>
        {
            new("Cena.cs", Entry + "\npublic static class Medida { public const double X = 6; }\n"),
            new("Pecas/Espacador.cs", Espacador.Replace("4, Altura", "Medida.X, Altura")),
        };

        var send = PartLibrarySync.PlanSend(scene, Library(), "Espacador", "Projetos/X", "Cena.cs");

        send.Refusal.Should().Contain("arquivo de entrada");
    }

    [Fact]
    public void Enviar_QuandoOArquivoDaBibliotecaTemOutrosTipos_Recusa()
    {
        var library = Library(("Fixacao/Espacador.cs", Espacador + "\npublic enum Folga { Justa, Livre }\n"));

        var send = PartLibrarySync.PlanSend(Scene(("Pecas/Espacador.cs", Espacador.Replace("5", "6"))), library, "Espacador", "P", "Cena.cs");

        send.Refusal.Should().Contain("Folga").And.Contain("apagaria");
    }

    // ---------------------------------------------------------------- Atualizar

    [Fact]
    public void Atualizar_TrocaOTextoNoCaminhoDaCena_ECopiaADependenciaQueFalta()
    {
        string suporteDaCena = Suporte.Replace("new Espacador(6)", "new Espacador(7)");
        var scene = Scene(("Pecas/Espacador.cs", Espacador), ("Pecas/MeuSuporte.cs", suporteDaCena));
        string suporteDaBiblioteca = Suporte.Replace("Espacador", "Bucha");
        var library = Library(("Fixacao/Suporte.cs", suporteDaBiblioteca), ("Fixacao/Bucha.cs", Espacador.Replace("Espacador", "Bucha")));

        var update = PartLibrarySync.PlanUpdate(scene, library, "Suporte");

        update.Refusal.Should().BeNull();
        update.NewFiles.Select(f => f.FilePath).Should().Equal("Pecas/MeuSuporte.cs", "Pecas/Bucha.cs");
        update.NewFiles[0].Text.Should().Be(suporteDaBiblioteca);
    }

    // ---------------------------------------------------------------- Citar no chat

    [Fact]
    public void Citar_APecaEntraNoContextoNoCaminhoDaCena_ComoBiblioteca()
    {
        var scene = Scene();
        var library = Library(("Fixacao/Suporte.cs", Suporte), ("Fixacao/Espacador.cs", Espacador));
        var cited = PartLibraryCitation.ContextDocuments(library, scene, new[] { "Suporte" });

        cited.Select(d => d.FilePath).Should().Equal("Pecas/Suporte.cs", "Pecas/Espacador.cs");

        var request = new AiContextRequest(scene, "Cena.cs", null, new List<AvaloniaCadEditor.Domain.Features.IFeature>(), LibraryDocuments: cited);
        var context = new AiContextBuilder().Build(request);

        new AiContextBuilder().ResolveScope(request).Files
            .Should().Contain(f => f.FilePath == "Pecas/Suporte.cs" && f.Reason == AiContextReason.Library);
        context.ProjectDocuments!.Select(d => d.FilePath).Should().Contain(new[] { "Pecas/Suporte.cs", "Pecas/Espacador.cs" },
            "a validação compila a resposta com as peças citadas");
    }

    [Fact]
    public void Citar_AoAplicar_CopiaSoOQueARespostaUsa_ENadaQueElaMesmaDeclare()
    {
        var scene = Scene();
        var library = Library(("Fixacao/Suporte.cs", Suporte), ("Fixacao/Espacador.cs", Espacador));
        var cited = new[] { "Suporte" };

        var usa = new[] { new ScriptDocument("Cena.cs", Entry + "Add(new Suporte(), at: Placement.At(0, 0, 0));\n") };
        PartLibraryCitation.CompleteChanges(library, scene, usa, cited).Select(c => c.FilePath)
            .Should().Equal("Pecas/Suporte.cs", "Pecas/Espacador.cs", "Cena.cs");

        var naoUsa = new[] { new ScriptDocument("Cena.cs", Entry + "// nada\n") };
        PartLibraryCitation.CompleteChanges(library, scene, naoUsa, cited).Should().Equal(naoUsa);

        var reescreve = new[]
        {
            new ScriptDocument("Pecas/Suporte.cs", Suporte.Replace("30", "40")),
            new ScriptDocument("Cena.cs", Entry + "Add(new Suporte(), at: Placement.At(0, 0, 0));\n"),
        };
        PartLibraryCitation.CompleteChanges(library, scene, reescreve, cited).Select(c => c.FilePath)
            .Should().Equal("Pecas/Espacador.cs", "Pecas/Suporte.cs", "Cena.cs");
    }

    // ---------------------------------------------------------------- Pela aba

    [Fact]
    public async Task Aba_EnviarMarcaIgual_EditarMarcaAlterada_EAtualizarVoltaComCtrlZ()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        folder.Write("Pecas/Espacador.cs", Espacador);
        var vm = TestUtils.CreateCadViewModel(new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object, null, new PureMathGeometryKernel());
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        var store = new MemoryStore();
        var library = new LibraryViewModel(vm.ScriptEditor, store, uiInvoker: a => a());

        var daCena = library.Items.Single(i => i.Name == "Espacador");
        daCena.State.Should().Be(PartLibraryState.SceneOnly);
        daCena.CanSendToLibrary.Should().BeTrue();

        await library.SendToLibraryAsync("Espacador");

        store.Files.Keys.Should().Equal("Projetos/CenaTeste/Espacador.cs");
        library.Items.Should().OnlyContain(i => i.Name != "Espacador" || i.State == PartLibraryState.Same);
        library.Items.Single(i => i.Name == "Espacador" && i.Item.Source == PartLibrarySource.Library).Tag.Should().Be("na cena");

        // A biblioteca muda por fora (outro projeto, git pull): a cópia da cena fica "alterada".
        store.Files["Projetos/CenaTeste/Espacador.cs"] = Espacador.Replace("Altura = 5", "Altura = 8");
        library.Refresh();
        var alterada = library.Items.Single(i => i.Name == "Espacador" && i.Item.Source == PartLibrarySource.Scene);
        alterada.Tag.Should().Be("alterada");
        alterada.CanUpdateFromLibrary.Should().BeTrue();

        await library.UpdateFromLibraryAsync("Espacador");
        vm.ScriptEditor.GetSceneDocuments().Single(d => d.FilePath == "Pecas/Espacador.cs").Text.Should().Contain("Altura = 8");
        library.Items.Single(i => i.Name == "Espacador" && i.Item.Source == PartLibrarySource.Scene).State.Should().Be(PartLibraryState.Same);

        vm.ScriptEditor.Undo();
        vm.ScriptEditor.GetSceneDocuments().Single(d => d.FilePath == "Pecas/Espacador.cs").Text.Should().Contain("Altura = 5");
    }

    private sealed class MemoryStore : IPartLibraryStore
    {
        public SortedDictionary<string, string> Files { get; } = new();
        public string RootPath { get; private set; } = "C:/Biblioteca";
        public IReadOnlyList<ScriptDocument> ReadDocuments() => Files.Select(kv => new ScriptDocument(kv.Key, kv.Value)).ToList();
        public void Write(string relativePath, string text) => Files[relativePath] = text;
        public void SetRootPath(string folder) => RootPath = folder;
    }
}
