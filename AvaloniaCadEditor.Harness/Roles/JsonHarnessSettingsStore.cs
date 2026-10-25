using System.Text.Json;

namespace AvaloniaCadEditor.Harness.Roles;

/// <summary>
/// Grava a configuração do harness em %AppData%/AvaloniaCadEditor/harness_settings.json, ao lado
/// do ai_settings.json do app (e sem misturar os dois).
/// </summary>
public sealed class JsonHarnessSettingsStore : IHarnessSettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    /// <param name="folder">Pasta do arquivo; os testes usam uma temporária.</param>
    public JsonHarnessSettingsStore(string? folder = null)
    {
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AvaloniaCadEditor");
        _filePath = Path.Combine(folder, "harness_settings.json");
    }

    public HarnessSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<HarnessSettings>(File.ReadAllText(_filePath)) ?? new HarnessSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[HarnessSettings] Falha ao ler {_filePath}: {ex.Message}");
        }
        return new HarnessSettings();
    }

    public void Save(HarnessSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, WriteOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[HarnessSettings] Falha ao salvar {_filePath}: {ex.Message}");
        }
    }
}
