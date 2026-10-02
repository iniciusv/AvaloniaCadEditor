using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiCore.Abstractions;
using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Application.Ai.Context;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Resultado de um round completo de conversa (pode ter incluído correções automáticas internas).
/// </summary>
public class CadScriptTurnResult
{
    public string ChatText { get; init; } = string.Empty;

    /// <summary>Conteúdo completo de cada arquivo alterado/criado pela resposta (vazio se ela só tinha texto).</summary>
    public IReadOnlyList<ScriptDocument> Changes { get; init; } = Array.Empty<ScriptDocument>();

    /// <summary>Código do primeiro arquivo alterado (atalho para o modo de arquivo único).</summary>
    public string? Code => Changes.Count > 0 ? Changes[0].Text : null;
    public string? Reasoning { get; init; }
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public int FeatureCount { get; init; }
    public int Attempts { get; init; } = 1;
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
}

/// <summary>
/// Orquestra uma conversa multi-round com a IA para gerar/editar o CadScript da cena — um arquivo
/// por vez, ou vários (os do <see cref="CadScriptContext.Scope"/>) num projeto.
/// Quando o código retornado não compila/executa, reenvia o erro à IA automaticamente
/// (até <see cref="MaxAttempts"/> tentativas no total) antes de reportar falha.
/// </summary>
public class CadScriptChatSession
{
    /// <summary>1 tentativa inicial + até 2 correções automáticas.</summary>
    public const int MaxAttempts = 3;

    public delegate Task<LlmResult> SendDelegate(IReadOnlyList<ChatMessage> messages, LlmOptions options, CancellationToken cancellationToken);

    private LlmOptions _options;
    private readonly IAiProtocol<CadScriptContext, CadScriptChanges> _protocol;
    private readonly ISceneEvaluator _evaluator;
    private readonly SendDelegate _send;
    private readonly List<ChatMessage> _history = new();

    public IReadOnlyList<ChatMessage> History => _history;

    public CadScriptChatSession(
        LlmOptions options,
        IAiProtocol<CadScriptContext, CadScriptChanges> protocol,
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

            var (changes, error) = AiEditScopePolicy.ResolveEdits(parsed.Payload!, context);
            SceneEvaluationResult? evalResult = null;
            if (error == null)
            {
                error = changes
                    .Select(c => (c.FilePath, Result: CadScriptValidator.Validate(c.Text, context.KindOf(c.FilePath))))
                    .Where(v => !v.Result.IsValid)
                    .Select(v => context.IsMultiFile ? $"{v.FilePath}: {v.Result.Error}" : v.Result.Error)
                    .FirstOrDefault();
            }
            if (error == null)
            {
                evalResult = await EvaluateAsync(changes, context);
                if (!evalResult.Success) error = evalResult.ErrorMessage ?? "Erro desconhecido ao executar o script.";
            }

            if (error == null)
            {
                return new CadScriptTurnResult
                {
                    ChatText = parsed.ChatMessage,
                    Changes = changes,
                    Reasoning = raw.ReasoningContent,
                    IsValid = true,
                    FeatureCount = evalResult!.Features.Count,
                    Attempts = attempt,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens
                };
            }

            if (attempt == MaxAttempts)
            {
                return new CadScriptTurnResult
                {
                    ChatText = parsed.ChatMessage,
                    Changes = changes,
                    Reasoning = raw.ReasoningContent,
                    IsValid = false,
                    Error = error,
                    Attempts = attempt,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens
                };
            }

            var fixContext = WithProposedChanges(context, changes) with { ErrorFeedback = error };
            _history.Add(new ChatMessage
            {
                Role = "user",
                Content = _protocol.BuildUserMessage(
                    fixContext,
                    context.IsMultiFile
                        ? "A resposta anterior falhou ao compilar/executar. Os arquivos acima já incluem as suas alterações. Corrija o problema e responda novamente com um bloco ```cadscript file=<caminho> COMPLETO por arquivo que precisar mudar."
                        : "O script anterior falhou ao compilar/executar. Corrija o problema e responda novamente com o SCRIPT COMPLETO corrigido.")
            });
        }

