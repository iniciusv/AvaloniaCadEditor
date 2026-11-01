using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Prompts;
using AvaloniaCadEditor.Harness.Agents;
using AvaloniaCadEditor.Harness.Skills;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;

namespace AvaloniaCadEditor.Harness.Tests.Agents;

/// <summary>
/// A rodada de estilo do perfil "editavel" no harness: avisos novos do linter voltam ao modelo uma vez; a
/// skill /parametrico a desliga; reescrita que falha devolve a cópia de trabalho ao estado que funcionava.
/// </summary>
public class EditabilityStyleRoundHarnessTests
{
    private static readonly CadScriptContext Context = TestProject.Context;

    private const string Computed = "var x0 = 10.0;\nvar placa_1 = Add(new Placa(), at: Placement.At(x0 * 2, 0, 0), name: \"placa_1\");\n";
    private const string Literal = "var placa_1 = Add(new Placa(), at: Placement.At(20, 0, 0), name: \"placa_1\");\n";

    private static string Block(string code) => $"Feito.\n```cadscript file=Cena.cs\n{code}```";

    private static HarnessChatSession Session(ScriptedModel model) =>
        new(new LlmOptions { ApiKey = "fake" }, new CadScriptProtocol(new EditablePromptProfile()), new RoslynSceneEvaluator(),
            new OpenCascadeGeometryKernel(), send: model.Send);

    [Fact]
    public async Task AvisoNovo_VoltaAoModelo_EAReescritaVale()
    {
        var model = new ScriptedModel().ThenSay(Block(Computed)).ThenSay(Block(Literal));
        var session = Session(model);

        var result = await session.SendAsync("Mova a placa para x 20", Context);

        result.IsValid.Should().BeTrue(result.Error);
        result.Changes.Single().Text.TrimEnd().Should().Be(Literal.TrimEnd());
        model.Calls.Should().HaveCount(2);
        session.History.Should().Contain(m => m.IsUser && m.Content.Contains("estilo editável à mão"));
    }

    [Fact]
    public async Task ComASkillParametrico_NaoHaRodadaDeEstilo()
    {
        var model = new ScriptedModel().ThenSay(Block(Computed));
        var session = Session(model);

        var result = await session.SendAsync($"/{SkillRegistry.Parametrico} mova a placa para x 20", Context);

        result.IsValid.Should().BeTrue(result.Error);
        result.Changes.Single().Text.TrimEnd().Should().Be(Computed.TrimEnd());
        model.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReescritaQueFalha_VoltaACopiaQueFuncionava()
    {
        var model = new ScriptedModel().ThenSay(Block(Computed)).ThenSay(Block("var placa_1 = ;\n"));
        var session = Session(model);

        var result = await session.SendAsync("Mova a placa para x 20", Context);

        result.IsValid.Should().BeTrue(result.Error);
        result.Changes.Single().Text.TrimEnd().Should().Be(Computed.TrimEnd());
        session.History.Should().NotContain(m => m.IsUser && m.Content.Contains("estilo editável à mão"));
    }
}
