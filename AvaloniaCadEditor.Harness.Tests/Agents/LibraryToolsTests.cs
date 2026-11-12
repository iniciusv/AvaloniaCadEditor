using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Harness.Agents;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;

namespace AvaloniaCadEditor.Harness.Tests.Agents;

/// <summary>
/// A IA busca na biblioteca do usuário e importa uma peça para Pecas/ (search_library, import_library_part). A
/// biblioteca chega pelo <see cref="CadScriptContext.Library"/>, e a cópia é uma mudança da cópia de trabalho como
/// qualquer outra: a UI a aplica no fim do turno.
/// </summary>
public class LibraryToolsTests
{
    private const string Trilho = """
        /// <summary>Trilho de guia linear MGN9 (9 × 6,5 mm). Só o comprimento varia.</summary>
        public sealed record TrilhoTeste(double Comprimento = 200) : Part
        {
            protected override IFeature Build() => new BoxFeature(Point3D.Origin, 9, Comprimento, 6.5) { Name = "trilho" };
        }
        """;

    private const string Carrinho = """
        /// <summary>Carrinho MGN9C que anda sobre o TrilhoTeste.</summary>
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

    private const string Furo = """
        /// <summary>Furo M3 para subtrair.</summary>
        public sealed record FuroTeste(Point3D Face = default, Vector3D? Direcao = null) : Part
        {
            public override bool IsCutter => true;
            protected override IFeature Build() => new CylinderFeature(Face, Direcao ?? new Vector3D(0, 0, -1), 1.7, 10) { Name = "furo" };
        }
        """;

    private static readonly IReadOnlyList<ScriptDocument> Library = new[]
    {
        new ScriptDocument("Guias lineares/TrilhoTeste.cs", Trilho),
        new ScriptDocument("Guias lineares/CarrinhoTeste.cs", Carrinho),
        new ScriptDocument("Furos/FuroTeste.cs", Furo)
    };

    private static readonly CadScriptContext Context = TestProject.Context with { Library = Library };

    private static HarnessChatSession Session(ScriptedModel model) =>
        new(new LlmOptions { ApiKey = "fake" }, new CadScriptProtocol(), new RoslynSceneEvaluator(), new OpenCascadeGeometryKernel(),
            send: model.Send);

    [Fact]
    public async Task Buscar_ListaCategorias_EAchaPorPalavras()
    {
        var model = new ScriptedModel()
            .ThenCall("search_library")
            .ThenCall("search_library", "{\"query\":\"mgn9 carrinho\"}")
            .ThenCall("search_library", "{\"query\":\"furo\"}")
            .ThenSay("Achei.");
        var session = Session(model);

        var result = await session.SendAsync("Tem carrinho MGN9 na biblioteca?", Context);

        result.IsValid.Should().BeTrue(result.Error);
        result.Changes.Should().BeEmpty("buscar não altera nada");
        var tools = session.History.Where(m => m.IsTool).Select(m => m.Content).ToList();
        tools[0].Should().Contain("3 peça(s) na biblioteca").And.Contain("- Guias lineares: CarrinhoTeste, TrilhoTeste").And.Contain("- Furos: FuroTeste");
        tools[1].Should().Contain("1 peça(s) com \"mgn9 carrinho\"")
            .And.Contain("- CarrinhoTeste [Guias lineares]")
            .And.Contain("CarrinhoTeste(double Largura = 20)")
            .And.Contain("modelos prontos: CarrinhoTeste.Curto")
            .And.NotContain("TrilhoTeste [");
        tools[2].Should().Contain("FuroTeste [Furos] (vazio");
    }

    [Fact]
    public async Task Importar_CopiaAPecaEADependencia_EACenaUsaNoMesmoTurno()
    {
        var model = new ScriptedModel()
            .ThenCall("import_library_part", "{\"name\":\"CarrinhoTeste\"}")
            .ThenCall("replace_in_file", System.Text.Json.JsonSerializer.Serialize(new
            {
                path = "Cena.cs",
                old_text = TestProject.Scene,
                new_text = TestProject.Scene + "var carrinho_1 = Add(new CarrinhoTeste(), at: Placement.At(100, 0, 0), name: \"carrinho_1\");\n"
            }))
            .ThenCall("evaluate_scene")
            .ThenSay("Pus o carrinho.");
        var session = Session(model);

        var result = await session.SendAsync("Ponha um carrinho MGN9 da biblioteca", Context);

        result.IsValid.Should().BeTrue(result.Error);
        result.Changes.Select(c => c.FilePath).Should().BeEquivalentTo("Pecas/CarrinhoTeste.cs", "Pecas/TrilhoTeste.cs", "Cena.cs");
        result.Changes.Single(c => c.FilePath == "Pecas/TrilhoTeste.cs").Text.Should().Be(Trilho);
        var import = session.History.First(m => m.IsTool).Content;
        import.Should().StartWith("ok — copiado da biblioteca: Pecas/CarrinhoTeste.cs, Pecas/TrilhoTeste.cs.")
            .And.Contain("Assinatura: CarrinhoTeste(double Largura = 20)")
            .And.Contain("public sealed record CarrinhoTeste");
        session.History.Where(m => m.IsTool).ElementAt(2).Content.Should().NotContain("Erro");
    }

    [Fact]
    public async Task Importar_SemBiblioteca_OuNomeQueNaoExiste_VoltaComoErro()
    {
        var model = new ScriptedModel()
            .ThenCall("import_library_part", "{\"name\":\"Inexistente\"}")
            .ThenSay("Não achei.");
        var session = Session(model);
        await session.SendAsync("Importe", Context);
        session.History.Single(m => m.IsTool).Content.Should().Contain("Erro: a biblioteca não tem a peça Inexistente");

        var empty = new ScriptedModel().ThenCall("search_library", "{\"query\":\"x\"}").ThenSay("Vazia.");
        var emptySession = Session(empty);
        await emptySession.SendAsync("Busque", TestProject.Context);
        emptySession.History.Single(m => m.IsTool).Content.Should().Contain("vazia ou não foi configurada");
    }
}
