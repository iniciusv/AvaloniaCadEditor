using System.Net;
using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Harness.Agents;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;

namespace AvaloniaCadEditor.Harness.Tests.Agents;

/// <summary>
/// O laço do harness com um modelo roteirizado: cada resposta do "modelo" é uma função do histórico.
/// O projeto tem a cena e uma peça em Pecas/ (as regras do CadScript não deixam declarar record no
/// script); avaliador Roslyn e kernel OCCT reais.
/// </summary>
public class HarnessChatSessionTests
{
    private const string Scene = "var placa_1 = Add(new Placa(), at: Placement.At(10, 0, 0), name: \"placa_1\");\n";

    private const string PlacaFile =
        "public sealed record Placa(double Largura = 50) : Part\n" +
        "{\n" +
        "    protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, 30, 5) { Name = \"placa\" };\n" +
        "}\n";

    private static readonly CadScriptContext Context = BuildContext();

    private static CadScriptContext BuildContext()
    {
        var all = new[] { new ScriptDocument("Cena.cs", Scene), new ScriptDocument("Pecas/Placa.cs", PlacaFile) };
        return new CadScriptContext(
            Scene,
            Kind: CadFileKind.SceneEntry,
            FileName: "Cena.cs",
            ProjectDocuments: all,
            Scope: new MultiFileScope(
                all,
                new Dictionary<string, CadFileKind>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Cena.cs"] = CadFileKind.SceneEntry,
                    ["Pecas/Placa.cs"] = CadFileKind.PartFile
                },
                Array.Empty<string>()));
    }

    private sealed class ScriptedModel
    {
        private readonly Queue<Func<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, LlmResult>> _replies = new();

        public List<(int Messages, int Tools)> Calls { get; } = new();

        public ScriptedModel Then(Func<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, LlmResult> reply)
        {
            _replies.Enqueue(reply);
            return this;
        }

        public ScriptedModel ThenCall(string tool, string args = "{}") =>
            Then((_, _) => new LlmResult { FinishReason = "tool_calls", ToolCalls = new[] { new ToolCall($"c{_replies.Count}", tool, args) } });

        public ScriptedModel ThenSay(string text) => Then((_, _) => new LlmResult { Content = text, FinishReason = "stop" });

        public Task<LlmResult> Send(IReadOnlyList<ChatMessage> messages, LlmOptions options, IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
        {
            Calls.Add((messages.Count, tools.Count));
            if (_replies.Count == 0) throw new InvalidOperationException("O roteiro acabou.");
            return Task.FromResult(_replies.Dequeue()(messages, tools));
        }
    }

    private static HarnessChatSession Session(ScriptedModel model, AgentLimits? limits = null) =>
        new(new LlmOptions { ApiKey = "fake" }, new CadScriptProtocol(), new RoslynSceneEvaluator(), new OpenCascadeGeometryKernel(),
            limits, send: model.Send);

    [Fact]
    public async Task Ferramentas_SaoExecutadasEOsResultadosVoltamComOId()
    {
        var model = new ScriptedModel()
            .Then((_, _) => new LlmResult
            {
                FinishReason = "tool_calls",
                ReasoningContent = "vou olhar",
                ToolCalls = new[]
                {
                    new ToolCall("a", "list_targets", "{}"),
                    new ToolCall("b", "get_bounding_box", "{\"target\":\"placa_1\"}")
                }
            })
            .ThenSay("A placa vai de x 10 a 60.");
        var session = Session(model);

        var result = await session.SendAsync("Onde está a placa?", Context);

        result.IsValid.Should().BeTrue(result.Error);
        result.ChatText.Should().Contain("x 10 a 60");
        result.Changes.Should().BeEmpty();
        model.Calls.Should().OnlyContain(c => c.Tools == 8, "as 8 ferramentas v1 vão em toda chamada");

        var history = session.History;
        var withCalls = history.Single(m => m.IsAssistant && m.ToolCalls.Count == 2);
        withCalls.ReasoningContent.Should().Be("vou olhar", "a DeepSeek exige o reasoning de volta com tools");
        history.Where(m => m.IsTool).Select(m => m.ToolCallId).Should().Equal("a", "b");
        history.Single(m => m.IsTool && m.ToolCallId == "a").Content.Should().Contain("- placa_1 (Placa): mundo min (10, 0, 0) max (60, 30, 5)");
        history.Single(m => m.IsTool && m.ToolCallId == "b").Content.Should().Contain("tamanho (50, 30, 5)");
        session.LastTurnSteps.Select(s => s.Tool).Should().Equal("list_targets", "get_bounding_box");
    }

    [Fact]
    public async Task TranslateInstance_EditaACopiaDeTrabalhoESaiComoUmChange()
    {
        var model = new ScriptedModel()
            .ThenCall("translate_instance", "{\"target\":\"placa_1\",\"dx\":5,\"dy\":0,\"dz\":2}")
            .ThenSay("Movi a placa.");
        var session = Session(model);

        var result = await session.SendAsync("Mova a placa 5 mm em X e 2 em Z", Context);

        result.IsValid.Should().BeTrue(result.Error);
        var change = result.Changes.Should().ContainSingle().Subject;
        change.FilePath.Should().Be("Cena.cs");
        change.Text.Should().Contain("Placement.At(15, 0, 2)");
        result.FeatureCount.Should().Be(1);
        session.History.Single(m => m.IsTool).Content.Should().Contain("mundo: min (15, 0, 2) max (65, 30, 7)", "a ferramenta devolve a caixa nova");
    }

    [Fact]
    public async Task ReplaceInFile_ComErro_PedeCorrecaoEAceitaOConserto()
    {
        var model = new ScriptedModel()
            .ThenCall("replace_in_file", "{\"path\":\"Pecas/Placa.cs\",\"old_text\":\"Largura = 50\",\"new_text\":\"Largura = \"}")
            .ThenSay("Mudei a largura.")
            .ThenCall("replace_in_file", "{\"path\":\"Pecas/Placa.cs\",\"old_text\":\"Largura = )\",\"new_text\":\"Largura = 80)\"}")
            .ThenSay("Corrigido.");
        var session = Session(model);

        var result = await session.SendAsync("Placa de 80", Context);

        result.IsValid.Should().BeTrue(result.Error);
        result.Attempts.Should().Be(2);
        var change = result.Changes.Should().ContainSingle().Subject;
        change.FilePath.Should().Be("Pecas/Placa.cs");
        change.Text.Should().Contain("double Largura = 80)");
        session.History.Should().Contain(m => m.IsUser && m.Content.Contains("falharam ao compilar"));
    }

    [Fact]
    public async Task ConsultaRepetida_NaMesmaRevisao_VemDoCache()
    {
        var model = new ScriptedModel()
            .ThenCall("get_view", "{\"target\":\"placa_1\",\"view\":\"Top\"}")
            .ThenCall("get_view", "{ \"view\": \"Top\", \"target\": \"placa_1\" }")
            .ThenSay("ok");
        var session = Session(model);

        await session.SendAsync("Veja a placa", Context);

        session.LastTurnSteps.Select(s => s.Cached).Should().Equal(false, true);
        session.History.Where(m => m.IsTool).Last().Content.Should().StartWith("(já consultado");
    }

    [Fact]
    public async Task LimiteDePassos_AvisaOModeloEEncerra()
    {
        var model = new ScriptedModel()
            .ThenCall("list_targets")
            .ThenCall("get_bounding_box", "{\"target\":\"placa_1\"}")
            .ThenCall("get_view", "{\"target\":\"placa_1\"}")
            .ThenCall("get_view", "{\"target\":\"placa_1\",\"view\":\"Top\"}");
        var session = Session(model, new AgentLimits { MaxToolSteps = 2 });

        var result = await session.SendAsync("Olhe tudo", Context);

        model.Calls.Should().HaveCount(4);
        session.LastTurnSteps.Should().HaveCount(2);
        session.History.Should().Contain(m => m.IsTool && m.Content.StartsWith("Limite de passos"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ServidorQueRecusaTools_DegradaParaOProtocoloEmTexto()
    {
        var model = new ScriptedModel()
            .Then((_, _) => throw new HttpRequestException("Erro na API da IA (400): model does not support tools", null, HttpStatusCode.BadRequest))
            .ThenSay("```tool\n{\"name\": \"list_targets\", \"arguments\": {}}\n```")
            .ThenSay("Achei a placa_1.");
        var session = Session(model);

        var result = await session.SendAsync("Quais peças há?", Context);

        result.ChatText.Should().Contain("placa_1");
        session.Mode.Should().Be(ToolMode.Text);
        model.Calls[0].Tools.Should().Be(8, "a primeira tentativa vai com tools; o servidor recusa");
        model.Calls.Skip(1).Should().OnlyContain(c => c.Tools == 0);
        session.History.Should().Contain(m => m.IsUser && m.Content.StartsWith("### FERRAMENTAS (protocolo em texto)"));
        session.History.Should().Contain(m => m.IsUser && m.Content.StartsWith("### RESULTADOS DAS FERRAMENTAS") && m.Content.Contains("placa_1"));
    }

    [Fact]
    public async Task ChamadaEscritaNoTexto_ComToolsNativas_TambemEExecutada()
    {
        var model = new ScriptedModel()
            .ThenSay("Vou listar.\n```tool\n{\"name\": \"list_targets\", \"arguments\": {}}\n```")
            .ThenSay("Pronto.");
        var session = Session(model);

        await session.SendAsync("Liste", Context);

        session.Mode.Should().Be(ToolMode.Text);
        session.LastTurnSteps.Should().ContainSingle(s => s.Tool == "list_targets");
    }

    [Fact]
    public async Task FerramentaDesconhecida_VoltaComoErroParaOModelo()
    {
        var model = new ScriptedModel().ThenCall("apagar_tudo").ThenSay("ok");
        var session = Session(model);

        await session.SendAsync("?", Context);

        session.History.Single(m => m.IsTool).Content.Should().StartWith("Erro: ferramenta desconhecida 'apagar_tudo'");
    }

    [Fact]
    public async Task DeleteTurn_RemoveAsMensagensDeFerramentaDoTurno()
    {
        var model = new ScriptedModel().ThenCall("list_targets").ThenSay("ok");
        var session = Session(model);
        await session.SendAsync("Liste", Context);
        session.History.Should().Contain(m => m.IsTool);

        session.DeleteTurn(0);

        session.History.Should().ContainSingle().Which.Role.Should().Be("system");
    }

    [Fact]
    public async Task SystemPrompt_TemAsInstrucoesDasFerramentas()
    {
        var session = Session(new ScriptedModel());

        session.History[0].Content.Should().Contain("### FERRAMENTAS").And.Contain("get_view");
        await Task.CompletedTask;
    }
}
