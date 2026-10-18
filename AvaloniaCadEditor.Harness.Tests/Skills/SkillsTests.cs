using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Harness.Skills;
using AvaloniaCadEditor.Harness.Tests.Agents;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;

namespace AvaloniaCadEditor.Harness.Tests.Skills;

/// <summary>
/// Skills: ligada = no system prompt da conversa toda; <c>/id</c> no começo do pedido = só naquele turno.
/// </summary>
public class SkillsTests
{
    private static readonly SkillRegistry Registry = SkillRegistry.Default();

    private static HarnessChatSession Session(ScriptedModel model, params string[] enabled) =>
        new(new LlmOptions { ApiKey = "fake" }, new CadScriptProtocol(), new RoslynSceneEvaluator(), new OpenCascadeGeometryKernel(),
            send: model.Send, skills: Registry.Resolve(enabled));

    [Fact]
    public void Registro_TemIdsUnicosETextoEmTodaSkill()
    {
        Registry.All.Should().NotBeEmpty();
        Registry.All.Select(s => s.Id).Should().OnlyHaveUniqueItems();
        Registry.All.Should().OnlyContain(s =>
            s.Id == s.Id.ToLowerInvariant() && !s.Id.Contains(' ') &&
            s.Title.Length > 0 && s.Description.Length > 0 && s.Instructions.Length > 0);
    }

    [Fact]
    public void Resolve_IgnoraIdDesconhecidoEMantemAOrdemDoRegistro()
    {
        Registry.Resolve(new[] { "revisar", "nao-existe", "QUESTIONAR" }).Select(s => s.Id)
            .Should().Equal(SkillRegistry.Questionar, SkillRegistry.Revisar);
    }

    [Theory]
    [InlineData("/questionar /revisar põe um motor", "questionar,revisar", "põe um motor")]
    [InlineData("  /Questionar põe um motor", "questionar", "põe um motor")]
    [InlineData("/questionar", "questionar", "")]
    [InlineData("/questionar mede a/b", "questionar", "mede a/b")]
    [InlineData("/outra coisa", "", "/outra coisa")]
    [InlineData("/questionar /outra coisa", "questionar", "/outra coisa")]
    [InlineData("mede a /questionar", "", "mede a /questionar")]
    public void ParseCommands_TiraSoAsSkillsDoComeco(string instruction, string ids, string rest)
    {
        var (skills, remaining) = Registry.ParseCommands(instruction);

        string.Join(",", skills.Select(s => s.Id)).Should().Be(ids);
        remaining.Should().Be(rest);
    }

    [Fact]
    public async Task SemSkill_NadaDeSkillNoPrompt()
    {
        var model = new ScriptedModel().ThenSay("ok");
        var session = Session(model);

        await session.SendAsync("Onde está a placa?", TestProject.Context);

        session.History[0].Content.Should().NotContain("### SKILL");
        model.Calls.Single().LastUser.Should().NotContain("### SKILL");
        session.LastTurnSkills.Should().BeEmpty();
    }

    [Fact]
    public async Task SkillLigada_VaiNoSystemPromptEValeEmTodoTurno()
    {
        var model = new ScriptedModel().ThenSay("1. Qual altura?").ThenSay("Feito.");
        var session = Session(model, SkillRegistry.Questionar);

        await session.SendAsync("Põe um motor", TestProject.Context);
        await session.SendAsync("No chão", TestProject.Context);

        session.History[0].Content.Should().Contain("### SKILLS LIGADAS").And.Contain("### SKILL: questionar")
            .And.Contain(SkillRegistry.QuestionarInstructions.Trim());
        model.Calls.Should().OnlyContain(c => !c.LastUser.Contains("### SKILLS DESTE PEDIDO"), "ligada, ela já está no system prompt");
        session.LastTurnSkills.Select(s => s.Id).Should().Equal(SkillRegistry.Questionar);
    }

    [Fact]
    public async Task ComandoNoPedido_ValeSoNaqueleTurno()
    {
        var model = new ScriptedModel().ThenSay("1. Qual altura?").ThenSay("Feito.");
        var session = Session(model);

        var result = await session.SendAsync("/questionar Põe um motor", TestProject.Context);

        result.IsValid.Should().BeTrue(result.Error);
        string first = model.Calls[0].LastUser;
        first.Should().Contain("### SKILLS DESTE PEDIDO").And.Contain("### SKILL: questionar");
        first.Should().Contain("Põe um motor").And.NotContain("/questionar", "o comando sai do pedido");
        session.History[0].Content.Should().NotContain("### SKILL", "o system prompt da conversa não muda");
        session.LastTurnSkills.Select(s => s.Id).Should().Equal(SkillRegistry.Questionar);

        await session.SendAsync("No chão", TestProject.Context);

        model.Calls[1].LastUser.Should().NotContain("### SKILL");
        session.LastTurnSkills.Should().BeEmpty();
    }

    [Fact]
    public async Task ComandoDeSkillJaLigada_NaoRepeteAsInstrucoes()
    {
        var model = new ScriptedModel().ThenSay("ok");
        var session = Session(model, SkillRegistry.Questionar);

        await session.SendAsync("/questionar Põe um motor", TestProject.Context);

        model.Calls.Single().LastUser.Should().NotContain("### SKILLS DESTE PEDIDO").And.NotContain("/questionar");
        session.LastTurnSkills.Select(s => s.Id).Should().Equal(SkillRegistry.Questionar);
    }
}
