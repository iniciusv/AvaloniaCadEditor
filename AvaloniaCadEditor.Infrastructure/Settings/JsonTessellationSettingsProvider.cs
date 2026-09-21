using System;
using System.IO;
using System.Text.Json;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Infrastructure.Settings;

public class JsonTessellationSettingsProvider : ITessellationSettingsProvider
{
    private readonly string _filePath;

    public JsonTessellationSettingsProvider()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "AvaloniaCadEditor");
        Directory.CreateDirectory(appFolder);
        _filePath = Path.Combine(appFolder, "tessellation_settings.json");
    }

    public TessellationSettings GetSettings()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<TessellationSettings>(json);
                if (settings != null)
                    return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings] Falha ao ler arquivo de configurações: {ex.Message}");
            }
        }

        // Default fallback
        var defaultSettings = new TessellationSettings();
        SaveSettings(defaultSettings);
        return defaultSettings;
    }

    public void SaveSettings(TessellationSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] Falha ao salvar arquivo de configurações: {ex.Message}");
        }
    }
}
