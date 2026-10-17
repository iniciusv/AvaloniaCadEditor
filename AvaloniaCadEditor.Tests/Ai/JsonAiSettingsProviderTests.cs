using System;
using System.IO;
using System.Linq;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Infrastructure.Settings;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// Testa o provider real numa pasta temporária (o construtor aceita a pasta justamente para não
/// tocar no %AppData%/AvaloniaCadEditor/ai_settings.json do usuário).
/// </summary>
public class JsonAiSettingsProviderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ai-settings-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "ai_settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void SaveSettings_DepoisGetSettings_RoundTripPreservaPerfis()
    {
        var provider = new JsonAiSettingsProvider(_folder);
        var settings = AiSettingsMigration.Upgrade(new AiSettings());
        settings.Profiles[0].ApiKey = "test-key-123";
        var local = AiEndpointProfile.Local("ollama", "Ollama");
        local.Model = "qwen3:8b";
        settings.Profiles.Add(local);
        settings.ActiveProfileId = "ollama";

        provider.SaveSettings(settings);
        var loaded = provider.GetSettings();

        loaded.Profiles.Select(p => p.Id).Should().Equal("deepseek", "ollama");
        loaded.Profiles[0].ApiKey.Should().Be("test-key-123");
        loaded.GetActiveProfile()!.Model.Should().Be("qwen3:8b");
        loaded.GetActiveProfile()!.IsLocal.Should().BeTrue();
    }

    [Fact]
    public void ArquivoDoEsquemaAntigo_ViraPerfilDeepSeek()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath,
            "{ \"ApiKey\": \"k\", \"Model\": \"deepseek-flash\", \"BaseUrl\": \"https://example.test/\", \"EnableReasoning\": true }");

        var loaded = new JsonAiSettingsProvider(_folder).GetSettings();

        loaded.SchemaVersion.Should().Be(AiSettings.CurrentSchemaVersion);
        loaded.ActiveProfileId.Should().Be("deepseek");
        var deepSeek = loaded.GetActiveProfile()!;
        deepSeek.ApiKey.Should().Be("k");
        deepSeek.Model.Should().Be("deepseek-flash");
        deepSeek.BaseUrl.Should().Be("https://example.test/");
        loaded.EnableReasoning.Should().BeTrue();
        loaded.ApiKey.Should().BeEmpty("os campos legados são zerados depois de migrados");
    }

    [Fact]
    public void PromptProfile_RoundTrip_EArquivoAntigoSemOCampo_FicaVazio()
    {
        var provider = new JsonAiSettingsProvider(_folder);

        provider.SaveSettings(new AiSettings { PromptProfile = "enxuto" });
        provider.GetSettings().PromptProfile.Should().Be("enxuto");

        // Arquivo gravado antes do campo existir: cai no vazio (= perfil padrão).
        File.WriteAllText(FilePath, "{ \"ApiKey\": \"k\" }");
        provider.GetSettings().PromptProfile.Should().BeEmpty();
    }

    [Fact]
    public void GetSettings_QuandoArquivoNaoExiste_RetornaSoOPerfilDeepSeekVazio()
    {
        var settings = new JsonAiSettingsProvider(_folder).GetSettings();

        settings.Profiles.Should().ContainSingle();
        var deepSeek = settings.GetActiveProfile()!;
        deepSeek.Id.Should().Be("deepseek");
        deepSeek.ApiKey.Should().BeEmpty();
        deepSeek.ApiKeyEnvVar.Should().Be("DEEPSEEK_API_KEY");
    }
}
