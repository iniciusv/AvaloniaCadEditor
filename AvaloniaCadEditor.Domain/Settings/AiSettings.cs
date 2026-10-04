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

    /// <summary>Manter o histórico da conversa entre turnos (multi-round). Desligado = cada envio é independente (stateless).</summary>
    public bool KeepConversationHistory { get; set; } = true;

    /// <summary>Habilitar o reasoning/thinking mode (em modelos compatíveis). Desligado por padrão.</summary>
    public bool EnableReasoning { get; set; } = false;
}
