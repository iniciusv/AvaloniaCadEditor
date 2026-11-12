using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.EditContext;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Library;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using AvaloniaCadEditor.Tests.Scenes;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Library;

/// <summary>
/// Vazios (<c>Part.IsCutter</c>): a biblioteca os marca e oferece "Furar a peça aberta", em que o clique numa face
/// dá o ponto e a direção, já nas coordenadas do <c>Build()</c> da peça.
/// </summary>
public class PartLibraryDrillTests
{
    private const string SceneFile = ScriptDocument.DefaultSceneFile;

    /// <summary>Bloco 40 × 30 × 10 com a origem num canto.</summary>
    private const string Bloco = """
        public sealed record Bloco(double Largura = 40) : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(Point3D.Origin, Largura, 30, 10) { Name = "corpo" };
                return corpo;
            }
        }
        """;

    /// <summary>Girado 90° em Z e erguido: o ponto local (x, y, z) vai para o mundo em (100 − y, 50 + x, 20 + z).</summary>
    private const string BlocoScene = """
        var bloco = Add(new Bloco(), at: Placement.At(100, 50, 20, rotationZDegrees: 90), name: "bloco");
        """;

    private const string FuroTeste = """
        /// <summary>Furo de teste, para subtrair.</summary>
        public sealed record FuroTeste(Point3D Face = default, Vector3D? Direcao = null, double Diametro = 3) : Part
        {
            public static FuroTeste Largo => new(Diametro: 5);

            public override bool IsCutter => true;

            protected override IFeature Build()
            {
                var direcao = (Direcao ?? new Vector3D(0, 0, -1)).Normalize();
                var furo = new CylinderFeature(Face - direcao, direcao, Diametro / 2, 6) { Name = "furo" };
                return furo;
            }
        }
        """;

    private static PartLibraryItem Furo() =>
        PartLibraryCatalog.Read(new[] { new ScriptDocument("Furos/FuroTeste.cs", FuroTeste) }, PartLibrarySource.Library).Single();

    // ---------------------------------------------------------------- Catálogo

    [Theory]
    [InlineData("public override bool IsCutter => true;", true)]
    [InlineData("public override bool IsCutter { get { return true; } }", true)]
    [InlineData("public override bool IsCutter { get => true; }", true)]
    [InlineData("public override bool IsCutter => false;", false)]
    [InlineData("public bool IsCutter => true;", false)]
    [InlineData("", false)]
    public void Catalogo_LeAMarcaDeVazioPelaSintaxe(string member, bool expected)
    {
        string text = $$"""
            public sealed record X(double A = 1) : Part
            {
                {{member}}
                protected override IFeature Build() => new BoxFeature(Point3D.Origin, A, A, A) { Name = "x" };
            }
            """;
        PartLibraryCatalog.Read(new[] { new ScriptDocument("X.cs", text) }, PartLibrarySource.Library)
            .Single().IsCutter.Should().Be(expected);
    }

    [Fact]
    public void Catalogo_DaCenaDeExemplo_MarcaOsDoisVazios_EOFuroRebaixadoTemM3M4M5()
    {
        var documents = Directory.GetFiles(Path.Combine(AiCoreFolder(), "Pecas"), "*.cs")
            .Select(f => new ScriptDocument("Pecas/" + Path.GetFileName(f), File.ReadAllText(f)))
            .ToList();

        var items = PartLibraryCatalog.Read(documents, PartLibrarySource.Scene);

        items.Where(i => i.IsCutter).Select(i => i.Name).Should().BeEquivalentTo("FuroPorca", "FuroRebaixado");
        var rebaixado = items.Single(i => i.Name == "FuroRebaixado");
        rebaixado.Presets.Select(p => p.Name).Should().Equal("M3", "M4", "M5");
        rebaixado.Presets[0].Arguments["DiametroFuro"].Should().Be("3.4");
        PartLibraryDrill.Refusal(rebaixado).Should().BeNull();
        PartLibraryDrill.PointParameter(rebaixado)!.Name.Should().Be("Face");
        PartLibraryDrill.DirectionParameter(items.Single(i => i.Name == "FuroPorca"))!.Name.Should().Be("Direcao");
    }

