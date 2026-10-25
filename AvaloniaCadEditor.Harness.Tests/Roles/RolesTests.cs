using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Harness.Agents;
using AvaloniaCadEditor.Harness.Roles;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;

namespace AvaloniaCadEditor.Harness.Tests.Roles;

public class RolesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "harness-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static AiSettings App()
    {
        var app = AiSettingsMigration.Upgrade(new AiSettings());
        app.Profiles[0].ApiKey = "sk-test";
        var local = AiEndpointProfile.Local("ollama", "Ollama");
        local.Model = "qwen3:8b";
        app.Profiles.Add(local);
        return app;
    }

    private sealed class FixedAppSettings(AiSettings settings) : IAiSettingsProvider
    {
        public AiSettings GetSettings() => settings;
        public void SaveSettings(AiSettings s) { }
    }

    [Fact]
    public void Editor_SemEscolha_UsaOPerfilAtivoDoChat()
    {
        var (role, error) = RoleOptionsResolver.Resolve(App(), new HarnessSettings(), AgentRole.Editor);

        error.Should().BeNull();
        role!.Profile.Id.Should().Be("deepseek");
        role.Limits.Should().Be(AgentLimits.Remote);
    }

    [Fact]
    public void Inspetor_SemEscolha_NaoExiste()
    {
        var (role, error) = RoleOptionsResolver.Resolve(App(), new HarnessSettings(), AgentRole.Inspector);

        role.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void InspetorLocal_GanhaLimitesDeModeloLocal()
    {
        var harness = new HarnessSettings();
        harness.SetProfile(AgentRole.Inspector, "ollama");

        var (role, _) = RoleOptionsResolver.Resolve(App(), harness, AgentRole.Inspector);

        role!.Options.Model.Should().Be("qwen3:8b");
        role.Options.ApiKeyRequired.Should().BeFalse();
        role.Limits.Should().Be(AgentLimits.Local);
        role.Limits.MaxToolSteps.Should().BeGreaterThan(AgentLimits.Remote.MaxToolSteps);
    }

    [Fact]
    public void PerfilApagado_DaErroComONomeDoPapel()
    {
        var harness = new HarnessSettings();
        harness.SetProfile(AgentRole.Inspector, "sumiu");

        var (role, error) = RoleOptionsResolver.Resolve(App(), harness, AgentRole.Inspector);

        role.Should().BeNull();
        error.Should().Contain("sumiu").And.Contain("Inspector");
    }

    [Fact]
    public void Store_RoundTripNumaPastaTemporaria()
    {
        var store = new JsonHarnessSettingsStore(_folder);
        var settings = new HarnessSettings { Enabled = true, PreInspection = false };
        settings.SetProfile(AgentRole.Inspector, "ollama");

        store.Save(settings);
        var loaded = store.Load();

        loaded.Enabled.Should().BeTrue();
        loaded.PreInspection.Should().BeFalse();
        loaded.ProfileFor(AgentRole.Inspector).Should().Be("ollama");
        loaded.ProfileFor(AgentRole.Editor).Should().BeNull();
    }

    [Fact]
    public void Store_SemArquivo_HarnessDesligado()
    {
        new JsonHarnessSettingsStore(_folder).Load().Enabled.Should().BeFalse();
    }

    [Fact]
    public void Factory_Desligado_DaASessaoDeSempre()
    {
        var factory = new HarnessSessionFactory(new FixedAppSettings(App()), new JsonHarnessSettingsStore(_folder),
            new RoslynSceneEvaluator(), () => new OpenCascadeGeometryKernel());

        var session = factory.Create(new LlmOptions { ApiKey = "k" }, new CadScriptProtocol());

        session.Should().BeOfType<CadScriptChatSession>();
        factory.Description.Should().BeEmpty();
    }

    [Fact]
    public void Factory_Ligado_ComInspetor_DaAHarnessChatSession()
    {
        var store = new JsonHarnessSettingsStore(_folder);
        var settings = new HarnessSettings { Enabled = true };
        settings.SetProfile(AgentRole.Inspector, "ollama");
        store.Save(settings);
        var factory = new HarnessSessionFactory(new FixedAppSettings(App()), store, new RoslynSceneEvaluator(), () => new OpenCascadeGeometryKernel());

        var session = factory.Create(new LlmOptions { ApiKey = "k" }, new CadScriptProtocol());

        var harness = session.Should().BeOfType<HarnessChatSession>().Subject;
        harness.Inspector!.Options.Model.Should().Be("qwen3:8b");
        harness.Inspector.ProfileName.Should().Be("Ollama");
        harness.PreInspection.Should().BeTrue();
        factory.Description.Should().Be("ferramentas · Inspetor: Ollama");
    }

    [Fact]
    public void Store_GuardaAsSkillsLigadas()
    {
        var store = new JsonHarnessSettingsStore(_folder);
        var settings = new HarnessSettings { Enabled = true };
        settings.SetSkill("questionar", true);
        settings.SetSkill("revisar", true);
        settings.SetSkill("revisar", false);

        store.Save(settings);
        var loaded = store.Load();

        loaded.Skills.Should().Equal("questionar");
        loaded.IsSkillEnabled("QUESTIONAR").Should().BeTrue();
        loaded.IsSkillEnabled("revisar").Should().BeFalse();
    }

    [Fact]
    public void Factory_ComSkillLigada_PoeNaSessaoEIgnoraIdQueNaoExisteMais()
    {
        var store = new JsonHarnessSettingsStore(_folder);
        var settings = new HarnessSettings { Enabled = true, Skills = { "questionar", "skill-removida" } };
        store.Save(settings);
        var factory = new HarnessSessionFactory(new FixedAppSettings(App()), store, new RoslynSceneEvaluator(), () => new OpenCascadeGeometryKernel());

        var session = factory.Create(new LlmOptions { ApiKey = "k" }, new CadScriptProtocol());

        var harness = session.Should().BeOfType<HarnessChatSession>().Subject;
        harness.ActiveSkills.Select(s => s.Id).Should().Equal("questionar");
        harness.History[0].Content.Should().Contain("### SKILL: questionar");
        factory.Description.Should().Be("ferramentas · skills: questionar");
    }
}
