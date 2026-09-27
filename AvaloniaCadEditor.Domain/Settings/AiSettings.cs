namespace AvaloniaCadEditor.Domain.Settings;

/// <summary>
/// Configuração da IA (DeepSeek) editável pela interface e persistida em disco entre execuções.
/// Campos vazios significam "usar o default do <c>LlmOptions</c>".
/// </summary>
public class AiSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
