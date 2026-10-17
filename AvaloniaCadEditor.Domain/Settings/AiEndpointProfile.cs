namespace AvaloniaCadEditor.Domain.Settings;

/// <summary>
/// Um endpoint de chat OpenAI-compatível: a DeepSeek ou um servidor local (Ollama, LM Studio,
/// llama.cpp). O chat usa o perfil ativo de <see cref="AiSettings"/>; trocar de perfil troca de
/// provedor sem perder a configuração dos outros.
/// </summary>
public class AiEndpointProfile
{
    public const string DeepSeekId = "deepseek";
    public const string DeepSeekKeyEnvVar = "DEEPSEEK_API_KEY";

    /// <summary>Identificador estável (usado por <see cref="AiSettings.ActiveProfileId"/> e por ferramentas externas).</summary>
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>URL base, com ou sem <c>/v1</c>. Vazio = a URL padrão da DeepSeek.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Id do modelo no servidor. Vazio = o modelo padrão da DeepSeek.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Chave de API. Vazio = lê de <see cref="ApiKeyEnvVar"/>; em perfil local, vazio = sem chave.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Variável de ambiente lida quando <see cref="ApiKey"/> está vazia.</summary>
    public string ApiKeyEnvVar { get; set; } = string.Empty;

    /// <summary>
    /// Servidor local: a chave é opcional e o campo <c>thinking</c> (da DeepSeek) não é enviado.
    /// </summary>
    public bool IsLocal { get; set; }

    /// <summary>Limite de tokens da resposta. 0 = omitir e usar o padrão do servidor.</summary>
    public int MaxTokens { get; set; }

    /// <summary>Tempo máximo de uma requisição, em segundos. 0 = o padrão do cliente.</summary>
    public int TimeoutSeconds { get; set; }

    public static AiEndpointProfile DeepSeek() => new()
    {
        Id = DeepSeekId,
        DisplayName = "DeepSeek",
        ApiKeyEnvVar = DeepSeekKeyEnvVar,
        MaxTokens = 384000
    };

    /// <summary>Perfil local com os padrões do Ollama; modelos locais grandes podem demorar, daí o timeout longo.</summary>
    public static AiEndpointProfile Local(string id, string displayName) => new()
    {
        Id = id,
        DisplayName = displayName,
        BaseUrl = "http://localhost:11434/v1",
        IsLocal = true,
        TimeoutSeconds = 1800
    };

    /// <summary>Id ainda não usado derivado do nome ("Ollama Qwen" → "ollama-qwen", "ollama-qwen-2", ...).</summary>
    public static string NewId(IEnumerable<AiEndpointProfile> existing, string displayName)
    {
        var used = existing.Select(p => p.Id).ToHashSet();
        string slug = new string(displayName.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        if (slug.Length == 0) slug = "perfil";

        string id = slug;
        for (int n = 2; used.Contains(id); n++) id = $"{slug}-{n}";
        return id;
    }

    public AiEndpointProfile Clone() => (AiEndpointProfile)MemberwiseClone();

    public override string ToString() => DisplayName.Length > 0 ? DisplayName : Id;
}
