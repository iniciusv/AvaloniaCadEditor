using AiCore.Abstractions;
using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Context;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Harness.Agents;
using AvaloniaCadEditor.Harness.Inspection;
using AvaloniaCadEditor.Harness.Tools;
using AvaloniaCadEditor.Harness.Tools.Query;

namespace AvaloniaCadEditor.Harness;

/// <summary>
/// Conversa de CadScript com ferramentas: o modelo consulta a geometria e edita uma cópia de
/// trabalho pelo <see cref="AgentLoop"/>, e no fim do turno a cópia é validada e avaliada como
/// na <see cref="CadScriptChatSession"/> — com até <see cref="MaxAttempts"/> rodadas de correção.
/// Para a UI é só outra <see cref="ICadScriptChatSession"/>: um diff no fim, undo igual.
/// </summary>
public sealed class HarnessChatSession : ICadScriptChatSession
{
    /// <summary>1 rodada + até 2 correções depois da validação final.</summary>
    public const int MaxAttempts = 3;

    private readonly IAiProtocol<CadScriptContext, CadScriptChanges> _protocol;
    private readonly WorkingCopyEvaluator _workingCopy;
    private readonly SceneInspectionService _inspection;
    private readonly ToolRegistry _tools;
    private readonly AgentLoop _loop;
    private readonly TurnHistory _history = new();
    private readonly InspectorAgent? _inspector;
    private LlmOptions _options;

    public HarnessChatSession(
        LlmOptions options,
        IAiProtocol<CadScriptContext, CadScriptChanges> protocol,
        ISceneEvaluator evaluator,
        IGeometryKernel kernel,
        AgentLimits? limits = null,
        ToolRegistry? tools = null,
        ToolSendDelegate? send = null,
        InspectorAgent? inspector = null,
        bool preInspection = true)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _workingCopy = new WorkingCopyEvaluator(evaluator ?? throw new ArgumentNullException(nameof(evaluator)));
        _inspection = new SceneInspectionService(kernel ?? throw new ArgumentNullException(nameof(kernel)));
        _tools = tools ?? ToolRegistry.Default();
        _inspector = inspector;
        PreInspection = preInspection;
        if (inspector != null) _tools = new ToolRegistry(_tools.All.Append(new InspectTool(inspector)));
        _loop = new AgentLoop(send);
        Limits = limits ?? AgentLimits.Remote;
        ResetHistory();
    }

    public IReadOnlyList<ChatMessage> History => _history.Messages;

    public bool KeepHistory { get; set; } = true;

    public AgentLimits Limits { get; set; }

    /// <summary>As ferramentas chamadas no último turno, na ordem (para a UI mostrar).</summary>
    public IReadOnlyList<AgentStep> LastTurnSteps { get; private set; } = Array.Empty<AgentStep>();

    /// <summary>Como as ferramentas estão indo ao modelo (cai para texto se o servidor recusar tools).</summary>
    public ToolMode Mode => _loop.Mode;

    /// <summary>Rodar o Inspetor antes de cada pedido (só vale com Inspetor).</summary>
    public bool PreInspection { get; set; }

    /// <summary>O Inspetor configurado, se houver.</summary>
    public InspectorAgent? Inspector => _inspector;

    /// <summary>O relatório da pré-inspeção do último turno.</summary>
    public InspectorReport? LastPreInspection { get; private set; }

    public void NewConversation() => ResetHistory();

    public void UpdateOptions(LlmOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loop.ResetMode();
    }

    public void DeleteTurn(int turnIndex) => _history.DeleteTurn(turnIndex);

    private void ResetHistory() => _history.Reset(_protocol.GetSystemPrompt() + "\n\n" + HarnessPrompt.Tools);

    public async Task<CadScriptTurnResult> SendAsync(string instruction, CadScriptContext context, CancellationToken cancellationToken = default)
    {
        if (!KeepHistory) ResetHistory();

        var toolContext = new CadToolContext(context, _workingCopy, _inspection);
        string request = _protocol.BuildUserMessage(context, instruction);

        LastPreInspection = null;
        if (_inspector != null && PreInspection)
        {
            LastPreInspection = await _inspector.InspectAsync(request, toolContext, cancellationToken);
            request += $"\n\n### NOTAS GEOMÉTRICAS (do Inspetor, {_inspector.ProfileName})\n{LastPreInspection.Summary}";
        }

        _history.BeginTurn();
        _history.Messages.Add(new ChatMessage { Role = "user", Content = request });
        var steps = new List<AgentStep>();
        int promptTokens = 0, completionTokens = 0;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var run = await _loop.RunAsync(_history.Messages, _options, _tools, toolContext, Limits, cancellationToken);
            steps.AddRange(run.Steps);
            LastTurnSteps = steps;
            promptTokens += run.PromptTokens;
            completionTokens += run.CompletionTokens;

            var parsed = _protocol.ParseResponse(run.Final.Content);
            string? error = null;
            if (parsed.HasPayload)
            {
                var (changes, scopeError) = AiEditScopePolicy.ResolveEdits(parsed.Payload!, context);
                if (scopeError == null) toolContext.ApplyChanges(changes);
                error = scopeError;
            }

            if (error == null && toolContext.Changes.Count == 0)
            {
                return Result(parsed.ChatMessage, run, toolContext, isValid: true, null, 0, attempt, promptTokens, completionTokens);
            }

            WorkingCopyCheck? check = null;
            if (error == null)
            {
                check = await toolContext.CheckAsync(cancellationToken);
                error = check.Error;
            }

            if (error == null)
            {
                return Result(parsed.ChatMessage, run, toolContext, isValid: true, null, check!.Evaluation!.Features.Count, attempt, promptTokens, completionTokens);
            }

            if (attempt == MaxAttempts)
            {
                return Result(parsed.ChatMessage, run, toolContext, isValid: false, error, 0, attempt, promptTokens, completionTokens);
            }

            var fixContext = WorkingCopyEvaluator.WithProposedChanges(context, toolContext.Changes) with { ErrorFeedback = error };
            _history.Messages.Add(new ChatMessage
            {
                Role = "user",
                Content = _protocol.BuildUserMessage(fixContext,
                    "As alterações do turno falharam ao compilar/executar (o erro está acima; os arquivos acima já as incluem). " +
                    "Corrija com as ferramentas (replace_in_file, evaluate_scene) ou com blocos ```cadscript completos.")
            });
        }

        throw new InvalidOperationException("Loop de tentativas do HarnessChatSession terminou sem retornar um resultado.");
    }

    private static CadScriptTurnResult Result(string chatText, AgentRunResult run, CadToolContext context, bool isValid, string? error,
        int featureCount, int attempt, int promptTokens, int completionTokens) => new()
    {
        ChatText = chatText,
        Changes = context.Changes.ToList(),
        Reasoning = run.Final.ReasoningContent,
        IsValid = isValid,
        Error = error,
        FeatureCount = featureCount,
        Attempts = attempt,
        PromptTokens = promptTokens,
        CompletionTokens = completionTokens
    };
}
