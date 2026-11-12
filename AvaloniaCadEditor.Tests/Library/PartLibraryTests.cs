using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Library;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Infrastructure.Scripting;
using AvaloniaCadEditor.Tests.Scenes;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Library;

/// <summary>Catálogo, importação com dependências e código da instrução da biblioteca de peças.</summary>
public class PartLibraryTests
{
    private const string Extrusao = """
        /// <summary>
        /// Perfil de extrusão de alumínio 2020 (20 × 20 mm), com furo central passante. Só o comprimento varia.
        /// </summary>
        public sealed record Extrusao2020(double Comprimento = 200, double DiametroFuroCentral = 6) : Part
        {
            protected override IFeature Build() =>
                new BoxFeature(Point3D.Origin, 20, 20, Comprimento) { Name = "perfil" };
        }
        """;

    private const string Rolamento = """
        /// <summary>Rolamento simplificado. Os modelos saem de <see cref="F623ZZ"/>.</summary>
        public sealed record Rolamento(double DiametroInterno = 3, double DiametroExterno = 10, double Largura = 4) : Part
        {
            /// <summary>F623ZZ: vai no parafuso M3.</summary>
            public static Rolamento F623ZZ => new(3, 10, Largura: 4.5);

            /// <summary>Um que não dá para ler.</summary>
            public static Rolamento Estranho => F623ZZ with { Largura = 6 };

            protected override IFeature Build() =>
                new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, DiametroExterno / 2, Largura) { Name = "anel" };
        }
        """;

    private const string Furo = """
        public enum Rosca { M3, M4 }

        /// <summary>Furo para parafuso, para ser subtraído de outra peça.</summary>
        public sealed record FuroSimples(double Profundidade, Rosca Rosca = Rosca.M3) : Part
        {
            public static double Diametro(Rosca r) => r == Rosca.M3 ? 3.4 : 4.5;

            protected override IFeature Build() =>
                new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, Diametro(Rosca) / 2, Profundidade) { Name = "furo" };
        }
        """;

    private const string Placa = """
        /// <summary>Placa com um furo: depende de FuroSimples (e do enum Rosca, declarado no mesmo arquivo).</summary>
        public sealed record PlacaFurada(double Espessura = 5) : Part
        {
            protected override IFeature Build()
            {
                var chapa = new BoxFeature(Point3D.Origin, 40, 40, Espessura) { Name = "chapa" };
                var furo_1 = new FuroSimples(Espessura + 2, Rosca.M3).Instance("furo_1", at: Placement.At(20, 20, -1));
                return BooleanFeature.CreateDifference("placa", chapa, furo_1);
            }
        }
        """;

    private static IReadOnlyList<ScriptDocument> Library() => new[]
    {
        new ScriptDocument("Perfis/Extrusao2020.cs", Extrusao),
        new ScriptDocument("Rolamentos e polias/Rolamento.cs", Rolamento),
        new ScriptDocument("Furos/FuroSimples.cs", Furo),
        new ScriptDocument("Projetos/Impressora 3D/PlacaFurada.cs", Placa),
    };

    // ---------------------------------------------------------------- Catálogo