    [Fact]
    public void PecaComum_NaoFura()
    {
        var item = PartLibraryCatalog.Read(new[] { new ScriptDocument("Bloco.cs", Bloco) }, PartLibrarySource.Library).Single();
        PartLibraryDrill.Refusal(item).Should().Contain("não é um vazio");
    }

    // ---------------------------------------------------------------- Ponto e direção do clique

    [Fact]
    public void Clique_NaFaceDeCimaDaPecaGirada_VaiParaAsCoordenadasLocais()
    {
        var context = new PartEditContext("bloco", "Bloco", Placement.At(100, 50, 20, rotationZDegrees: 90), 1);

        // Local (20, 10, 10), na face de cima.
        var (point, direction) = PartLibraryDrill.ToLocal(context, new Point3D(90, 70, 30), new Vector3D(0, 0, 1));
        point.Should().Be(new Point3D(20, 10, 10));
        direction.Should().Be(new Vector3D(0, 0, -1));

        // Local (40, 15, 5), na face x = 40: a normal do mundo (0, 1, 0) é +X local, e o furo entra em −X.
        (point, direction) = PartLibraryDrill.ToLocal(context, new Point3D(85, 90, 25), new Vector3D(0, 1, 0));
        point.Should().Be(new Point3D(40, 15, 5));
        direction.Should().Be(new Vector3D(-1, 0, 0));
    }

    [Fact]
    public void Trecho_TemOsValoresEscolhidos_ONomeLivre_ENuncaOModeloProntoPeloNome()
    {
        var item = Furo();
        var context = new PartEditContext("bloco", "Bloco", Placement.At(0, 0, 0), 1);
        var values = PartLibraryInsertion.InitialValues(item, "Largo");

        string snippet = PartLibraryDrill.Snippet(item, values, context, new Point3D(12.3456, 7, 10), new Vector3D(0, 0, 1),
            new[] { "furo_teste_1" });

        snippet.Should().StartWith(
            "var furo_teste_2 = new FuroTeste(Face: new Point3D(12.35, 7, 10), Direcao: new Vector3D(0, 0, -1), Diametro: 5).Instance(\"furo_teste_2\");\n");
        snippet.Should().Contain("AddFeature(furo_teste_2.Transformed(");
    }

    [Fact]
    public async Task Furar_PecaGirada_GravaOVazioNoBuildSemAt_EOCorteFicaNoLugar()
    {
        var documents = new[]
        {
            new ScriptDocument(SceneFile, BlocoScene),
            new ScriptDocument("Pecas/Bloco.cs", Bloco),
            new ScriptDocument("Pecas/FuroTeste.cs", FuroTeste)
        };
        var evaluated = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        evaluated.Success.Should().BeTrue(evaluated.ErrorMessage + string.Join("\n", evaluated.Diagnostics));
        var context = PartEditContext.For(evaluated.Features.Single(f => f.Name == "bloco"), evaluated.Features)!;

        string snippet = PartLibraryDrill.Snippet(Furo(), PartLibraryInsertion.InitialValues(Furo()), context,
            new Point3D(85, 90, 25), new Vector3D(0, 1, 0), PartLibraryDrill.NamesIn(documents));
        var insertion = await new PartSolidInserter(new RoslynSceneEvaluator(), new RoslynPartMerger())
            .InsertAsync(documents, SceneFile, snippet, context, PartMergeMode.SubtractAsTool);

        insertion.Refusal.Should().BeNull();
        insertion.Changes.Select(c => c.FilePath).Should().Equal("Pecas/Bloco.cs");
        string part = insertion.Changes.Single().Text;
        part.Should().Contain("var furo_teste_1 = new FuroTeste(Face: new Point3D(40, 15, 5), Direcao: new Vector3D(-1, 0, 0)).Instance(\"furo_teste_1\");");
        part.Should().NotContain("at:", "os parâmetros já são locais");
        part.Should().Contain("CreateDifference");

        var applied = documents.Select(d => insertion.Changes.FirstOrDefault(c => c.FilePath == d.FilePath) ?? d).ToList();
        var after = await new RoslynSceneEvaluator().EvaluateAsync(applied);
        after.Success.Should().BeTrue(after.ErrorMessage + string.Join("\n", after.Diagnostics));
        var definition = after.Features.OfType<PartInstance>().Single(i => i.Name == "bloco").Definition;
        definition.Should().BeOfType<BooleanFeature>()
            .Which.ToolFeatures.Should().ContainSingle(f => f.Name == "furo_teste_1");
    }

