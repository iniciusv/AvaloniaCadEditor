namespace AvaloniaCadEditor.Domain.Settings;

/// <summary>
/// Configuração da IA editável pela interface e persistida em disco entre execuções.
/// Desde a versão 2 do esquema, os endpoints ficam em <see cref="Profiles"/> e o chat usa o
/// <see cref="ActiveProfileId"/>; arquivos antigos são convertidos por <see cref="AiSettingsMigration"/>.
/// </summary>
public class AiSettings
{
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Versão do esquema. 0 = arquivo gravado antes dos perfis (o JSON não tinha o campo); por
    /// isso o default é 0, não <see cref="CurrentSchemaVersion"/>.
    /// </summary>
    public int SchemaVersion { get; set; }

    public List<AiEndpointProfile> Profiles { get; set; } = new();

    public string ActiveProfileId { get; set; } = string.Empty;

    /// <summary>Legado (esquema 0): migrado para o perfil "deepseek" e zerado em seguida.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Legado (esquema 0): migrado para o perfil "deepseek" e zerado em seguida.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Legado (esquema 0): migrado para o perfil "deepseek" e zerado em seguida.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Manter o histórico da conversa entre turnos (multi-round). Desligado = cada envio é independente (stateless).</summary>
    public bool KeepConversationHistory { get; set; } = true;

    /// <summary>
    /// Habilitar o reasoning/thinking mode (em modelos compatíveis). Desligado por padrão.
    /// O valor é enviado explicitamente à API como <c>thinking: { type: "enabled" | "disabled" }</c>:
    /// omitir o campo não desliga nada, já que o thinking mode é ligado por padrão no servidor.
    /// </summary>
    public bool EnableReasoning { get; set; } = false;

    /// <summary>Id do perfil de pré-prompt (system prompt) do chat. Vazio = o perfil padrão.</summary>
    public string PromptProfile { get; set; } = string.Empty;

    /// <summary>O perfil ativo, ou o primeiro se o id não existir. Null só se não houver perfis.</summary>
    public AiEndpointProfile? GetActiveProfile() =>
        Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles.FirstOrDefault();
}
