using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Harness.Agents;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;

namespace AvaloniaCadEditor.Harness.Tests.Agents;

/// <summary>
/// Editor e Inspetor em modelos diferentes ("editor" e "inspetor"), cada um com o seu roteiro.
/// </summary>
public class InspectorTests
{
    private static readonly string[] ReadOnlyTools = { "evaluate_scene", "get_bounding_box", "get_dimensions", "get_view", "list_targets" };

    private static HarnessChatSession Session(ModelRouter router, bool preInspection)
    {
        var inspector = new InspectorAgent(new LlmOptions { Model = "inspetor", ApiKeyRequired = false }, AgentLimits.Local, "Ollama", router.Send);
        return new HarnessChatSession(new LlmOptions { Model = "editor", ApiKey = "fake" }, new CadScriptProtocol(),
            new RoslynSceneEvaluator(), new OpenCascadeGeometryKernel(), send: router.Send, inspector: inspector, preInspection: preInspection);
    }

    [Fact]
    public async Task PreInspecao_OResumoDoInspetorVaiNoPedidoDoEditor()
    {
        var inspector = new ScriptedModel()
            .ThenCall("get_bounding_box", "{\"target\":\"placa_1\"}")
            .ThenSay("- placa_1: x 10..60, y 0..30, z 0..5 (mundo).");
        var editor = new ScriptedModel().ThenSay("A placa vai de x 10 a 60.");
        var session = Session(new ModelRouter().Add("inspetor", inspector).Add("editor", editor), preInspection: true);

        var result = await session.SendAsync("Onde está a placa?", TestProject.Context);

        result.IsValid.Should().BeTrue(result.Error);
        inspector.Calls.Should().OnlyContain(c => c.ToolNames.SequenceEqual(ReadOnlyTools), "o Inspetor só recebe ferramentas de leitura");
        inspector.Calls[0].LastUser.Should().Contain("Onde está a placa?", "o Inspetor recebe o pedido com o contexto");

        var request = editor.Calls.Single().LastUser;
        request.Should().Contain("### NOTAS GEOMÉTRICAS (do Inspetor, Ollama)");
        request.Should().Contain("x 10..60");
        editor.Calls.Single().ToolNames.Should().Contain("inspect");

        session.LastPreInspection!.Steps.Should().ContainSingle(s => s.Tool == "get_bounding_box");
        session.LastPreInspection.Steps[0].Result.Should().Contain("mundo: min (10, 0, 0)");
    }

    [Fact]
    public async Task FerramentaInspect_RodaOInspetorSobAPerguntaDoEditor()
    {
        var inspector = new ScriptedModel()
            .ThenCall("list_targets")
            .ThenSay("Só há placa_1 (Placa).");
        var editor = new ScriptedModel()
            .ThenCall("inspect", "{\"question\":\"Quais peças existem?\"}")
            .ThenSay("Só a placa_1.");
        var session = Session(new ModelRouter().Add("inspetor", inspector).Add("editor", editor), preInspection: false);

        await session.SendAsync("Quais peças?", TestProject.Context);

        inspector.Calls[0].LastUser.Should().StartWith("### PERGUNTA DO EDITOR\nQuais peças existem?");
        session.History.Single(m => m.IsTool).Content.Should().Be("Só há placa_1 (Placa).", "o Editor recebe só o resumo");
        editor.Calls.First().LastUser.Should().NotContain("NOTAS GEOMÉTRICAS", "sem pré-inspeção");
    }

    [Fact]
    public async Task ResumoLongo_ECortado()
    {
        var inspector = new ScriptedModel().ThenSay(new string('x', InspectorAgent.MaxSummaryChars + 500));
        var editor = new ScriptedModel().ThenSay("ok");
        var session = Session(new ModelRouter().Add("inspetor", inspector).Add("editor", editor), preInspection: true);

        await session.SendAsync("?", TestProject.Context);

        session.LastPreInspection!.Summary.Should().EndWith("…(resumo cortado)");
        session.LastPreInspection.Summary.Length.Should().BeLessThan(InspectorAgent.MaxSummaryChars + 40);
    }
}
