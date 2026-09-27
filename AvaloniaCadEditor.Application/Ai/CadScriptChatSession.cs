using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiCore.Abstractions;
using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Resultado de um round completo de conversa (pode ter incluído correções automáticas internas).
/// </summary>
public class CadScriptTurnResult
{
    public string ChatText { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? Reasoning { get; init; }
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public int FeatureCount { get; init; }
    public int Attempts { get; init; } = 1;
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
}

/// <summary>
/// Orquestra uma conversa multi-round com a IA para gerar/editar o CadScript da cena.
/// Quando o script retornado não compila/executa, reenvia o erro à IA automaticamente
/// (até <see cref="MaxAttempts"/> tentativas no total) antes de reportar falha.
/// </summary>
public class CadScriptChatSession
{
    /// <summary>1 tentativa inicial + até 2 correções automáticas.</summary>
    public const int MaxAttempts = 3;

    public delegate Task<LlmResult> SendDelegate(IReadOnlyList<ChatMessage> messages, LlmOptions options, CancellationToken cancellationToken);

    private LlmOptions _options;
    private readonly IAiProtocol<CadScriptContext, string> _protocol;
    private readonly ISceneEvaluator _evaluator;
    private readonly SendDelegate _send;
    private readonly List<ChatMessage> _history = new();

    public IReadOnlyList<ChatMessage> History => _history;

    public CadScriptChatSession(
        LlmOptions options,
        IAiProtocol<CadScriptContext, string> protocol,
        ISceneEvaluator evaluator,
        SendDelegate? send = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _send = send ?? DefaultSend;
        ResetHistory();
    }

    private static Task<LlmResult> DefaultSend(IReadOnlyList<ChatMessage> messages, LlmOptions options, CancellationToken cancellationToken)
        => new LlmClient().SendConversationAsync(messages, options, cancellationToken: cancellationToken);

    /// <summary>Começa uma nova conversa, descartando o histórico (mas mantendo as opções da IA).</summary>
    public void NewConversation() => ResetHistory();

    /// <summary>Troca a chave/modelo/URL em uso (ex.: após o usuário editar as configurações na UI).</summary>
    public void UpdateOptions(LlmOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private void ResetHistory()
    {
        _history.Clear();
        _history.Add(new ChatMessage { Role = "system", Content = _protocol.GetSystemPrompt() });
    }

    public async Task<CadScriptTurnResult> SendAsync(string instruction, CadScriptContext context, CancellationToken cancellationToken = default)
    {
        _history.Add(new ChatMessage { Role = "user", Content = _protocol.BuildUserMessage(context, instruction) });

        int promptTokens = 0;
        int completionTokens = 0;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            LlmResult raw = await _send(_history, _options, cancellationToken);
            promptTokens += raw.PromptTokens;
            completionTokens += raw.CompletionTokens;

            // O reasoning_content não é reenviado no histórico: guardamos apenas para exibição,
            // a API espera receber de volta somente o "content" das respostas anteriores.
            _history.Add(new ChatMessage { Role = "assistant", Content = raw.Content });

            var parsed = _protocol.ParseResponse(raw.Content);

            if (!parsed.HasPayload)
            {
                return new CadScriptTurnResult
                {
                    ChatText = parsed.ChatMessage,
                    Reasoning = raw.ReasoningContent,
                    IsValid = true,
                    Attempts = attempt,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens
                };
            }

            string code = parsed.Payload!;
            var syntaxResult = CadScriptValidator.Validate(code);
            SceneEvaluationResult? evalResult = syntaxResult.IsValid
                ? await _evaluator.EvaluateAsync(code)
                : null;

            if (syntaxResult.IsValid && evalResult!.Success)
            {
                return new CadScriptTurnResult
                {
                    ChatText = parsed.ChatMessage,
                    Code = code,
                    Reasoning = raw.ReasoningContent,
                    IsValid = true,
                    FeatureCount = evalResult.Features.Count,
                    Attempts = attempt,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens
                };
            }

            string error = !syntaxResult.IsValid
                ? syntaxResult.Error!
                : (evalResult!.ErrorMessage ?? "Erro desconhecido ao executar o script.");

            if (attempt == MaxAttempts)
            {
                return new CadScriptTurnResult
                {
                    ChatText = parsed.ChatMessage,
                    Code = code,
                    Reasoning = raw.ReasoningContent,
                    IsValid = false,
                    Error = error,
                    Attempts = attempt,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens
                };
            }

            var fixContext = context with { CurrentCode = code, ErrorFeedback = error };
            _history.Add(new ChatMessage
            {
                Role = "user",
                Content = _protocol.BuildUserMessage(
                    fixContext,
                    "O script anterior falhou ao compilar/executar. Corrija o problema e responda novamente com o SCRIPT COMPLETO corrigido.")
            });
        }

        throw new InvalidOperationException("Loop de tentativas do CadScriptChatSession terminou sem retornar um resultado.");
    }

    /// <summary>
    /// Carrega as opções da IA a partir da configuração persistida pela UI
    /// (<see cref="IAiSettingsProvider"/>), usando as variáveis de ambiente DEEPSEEK_API_KEY /
    /// DEEPSEEK_BASE_URL / DEEPSEEK_MODEL como fallback para cada campo deixado em branco.
    /// </summary>
    public static (LlmOptions? Options, string? Error) LoadOptions(IAiSettingsProvider settingsProvider)
    {
        var settings = settingsProvider.GetSettings();

        string? apiKey = !string.IsNullOrWhiteSpace(settings.ApiKey)
            ? settings.ApiKey
            : Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (null, "No DeepSeek API key configured. Set it in the AI chat panel (⚙ Settings) or via the DEEPSEEK_API_KEY environment variable.");
        }

        var options = new LlmOptions { ApiKey = apiKey };

        string? baseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? settings.BaseUrl
            : Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl)) options.BaseUrl = baseUrl;

        string? model = !string.IsNullOrWhiteSpace(settings.Model)
            ? settings.Model
            : Environment.GetEnvironmentVariable("DEEPSEEK_MODEL");
        if (!string.IsNullOrWhiteSpace(model)) options.Model = model;

        return (options, null);
    }

    /// <summary>
    /// Carrega as opções da IA apenas a partir de variáveis de ambiente
    /// (DEEPSEEK_API_KEY obrigatória; DEEPSEEK_BASE_URL e DEEPSEEK_MODEL opcionais).
    /// Mantido para uso sem UI (ex.: scripts/CI); a UI usa <see cref="LoadOptions"/>.
    /// </summary>
    public static (LlmOptions? Options, string? Error) LoadOptionsFromEnvironment()
    {
        string? apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (null, "Environment variable DEEPSEEK_API_KEY is not set. Set it and restart the application to use the AI chat.");
        }

        var options = new LlmOptions { ApiKey = apiKey };

        string? baseUrl = Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl)) options.BaseUrl = baseUrl;

        string? model = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL");
        if (!string.IsNullOrWhiteSpace(model)) options.Model = model;

        return (options, null);
    }
}
