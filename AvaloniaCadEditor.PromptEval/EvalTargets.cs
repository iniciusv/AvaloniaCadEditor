using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Harness.Agents;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>Um destino das execuções: o endpoint (perfil do ai_settings.json ou as variáveis de ambiente), o modelo e os limites do harness.</summary>
internal sealed record EvalTarget(string Endpoint, bool IsLocal, LlmOptions Options, AgentLimits Limits)
{
    public const string EnvironmentEndpoint = "env";
}

/// <summary>
/// Monta os destinos a partir de <c>--endpoints</c>, <c>--local</c> e <c>--models</c>. Sem eles (e sem
/// <c>perfil:modelo</c>), vale o de antes: DEEPSEEK_* do ambiente, um destino por modelo.
/// </summary>
internal static class EvalTargets
{
    /// <summary>Id do perfil avulso criado por <c>--local</c>.</summary>
    public const string LocalEndpoint = "local";

    /// <summary>Os perfis do ai_settings.json e, com <c>--local</c>, um perfil local avulso (que não é gravado).</summary>
    private static AiSettings LoadProfiles(EvalOptions options, Func<AiSettings> loadSettings)
    {
        var settings = AiSettingsMigration.Upgrade(loadSettings());
        if (options.LocalUrl != null)
        {
            settings.Profiles.RemoveAll(p => string.Equals(p.Id, LocalEndpoint, StringComparison.OrdinalIgnoreCase));
            var local = AiEndpointProfile.Local(LocalEndpoint, "Local (--local)");
            local.BaseUrl = options.LocalUrl;
            settings.Profiles.Add(local);
        }
        return settings;
    }

    /// <remarks>
    /// "perfil:modelo" só vale quando o prefixo é o id de um perfil existente: "qwen3:8b" (tag do
    /// Ollama) continua sendo um modelo.
    /// </remarks>
    public static (List<EvalTarget>? Targets, string? Error) Build(EvalOptions options, LlmOptions environment, Func<AiSettings> loadSettings)
    {
        bool needsSettings = options.Endpoints.Count > 0 || options.LocalUrl != null || options.Models.Any(m => m.Contains(':'));
        var settings = needsSettings ? LoadProfiles(options, loadSettings) : null;
        bool IsQualified(string model) =>
            settings != null && model.Contains(':') &&
            settings.Profiles.Any(p => string.Equals(p.Id, model.Split(':', 2)[0], StringComparison.OrdinalIgnoreCase));

        var qualified = options.Models.Where(IsQualified).ToList();
        var endpoints = options.Endpoints
            .Concat(options.LocalUrl != null ? new[] { LocalEndpoint } : Array.Empty<string>())
            .Concat(qualified.Select(m => m.Split(':', 2)[0]))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (endpoints.Count == 0)
        {
            var models = options.Models.Count > 0 ? options.Models : new List<string> { environment.Model };
            return (models.Select(model => new EvalTarget(EvalTarget.EnvironmentEndpoint, false, With(environment, model), AgentLimits.Remote)).ToList(), null);
        }

        var plainModels = options.Models.Where(m => !qualified.Contains(m)).ToList();
        var targets = new List<EvalTarget>();
        foreach (string id in endpoints)
        {
            var profile = settings!.Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
                return (null, $"Perfil de endpoint desconhecido: {id}. Existentes: {string.Join(", ", settings.Profiles.Select(p => p.Id))}");

            var models = qualified.Where(m => string.Equals(m.Split(':', 2)[0], id, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Split(':', 2)[1])
                .Concat(plainModels)
                .ToList();
            if (models.Count == 0 && profile.Model.Length > 0) models.Add(profile.Model);
            if (models.Count == 0) return (null, $"{id}: diga o modelo com --models (os do servidor: GET {profile.BaseUrl}/models).");

            foreach (string model in models)
            {
                var candidate = profile.Clone();
                candidate.Model = model;
                var (resolved, error) = AiEndpointOptions.Resolve(candidate, options.Reasoning);
                if (resolved == null) return (null, $"{id}: {error}");
                targets.Add(new EvalTarget(profile.Id, profile.IsLocal, resolved, profile.IsLocal ? AgentLimits.Local : AgentLimits.Remote));
            }
        }
        return (targets, null);
    }

    /// <summary>O perfil do Inspetor (<c>--inspector</c>), com o modelo dele.</summary>
    public static (EvalTarget? Inspector, string? Error) Inspector(EvalOptions options, Func<AiSettings> loadSettings)
    {
        if (options.InspectorEndpoint == null) return (null, null);

        var settings = LoadProfiles(options, loadSettings);
        var profile = settings.Profiles.FirstOrDefault(p => string.Equals(p.Id, options.InspectorEndpoint, StringComparison.OrdinalIgnoreCase));
        if (profile == null) return (null, $"Perfil do Inspetor desconhecido: {options.InspectorEndpoint}.");

        var (resolved, error) = AiEndpointOptions.Resolve(profile, options.Reasoning);
        return resolved == null
            ? (null, $"Inspetor: {error}")
            : (new EvalTarget(profile.Id, profile.IsLocal, resolved, profile.IsLocal ? AgentLimits.Local : AgentLimits.Remote), null);
    }

    private static LlmOptions With(LlmOptions source, string model) => new()
    {
        ApiKey = source.ApiKey,
        ApiKeyRequired = source.ApiKeyRequired,
        BaseUrl = source.BaseUrl,
        Model = model,
        Temperature = source.Temperature,
        MaxTokens = source.MaxTokens,
        EnableThinkingMode = source.EnableThinkingMode,
        Thinking = source.Thinking,
        ReasoningEffort = source.ReasoningEffort,
        RequestTimeout = source.RequestTimeout
    };
}
