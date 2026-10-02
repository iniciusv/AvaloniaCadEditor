using System;
using System.IO;
using System.Text.Json;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Infrastructure.Settings;

public class JsonLastSceneProvider : ILastSceneProvider
{
    private readonly string _filePath;

    public JsonLastSceneProvider()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "AvaloniaCadEditor");
        Directory.CreateDirectory(appFolder);
        _filePath = Path.Combine(appFolder, "last_scene.json");
    }

    public string? GetLastScenePath()
    {
        if (!File.Exists(_filePath)) return null;
        try
        {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<LastSceneSettings>(json);
            return settings?.FolderPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] Falha ao ler a última cena: {ex.Message}");
            return null;
        }
    }

    public void SetLastScenePath(string folderPath)
    {
        try
        {
            var json = JsonSerializer.Serialize(new LastSceneSettings { FolderPath = folderPath }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] Falha ao salvar a última cena: {ex.Message}");
        }
    }
}
