using System;
using System.IO;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Infrastructure.Settings;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// Testa o provider real (mesmo caminho usado em produção, %AppData%/AvaloniaCadEditor/ai_settings.json),
/// preservando/restaurando o conteúdo original do arquivo do usuário ao redor do teste.
/// </summary>
public class JsonAiSettingsProviderTests
{
    private static string FilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "AvaloniaCadEditor", "ai_settings.json");
    }

    [Fact]
    public void SaveSettings_DepoisGetSettings_RoundTripPreservaValores()
    {
        string path = FilePath();
        string? original = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            var provider = new JsonAiSettingsProvider();
            var settings = new AiSettings { ApiKey = "test-key-123", Model = "deepseek-test", BaseUrl = "https://example.test/" };

            provider.SaveSettings(settings);
            var loaded = provider.GetSettings();

            loaded.ApiKey.Should().Be("test-key-123");
            loaded.Model.Should().Be("deepseek-test");
            loaded.BaseUrl.Should().Be("https://example.test/");
        }
        finally
        {
            if (original != null) File.WriteAllText(path, original);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PromptProfile_RoundTrip_EArquivoAntigoSemOCampo_FicaVazio()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ai-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new JsonAiSettingsProvider(folder);

            provider.SaveSettings(new AiSettings { PromptProfile = "enxuto" });
            provider.GetSettings().PromptProfile.Should().Be("enxuto");

            // Arquivo gravado antes do campo existir: cai no vazio (= perfil padrão).
            File.WriteAllText(Path.Combine(folder, "ai_settings.json"), "{ \"ApiKey\": \"k\" }");
            provider.GetSettings().PromptProfile.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void GetSettings_QuandoArquivoNaoExiste_RetornaSettingsVazias()
    {
        string path = FilePath();
        string? original = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            if (File.Exists(path)) File.Delete(path);

            var provider = new JsonAiSettingsProvider();
            var settings = provider.GetSettings();

            settings.ApiKey.Should().BeEmpty();
            settings.Model.Should().BeEmpty();
            settings.BaseUrl.Should().BeEmpty();
        }
        finally
        {
            if (original != null) File.WriteAllText(path, original);
        }
    }
}
