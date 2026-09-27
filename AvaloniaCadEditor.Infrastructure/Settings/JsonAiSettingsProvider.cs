using System;
using System.IO;
using System.Text.Json;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Infrastructure.Settings;

/// <summary>
/// Persiste a configuração da IA (chave de API, modelo, URL base) em
/// %AppData%/AvaloniaCadEditor/ai_settings.json, para que sobreviva entre execuções do app.
/// </summary>
public class JsonAiSettingsProvider : IAiSettingsProvider
{
    private readonly string _filePath;

    public JsonAiSettingsProvider()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "AvaloniaCadEditor");
        Directory.CreateDirectory(appFolder);
        _filePath = Path.Combine(appFolder, "ai_settings.json");
    }

    public AiSettings GetSettings()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<AiSettings>(json);
                if (settings != null)
                    return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AiSettings] Falha ao ler arquivo de configurações: {ex.Message}");
            }
        }

        return new AiSettings();
    }

    public void SaveSettings(AiSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiSettings] Falha ao salvar arquivo de configurações: {ex.Message}");
        }
    }
}
