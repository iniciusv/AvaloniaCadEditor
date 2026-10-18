using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Converte um <see cref="AiEndpointProfile"/> nas <see cref="LlmOptions"/> do cliente, e
/// consulta os modelos do servidor do perfil.
/// </summary>
public static class AiEndpointOptions
{
    /// <summary>
    /// Opções da chamada para o perfil. Campos vazios caem nas variáveis de ambiente
    /// (a chave em <see cref="AiEndpointProfile.ApiKeyEnvVar"/>; em perfis não locais, também
    /// DEEPSEEK_BASE_URL e DEEPSEEK_MODEL, como antes dos perfis existirem).
    /// </summary>
    public static (LlmOptions? Options, string? Error) Resolve(AiEndpointProfile profile, bool enableReasoning)
    {
        string apiKey = profile.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(profile.ApiKeyEnvVar))
            apiKey = Environment.GetEnvironmentVariable(profile.ApiKeyEnvVar) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(apiKey) && !profile.IsLocal)
        {
            string envHint = string.IsNullOrWhiteSpace(profile.ApiKeyEnvVar) ? "" : $" or via the {profile.ApiKeyEnvVar} environment variable";
            return (null, $"No API key configured for '{profile}'. Set it in the AI chat panel (⚙ Settings){envHint}.");
        }

        var options = new LlmOptions
        {
            ApiKey = apiKey.Trim(),
            ApiKeyRequired = !profile.IsLocal,
            EnableThinkingMode = enableReasoning,
            Thinking = profile.IsLocal ? ThinkingControl.None : ThinkingControl.Auto,
            MaxTokens = Math.Max(0, profile.MaxTokens),
            // Servidores locais não têm o toggle "thinking" da DeepSeek: o padrão OpenAI é reasoning_effort.
            ReasoningEffort = profile.IsLocal && !enableReasoning ? "none" : string.Empty
        };
        if (profile.TimeoutSeconds > 0) options.RequestTimeout = TimeSpan.FromSeconds(profile.TimeoutSeconds);

        string? baseUrl = NonEmpty(profile.BaseUrl) ?? (profile.IsLocal ? null : NonEmpty(Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL")));
        if (baseUrl != null) options.BaseUrl = baseUrl.Trim();
        // Sem isto, um perfil local sem URL cairia no default do LlmOptions (a DeepSeek).
        else if (profile.IsLocal) return (null, $"No base URL configured for the local profile '{profile}' (e.g. http://localhost:11434/v1).");

        string? model = NonEmpty(profile.Model) ?? (profile.IsLocal ? null : NonEmpty(Environment.GetEnvironmentVariable("DEEPSEEK_MODEL")));
        if (model != null) options.Model = model.Trim();
        else if (profile.IsLocal) return (null, $"No model configured for the local profile '{profile}'. Pick one in ⚙ Settings (↻ lists the server's models).");

        return (options, null);
    }

    /// <summary>
    /// Modelos instalados no servidor do perfil (<c>GET /v1/models</c>). Não exige modelo
    /// configurado: serve justamente para escolher um.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ListModelsAsync(AiEndpointProfile profile, LlmClient? client = null, CancellationToken cancellationToken = default)
    {
        var probe = profile.Clone();
        if (string.IsNullOrWhiteSpace(probe.Model)) probe.Model = "probe";

        var (options, error) = Resolve(probe, enableReasoning: false);
        if (options == null) throw new InvalidOperationException(error);

        options.RequestTimeout = TimeSpan.FromSeconds(15);
        return await (client ?? new LlmClient()).ListModelsAsync(options, cancellationToken);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