        throw new InvalidOperationException("Loop de tentativas do CadScriptChatSession terminou sem retornar um resultado.");
    }

    /// <summary>O contexto com as alterações propostas sobrepostas (para o round de correção).</summary>
    private static CadScriptContext WithProposedChanges(CadScriptContext context, IReadOnlyList<ScriptDocument> changes)
    {
        if (!context.IsMultiFile)
        {
            return context with { CurrentCode = changes.Count > 0 ? changes[0].Text : context.CurrentCode };
        }

        return context with
        {
            Scope = context.Scope! with { Documents = Overlay(context.Scope!.Documents, changes) },
            ProjectDocuments = context.ProjectDocuments == null ? null : Overlay(context.ProjectDocuments, changes)
        };
    }

    /// <summary><paramref name="documents"/> com o texto de <paramref name="changes"/> no lugar (e arquivos novos no fim).</summary>
    private static IReadOnlyList<ScriptDocument> Overlay(IReadOnlyList<ScriptDocument> documents, IReadOnlyList<ScriptDocument> changes)
    {
        var result = documents
            .Select(d => changes.FirstOrDefault(c => string.Equals(c.FilePath, d.FilePath, StringComparison.OrdinalIgnoreCase)) ?? d)
            .ToList();
        result.AddRange(changes.Where(c => !documents.Any(d => string.Equals(d.FilePath, c.FilePath, StringComparison.OrdinalIgnoreCase))));
        return result;
    }

    /// <summary>
    /// Avalia as alterações propostas no contexto certo. No modo multi-arquivo, a cena inteira
    /// (<see cref="CadScriptContext.ProjectDocuments"/>, com o arquivo de entrada real) é avaliada com as
    /// alterações sobrepostas. No modo de arquivo único, um script é avaliado sozinho (ou com os demais
    /// arquivos do projeto); um arquivo de peça não tem statements próprios para executar, então ganha um
    /// arquivo de entrada sintético que só instancia a peça (<c>Add(new Peca(...))</c>), forçando o
    /// <c>Build()</c> a rodar.
    /// </summary>
    private Task<SceneEvaluationResult> EvaluateAsync(IReadOnlyList<ScriptDocument> changes, CadScriptContext context)
    {
        if (context.IsMultiFile)
        {
            var project = context.ProjectDocuments ?? context.Scope!.Documents;
            return _evaluator.EvaluateAsync(Overlay(project, changes));
        }

        string code = changes[0].Text;
        string fileName = context.FileName ?? ScriptDocument.DefaultSceneFile;
        var others = context.ProjectDocuments ?? Array.Empty<ScriptDocument>();

        if (context.Kind != CadFileKind.PartFile)
        {
            if (others.Count == 0) return _evaluator.EvaluateAsync(code);

            var docs = new List<ScriptDocument> { new(fileName, code) };
            docs.AddRange(others.Where(d => !string.Equals(d.FilePath, fileName, StringComparison.OrdinalIgnoreCase)));
            return _evaluator.EvaluateAsync(docs);
        }

        var partDoc = new ScriptDocument(fileName, code);
        var types = PartSourceEditor.FindPartTypes(new[] { partDoc });
        if (types.Count != 1)
        {
            return Task.FromResult(new SceneEvaluationResult(Array.Empty<IFeature>(), false, types.Count == 0
                ? "O arquivo não declara nenhum record que herda de Part."
                : $"O arquivo declara {types.Count} peças; deveria declarar exatamente uma."));
        }

        var partDocs = new List<ScriptDocument> { partDoc };
        partDocs.AddRange(others.Where(d => !string.Equals(d.FilePath, fileName, StringComparison.OrdinalIgnoreCase)));

        string entryFile = "__CadScriptPartCheck.cs";
        while (partDocs.Any(d => string.Equals(d.FilePath, entryFile, StringComparison.OrdinalIgnoreCase)))
        {
            entryFile = "_" + entryFile;
        }
        partDocs.Add(new ScriptDocument(entryFile, PartSourceEditor.BuildAddStatement(types[0])));

        return _evaluator.EvaluateAsync(partDocs);
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
