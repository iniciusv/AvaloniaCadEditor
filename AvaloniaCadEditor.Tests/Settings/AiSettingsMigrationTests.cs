using System.Linq;
using AvaloniaCadEditor.Domain.Settings;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Settings;

public class AiSettingsMigrationTests
{
    [Fact]
    public void EsquemaAntigo_MoveCamposParaPerfilDeepSeekAtivo()
    {
        var settings = new AiSettings { ApiKey = "k", BaseUrl = "https://x.test/", Model = "m" };

        AiSettingsMigration.Upgrade(settings);

        settings.SchemaVersion.Should().Be(AiSettings.CurrentSchemaVersion);
        var profile = settings.GetActiveProfile()!;
        profile.Id.Should().Be(AiEndpointProfile.DeepSeekId);
        profile.ApiKey.Should().Be("k");
        profile.BaseUrl.Should().Be("https://x.test/");
        profile.Model.Should().Be("m");
        profile.ApiKeyEnvVar.Should().Be("DEEPSEEK_API_KEY");
        profile.MaxTokens.Should().Be(384000, "o limite que o cliente usava antes dos perfis");
        settings.ApiKey.Should().BeEmpty();
        settings.BaseUrl.Should().BeEmpty();
        settings.Model.Should().BeEmpty();
    }

    [Fact]
    public void EIdempotente()
    {
        var settings = new AiSettings { ApiKey = "k" };

        AiSettingsMigration.Upgrade(settings);
        settings.Profiles.Add(AiEndpointProfile.Local("ollama", "Ollama"));
        settings.ActiveProfileId = "ollama";
        AiSettingsMigration.Upgrade(settings);

        settings.Profiles.Select(p => p.Id).Should().Equal("deepseek", "ollama");
        settings.ActiveProfileId.Should().Be("ollama");
        settings.Profiles[0].ApiKey.Should().Be("k");
    }

    [Fact]
    public void PerfilAtivoInexistente_CaiNoPrimeiro()
    {
        var settings = AiSettingsMigration.Upgrade(new AiSettings());
        settings.ActiveProfileId = "apagado";

        AiSettingsMigration.Upgrade(settings);

        settings.ActiveProfileId.Should().Be("deepseek");
    }

    [Fact]
    public void SemPerfis_RecriaODeepSeek()
    {
        var settings = AiSettingsMigration.Upgrade(new AiSettings());
        settings.Profiles.Clear();

        AiSettingsMigration.Upgrade(settings);

        settings.Profiles.Should().ContainSingle(p => p.Id == "deepseek");
    }

    [Theory]
    [InlineData("Ollama Qwen", "ollama-qwen")]
    [InlineData("  LM Studio / Gemma 3  ", "lm-studio-gemma-3")]
    [InlineData("***", "perfil")]
    public void NewProfileId_GeraSlug(string name, string expected)
    {
        AiEndpointProfile.NewId(new[] { AiEndpointProfile.DeepSeek() }, name).Should().Be(expected);
    }

    [Fact]
    public void NewProfileId_EvitaRepetido()
    {
        var existing = new[] { AiEndpointProfile.DeepSeek(), AiEndpointProfile.Local("ollama", "Ollama") };

        AiEndpointProfile.NewId(existing, "Ollama").Should().Be("ollama-2");
        AiEndpointProfile.NewId(existing, "DeepSeek").Should().Be("deepseek-2");
    }
}