    // ---------------------------------------------------------------- Pela aba

    [Fact]
    public async Task Aba_FurarAPecaAberta_CopiaOVazio_GravaNaPeca_ECtrlZDesfazOsDois()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        folder.Write(SceneFile, BlocoScene);
        folder.Write("Pecas/Bloco.cs", Bloco);
        var vm = TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new PureMathGeometryKernel());
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        await WaitUntil(() => vm.Sketch.Features.Any(f => f.Name == "bloco"));

        var store = new MemoryStore(new ScriptDocument("Furos/FuroTeste.cs", FuroTeste));
        var library = new LibraryViewModel(vm.ScriptEditor, store, uiInvoker: a => a());
        string? prompt = null;
        Func<RaycastHit, Task>? onPicked = null;
        library.OpenPart = () => vm.EditContext;
        library.PickFace = (text, callback) => { prompt = text; onPicked = callback; };

        var furo = library.Items.Single(i => i.Name == "FuroTeste");
        furo.IsCutter.Should().BeTrue();
        furo.CanInsert.Should().BeFalse();
        furo.CodePreview.Should().Contain("FuroTeste(Face: ‹clique›, Direcao: ‹clique›)");
        library.SelectedRow = furo;

        // Sem peça aberta, não pede o clique.
        library.HasOpenPart.Should().BeFalse();
        library.DrillCommand.Execute(null);
        onPicked.Should().BeNull();
        vm.StatusText.Should().Contain("Abra (duplo clique)");

        vm.ActiveBooleanGroupName = "bloco";
        library.HasOpenPart.Should().BeTrue();
        library.DrillCommand.Execute(null);
        prompt.Should().Contain("Bloco");
        onPicked.Should().NotBeNull();

        string partBefore = File.ReadAllText(folder.FullPath("Pecas/Bloco.cs"));
        await onPicked!(new RaycastHit(1, new Point3D(90, 70, 30), new Vector3D(0, 0, 1)));

        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        vm.StatusText.Should().StartWith("✓ FuroTeste furo_teste_1 gravado em Bloco").And.Contain("Pecas/FuroTeste.cs");
        File.ReadAllText(folder.FullPath("Pecas/FuroTeste.cs")).Should().Be(FuroTeste);
        File.ReadAllText(folder.FullPath("Pecas/Bloco.cs"))
            .Should().Contain("new FuroTeste(Face: new Point3D(20, 10, 10), Direcao: new Vector3D(0, 0, -1)).Instance(\"furo_teste_1\")");

        vm.ScriptEditor.Undo();
        await WaitUntil(() => vm.ScriptEditor.GetSceneDocuments().Single(d => d.FilePath == "Pecas/Bloco.cs").Text == partBefore);
        vm.ScriptEditor.GetSceneDocuments().Single(d => d.FilePath == "Pecas/FuroTeste.cs").Text.Should().BeEmpty();
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

    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "AiCore");
    }
}
