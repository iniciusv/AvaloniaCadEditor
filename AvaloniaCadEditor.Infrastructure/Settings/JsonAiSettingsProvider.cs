using System;
using System.IO;
using System.Text.Json;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Infrastructure.Settings;

/// <summary>
/// Persiste a configuração da IA (perfis de endpoint, perfil ativo, opções do chat) em
/// %AppData%/AvaloniaCadEditor/ai_settings.json, para que sobreviva entre execuções do app.
/// Arquivos de esquemas antigos são convertidos na leitura (<see cref="AiSettingsMigration"/>).
/// </summary>
public class JsonAiSettingsProvider : IAiSettingsProvider
{
    private readonly string _filePath;

    /// <param name="folder">
    /// Pasta do arquivo. Por padrão, %AppData%/AvaloniaCadEditor. Existe para os testes poderem
    /// usar uma pasta temporária em vez do arquivo real do usuário.
    /// </param>
    public JsonAiSettingsProvider(string? folder = null)
    {
        folder ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AvaloniaCadEditor");
        _filePath = Path.Combine(folder, "ai_settings.json");
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
                    return AiSettingsMigration.Upgrade(settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AiSettings] Falha ao ler arquivo de configurações: {ex.Message}");
            }
        }

        return AiSettingsMigration.Upgrade(new AiSettings());
    }

    public void SaveSettings(AiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiSettings] Falha ao salvar arquivo de configurações: {ex.Message}");
        }
    }
}