    [Fact]
    public void Catalogo_LeCategoriaDescricaoParametrosEModelosProntosDoProprioCodigo()
    {
        var items = PartLibraryCatalog.Read(Library(), PartLibrarySource.Library);

        items.Select(i => i.Name).Should().BeEquivalentTo("Extrusao2020", "Rolamento", "FuroSimples", "PlacaFurada");

        var extrusao = items.Single(i => i.Name == "Extrusao2020");
        extrusao.Category.Should().Be("Perfis");
        extrusao.Summary.Should().StartWith("Perfil de extrusão de alumínio 2020 (20 × 20 mm)");
        extrusao.ShortSummary.Should().Be("Perfil de extrusão de alumínio 2020 (20 × 20 mm), com furo central passante.");
        extrusao.Parameters.Select(p => (p.Name, p.DefaultValue)).Should().Equal(("Comprimento", "200"), ("DiametroFuroCentral", "6"));

        items.Single(i => i.Name == "PlacaFurada").Category.Should().Be("Projetos/Impressora 3D");

        var rolamento = items.Single(i => i.Name == "Rolamento");
        rolamento.Summary.Should().Be("Rolamento simplificado. Os modelos saem de F623ZZ.", "o <see cref> vira o nome");
        rolamento.Presets.Select(p => p.Name).Should().Equal("F623ZZ", "Estranho");
        var f623 = rolamento.Presets[0];
        f623.Summary.Should().Be("F623ZZ: vai no parafuso M3.");
        f623.Arguments.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["DiametroInterno"] = "3", ["DiametroExterno"] = "10", ["Largura"] = "4.5"
        });
        rolamento.Presets[1].Arguments.Should().BeEmpty("um 'with' não é lido; o modelo só entra pelo nome");
    }

    [Fact]
    public void Catalogo_SobreAsPecasDaCenaDeExemplo_AchaOsModelosProntosDoRolamento()
    {
        var documents = Directory.GetFiles(Path.Combine(AiCoreFolder(), "Pecas"), "*.cs")
            .Select(f => new ScriptDocument("Pecas/" + Path.GetFileName(f), File.ReadAllText(f)))
            .ToList();

        var items = PartLibraryCatalog.Read(documents, PartLibrarySource.Scene);

        items.Should().Contain(i => i.Name == "Extrusao2020" && i.Summary.Length > 0);
        var rolamento = items.Single(i => i.Name == "Rolamento");
        rolamento.Presets.Select(p => p.Name).Should().Contain(new[] { "F623ZZ", "F686ZZ" });
        rolamento.Presets.Single(p => p.Name == "F623ZZ").Arguments["DiametroFlange"].Should().Be("11.5");
    }

    // ---------------------------------------------------------------- Importação

    [Fact]
    public void Importar_PecaComDependencia_CopiaOFechoParaPecas()
    {
        var import = PartLibraryImporter.Plan(Library(), SceneWith(), "PlacaFurada");

        import.Refusal.Should().BeNull();
        import.Warnings.Should().BeEmpty();
        import.NewFiles.Select(f => f.FilePath).Should().Equal("Pecas/PlacaFurada.cs", "Pecas/FuroSimples.cs");
        import.NewFiles[1].Text.Should().Be(Furo, "o arquivo vai como está, sem cabeçalho nem caminho para a biblioteca");
    }

    [Fact]
    public void Importar_PecaQueACenaJaTemIgual_NaoCopiaNada()
    {
        var import = PartLibraryImporter.Plan(Library(), SceneWith(("Pecas/FuroSimples.cs", Furo)), "PlacaFurada");

        import.Refusal.Should().BeNull();
        import.Warnings.Should().BeEmpty();
        import.NewFiles.Select(f => f.FilePath).Should().Equal("Pecas/PlacaFurada.cs");
    }

    [Fact]
    public void Importar_PecaQueACenaTemDiferente_FicaComADaCenaEAvisa()
    {
        string alterado = Furo.Replace("3.4", "3.5");
        var import = PartLibraryImporter.Plan(Library(), SceneWith(("Pecas/Furos.cs", alterado)), "PlacaFurada");

        import.Refusal.Should().BeNull();
        import.NewFiles.Select(f => f.FilePath).Should().Equal("Pecas/PlacaFurada.cs");
        import.Warnings.Should().ContainSingle().Which.Should().Contain("FuroSimples").And.Contain("ficou a versão da cena");
    }

    [Fact]
    public void Importar_ArquivoQueRepetiriaSoParteDosTipos_Recusa()
    {
        // A cena já declara Rosca, mas não FuroSimples: copiar o arquivo repetiria Rosca (CS0101).
        var import = PartLibraryImporter.Plan(Library(), SceneWith(("Pecas/Rosca.cs", "public enum Rosca { M3 }")), "FuroSimples");

        import.Refusal.Should().Contain("Rosca").And.Contain("repetiria");
        import.NewFiles.Should().BeEmpty();
    }

    [Fact]
    public void Importar_DestinoOcupadoPorOutroConteudo_RecusaSemSobrescrever()
    {
        var import = PartLibraryImporter.Plan(Library(), SceneWith(("Pecas/Extrusao2020.cs", "public static class Outra { }")), "Extrusao2020");

        import.Refusal.Should().Contain("Pecas/Extrusao2020.cs");
    }

    [Fact]
    public void Importar_DestinoVazio_SobraDeUmDesfazer_PodeSerOcupado()
    {
        var import = PartLibraryImporter.Plan(Library(), SceneWith(("Pecas/Extrusao2020.cs", "")), "Extrusao2020");

        import.Refusal.Should().BeNull();
        import.NewFiles.Should().ContainSingle(f => f.FilePath == "Pecas/Extrusao2020.cs");
    }

    [Fact]
    public void Importar_NomeRepetidoNaBiblioteca_Recusa()
    {
        var library = Library().Append(new ScriptDocument("Outros/Extrusao2020.cs", Extrusao)).ToList();

        var import = PartLibraryImporter.Plan(library, SceneWith(), "Extrusao2020");

        import.Refusal.Should().Contain("mais de um arquivo");
    }

    [Fact]
    public async Task Importar_ACenaComAPecaCopiada_PassaNoCheck()
    {
        using var folder = new SceneTestFolder();
        var project = folder.CreateScene();
        var scene = project.ReadDocuments();

        var import = PartLibraryImporter.Plan(Library(), scene, "PlacaFurada");
        import.Refusal.Should().BeNull();
        foreach (var file in import.NewFiles) folder.Write(file.FilePath, file.Text);

        var item = PartLibraryCatalog.Read(Library(), PartLibrarySource.Library).Single(i => i.Name == "PlacaFurada");
        string statement = PartLibraryInsertion.AddStatement(PartLibraryInsertion.PartExpression(item, PartLibraryInsertion.InitialValues(item)), 100);
        string entry = File.ReadAllText(folder.FullPath(SceneProject.DefaultEntryFile));
        folder.Write(SceneProject.DefaultEntryFile, PartSourceEditor.InsertTopLevelStatement(entry, statement));

        var report = await new SceneCheckService(new RoslynSceneEvaluator()).CheckAsync(project, checkGeometry: false);

        report.Errors.Should().BeEmpty(report.Format());
    }

    // ---------------------------------------------------------------- Código da instrução

    [Fact]
    public void Instrucao_SoOsParametrosDiferentesDoPadraoVaoNomeados()
    {
        var item = Item("Extrusao2020");
        var values = new Dictionary<string, string>(PartLibraryInsertion.InitialValues(item));

        PartLibraryInsertion.PartExpression(item, values).Should().Be("new Extrusao2020()");

        values["Comprimento"] = "350,5";
        PartLibraryInsertion.PartExpression(item, values).Should().Be("new Extrusao2020(Comprimento: 350.5)", "vírgula decimal vira ponto");
        PartLibraryInsertion.AddStatement("new Extrusao2020()", 12.34567).Should().Be("Add(new Extrusao2020(), at: Placement.At(12.346, 0, 0));");
    }

    [Fact]
    public void Instrucao_ModeloProntoIntocado_EntraPeloNome_EEditadoViraNew()
    {
        var item = Item("Rolamento");
        var values = new Dictionary<string, string>(PartLibraryInsertion.InitialValues(item, "F623ZZ"));
        values["Largura"].Should().Be("4.5");

        PartLibraryInsertion.PartExpression(item, values, "F623ZZ").Should().Be("Rolamento.F623ZZ");

        values["DiametroExterno"] = "11";
        PartLibraryInsertion.PartExpression(item, values, "F623ZZ").Should().Be("new Rolamento(DiametroExterno: 11, Largura: 4.5)");
    }

    [Fact]
    public void Instrucao_ObrigatorioSempreVai_EEnumAceitaSoOValor()
    {
        var item = Item("FuroSimples");
        var values = new Dictionary<string, string>(PartLibraryInsertion.InitialValues(item));
        values["Profundidade"].Should().Be("10", "sem padrão nem [Range], vale o valor neutro");

        values["Rosca"] = "M4";
        PartLibraryInsertion.PartExpression(item, values).Should().Be("new FuroSimples(Profundidade: 10, Rosca: Rosca.M4)");
    }

    [Fact]
    public void Validacao_RecusaNumeroInvalidoEValorVazio()
    {
        var comprimento = Item("Extrusao2020").Parameters[0];

        PartLibraryInsertion.Validate(comprimento, "12,5").Should().BeNull();
        PartLibraryInsertion.Validate(comprimento, "abc").Should().Contain("não é um número");
        PartLibraryInsertion.Validate(comprimento, " ").Should().Contain("falta o valor");
    }

    // ---------------------------------------------------------------- Miniatura

    [Fact]
    public void Miniatura_DeUmaCaixa_PintaOCentroEDeixaOsCantosTransparentes()
    {
        var rgba = ThumbnailRasterizer.Render(Box(10, 20, 5), 64);

        Alpha(rgba, 64, 32, 32).Should().Be(255);
        Alpha(rgba, 64, 0, 0).Should().Be(0);
        Alpha(rgba, 64, 63, 63).Should().Be(0);

        // Faces com orientações diferentes saem com tons diferentes (sombreado plano).
        var tones = Enumerable.Range(0, 64 * 64).Where(i => rgba[4 * i + 3] == 255).Select(i => rgba[4 * i]).Distinct().Count();
        tones.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Miniatura_Png_TemAssinaturaCabecalhoEPixelsComprimidos()
    {
        var rgba = ThumbnailRasterizer.Render(Box(1, 1, 1), 16);
        var png = ThumbnailRasterizer.EncodePng(rgba, 16, 16);

        png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        System.Text.Encoding.ASCII.GetString(png, 12, 4).Should().Be("IHDR");

        int idatLength = (png[33] << 24) | (png[34] << 16) | (png[35] << 8) | png[36];
        System.Text.Encoding.ASCII.GetString(png, 37, 4).Should().Be("IDAT");
        using var zlib = new ZLibStream(new MemoryStream(png, 41, idatLength), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        raw.Length.Should().Be(16 * (1 + 16 * 4), "um byte de filtro por linha mais os pixels");
    }

    [Fact]
    public async Task Miniatura_DeUmaPecaDaBiblioteca_SaiEFicaEmCachePeloConteudo()
    {
        var cache = new MemoryCache();
        var service = new PartThumbnailService(new RoslynSceneEvaluator(),
            () => new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel(), cache);
        var index = PartDependencyIndex.Of(Library());

        var png = await service.GetAsync(Item("Extrusao2020"), index, default);

        png.Should().NotBeNull();
        cache.Entries.Should().ContainSingle();
        (await service.GetAsync(Item("Extrusao2020"), index, default)).Should().Equal(png);
        cache.Writes.Should().Be(1, "a segunda vez vem do cache");

        var changed = PartDependencyIndex.Of(Library().Select(d => d.FilePath.EndsWith("Extrusao2020.cs") ? d with { Text = d.Text.Replace("20, 20,", "20, 40,") } : d).ToList());
        PartThumbnailService.KeyOf("Extrusao2020", changed.Dependencies("Extrusao2020", out _))
            .Should().NotBe(PartThumbnailService.KeyOf("Extrusao2020", index.Dependencies("Extrusao2020", out _)));
    }

    // ---------------------------------------------------------------- Auxiliares

    private static PartLibraryItem Item(string name) =>
        PartLibraryCatalog.Read(Library(), PartLibrarySource.Library).Single(i => i.Name == name);

    private static IReadOnlyList<ScriptDocument> SceneWith(params (string Path, string Text)[] extra) =>
        new[] { new ScriptDocument("Cena.cs", "AddFeature(new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = \"base_1\" });\n") }
            .Concat(extra.Select(e => new ScriptDocument(e.Path, e.Text)))
            .ToList();

    private static byte Alpha(byte[] rgba, int size, int x, int y) => rgba[4 * (y * size + x) + 3];

    private static TriangleMesh Box(float sx, float sy, float sz)
    {
        var p = new List<float>();
        var idx = new List<uint>();
        void Quad(params (float X, float Y, float Z)[] v)
        {
            uint start = (uint)(p.Count / 3);
            foreach (var c in v) { p.Add(c.X); p.Add(c.Y); p.Add(c.Z); }
            idx.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        Quad((0, 0, 0), (sx, 0, 0), (sx, sy, 0), (0, sy, 0));
        Quad((0, 0, sz), (sx, 0, sz), (sx, sy, sz), (0, sy, sz));
        Quad((0, 0, 0), (sx, 0, 0), (sx, 0, sz), (0, 0, sz));
        Quad((0, sy, 0), (sx, sy, 0), (sx, sy, sz), (0, sy, sz));
        Quad((0, 0, 0), (0, sy, 0), (0, sy, sz), (0, 0, sz));
        Quad((sx, 0, 0), (sx, sy, 0), (sx, sy, sz), (sx, 0, sz));
        return new TriangleMesh(p.ToArray(), new float[p.Count], idx.ToArray());
    }

    private sealed class MemoryCache : IThumbnailCache
    {
        public Dictionary<string, byte[]> Entries { get; } = new();
        public int Writes { get; private set; }
        public byte[]? Get(string key) => Entries.TryGetValue(key, out var png) ? png : null;
        public void Put(string key, byte[] png) { Entries[key] = png; Writes++; }
    }

    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "AiCore");
    }
}
