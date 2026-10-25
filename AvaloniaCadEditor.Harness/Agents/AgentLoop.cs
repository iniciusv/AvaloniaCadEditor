using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Harness.Protocol;
using AvaloniaCadEditor.Harness.Tools;

namespace AvaloniaCadEditor.Harness.Agents;

/// <summary>Envia a conversa com as ferramentas disponíveis (lista vazia = sem <c>tools</c>).</summary>
public delegate Task<LlmResult> ToolSendDelegate(IReadOnlyList<ChatMessage> messages, LlmOptions options, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken);

/// <summary>Como as ferramentas chegam ao modelo.</summary>
public enum ToolMode
{
    /// <summary>Function calling do servidor (<c>tools</c> / <c>tool_calls</c>).</summary>
    Native,

    /// <summary>Blocos <c>```tool</c> no texto (<see cref="TextToolProtocol"/>).</summary>
    Text
}

/// <summary>Uma chamada de ferramenta executada no turno.</summary>
public sealed record AgentStep(string Tool, string ArgumentsJson, string Result, bool Cached, TimeSpan Duration);

/// <summary>O fim de uma rodada do agente: a última resposta do modelo (sem chamadas) e o que foi feito.</summary>
public sealed record AgentRunResult(
    LlmResult Final,
    IReadOnlyList<AgentStep> Steps,
    int PromptTokens,
    int CompletionTokens,
    bool HitLimit,
    ToolMode Mode);

/// <summary>
/// O harness em si: manda a conversa, executa as ferramentas que o modelo pedir, devolve os
/// resultados e repete até uma resposta sem chamadas (ou até os limites). O modo das ferramentas
/// (<see cref="Mode"/>) cai de nativo para texto quando o servidor recusa <c>tools</c>, e fica assim.
/// </summary>
public sealed class AgentLoop
{
    private const string AlreadyQueried = "(já consultado nesta revisão — mesmo resultado)\n";
    private const string LimitMessage =
        "Limite de passos/tempo do turno atingido: não chame mais ferramentas. Responda agora com o resultado final " +
        "(o que foi feito, e blocos ```cadscript completos se ainda faltar alterar algum arquivo).";

    private readonly ToolSendDelegate _send;

    public AgentLoop(ToolSendDelegate? send = null)
    {
        _send = send ?? DefaultSend;
    }

    public ToolMode Mode { get; private set; } = ToolMode.Native;

    /// <summary>Volta ao modo nativo (outro servidor/perfil).</summary>
    public void ResetMode() => Mode = ToolMode.Native;

    private static Task<LlmResult> DefaultSend(IReadOnlyList<ChatMessage> messages, LlmOptions options, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
        new LlmClient().SendConversationAsync(messages, options, cancellationToken: ct, tools: tools.Count == 0 ? null : tools);

    /// <param name="history">A conversa; recebe as mensagens assistant/tool do turno, inclusive a resposta final.</param>
    public async Task<AgentRunResult> RunAsync(List<ChatMessage> history, LlmOptions options, ToolRegistry tools, CadToolContext context,
        AgentLimits limits, CancellationToken cancellationToken)
    {
        var steps = new List<AgentStep>();
        var cache = new Dictionary<(int Revision, string Name, string Args), string>();
        var clock = Stopwatch.StartNew();
        int promptTokens = 0, completionTokens = 0;
        bool limitAnnounced = false;
        if (Mode == ToolMode.Text) EnsureTextInstructions(history, tools);

        while (true)
        {
            bool overLimit = steps.Count >= limits.MaxToolSteps || clock.Elapsed >= limits.MaxDuration;

            LlmResult raw = await SendAsync(history, options, tools, cancellationToken);
            promptTokens += raw.PromptTokens;
            completionTokens += raw.CompletionTokens;

            var calls = raw.ToolCalls;
            if (Mode == ToolMode.Native && calls.Count == 0 && TextToolProtocol.Parse(raw.Content) is { Count: > 0 } written)
            {
                // O modelo escreveu a chamada no texto em vez de usar tool_calls: segue no protocolo em texto.
                Mode = ToolMode.Text;
                EnsureTextInstructions(history, tools);
                calls = written;
            }
            else if (Mode == ToolMode.Text)
            {
                calls = TextToolProtocol.Parse(raw.Content);
            }

            if (calls.Count == 0 || (overLimit && limitAnnounced))
            {
                history.Add(new ChatMessage { Role = "assistant", Content = raw.Content, ReasoningContent = raw.ReasoningContent });
                return new AgentRunResult(raw, steps, promptTokens, completionTokens, overLimit, Mode);
            }

            bool native = Mode == ToolMode.Native && raw.ToolCalls.Count > 0;
            history.Add(new ChatMessage
            {
                Role = "assistant",
                Content = raw.Content,
                ReasoningContent = raw.ReasoningContent,
                ToolCalls = native ? raw.ToolCalls : Array.Empty<ToolCall>()
            });

            var results = new List<(ToolCall Call, string Result)>();
            foreach (var call in calls)
            {
                string result;
                if (overLimit)
                {
                    result = LimitMessage;
                }
                else
                {
                    result = await ExecuteAsync(call, tools, context, cache, steps, cancellationToken);
                }
                results.Add((call, result));
            }
            limitAnnounced |= overLimit;

            if (native)
            {
                foreach (var (call, result) in results)
                    history.Add(new ChatMessage { Role = "tool", ToolCallId = call.Id, Content = result });
            }
            else
            {
                history.Add(new ChatMessage { Role = "user", Content = TextToolProtocol.FormatResults(results) });
            }
        }
    }

    private async Task<LlmResult> SendAsync(List<ChatMessage> history, LlmOptions options, ToolRegistry tools, CancellationToken cancellationToken)
    {
        if (Mode == ToolMode.Text) return await _send(history, options, Array.Empty<ToolDefinition>(), cancellationToken);

        try
        {
            return await _send(history, options, tools.Definitions(), cancellationToken);
        }
        catch (HttpRequestException ex) when (RejectsTools(ex))
        {
            Mode = ToolMode.Text;
            EnsureTextInstructions(history, tools);
            return await _send(history, options, Array.Empty<ToolDefinition>(), cancellationToken);
        }
    }

    /// <summary>Erro 4xx que fala de tools/functions: o servidor (ou o modelo carregado) não aceita function calling.</summary>
    public static bool RejectsTools(HttpRequestException ex) =>
        ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotImplemented
        && (ex.Message.Contains("tool", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("function", StringComparison.OrdinalIgnoreCase));

    private static void EnsureTextInstructions(List<ChatMessage> history, ToolRegistry tools)
    {
        if (history.Any(m => m.IsUser && m.Content.StartsWith("### FERRAMENTAS (protocolo em texto)", StringComparison.Ordinal))) return;
        // Entra antes da última mensagem do usuário, para o pedido continuar sendo o último.
        int index = history.FindLastIndex(m => m.IsUser);
        var message = new ChatMessage { Role = "user", Content = TextToolProtocol.Instructions(tools) };
        if (index < 0) history.Add(message); else history.Insert(index, message);
    }

    private static async Task<string> ExecuteAsync(ToolCall call, ToolRegistry tools, CadToolContext context,
        Dictionary<(int, string, string), string> cache, List<AgentStep> steps, CancellationToken cancellationToken)
    {
        var tool = tools.Find(call.Name);
        if (tool == null)
        {
            string unknown = $"Erro: ferramenta desconhecida '{call.Name}'. Disponíveis: {string.Join(", ", tools.All.Select(t => t.Name).OrderBy(n => n))}.";
            steps.Add(new AgentStep(call.Name, call.ArgumentsJson, unknown, false, TimeSpan.Zero));
            return unknown;
        }

        string args = Normalize(call.ArgumentsJson);
        var key = (context.Revision, tool.Name, args);
        if (tool.IsReadOnly && cache.TryGetValue(key, out var cached))
        {
            steps.Add(new AgentStep(tool.Name, args, cached, true, TimeSpan.Zero));
            return AlreadyQueried + cached;
        }

        var clock = Stopwatch.StartNew();
        string result;
        try
        {
            using var doc = JsonDocument.Parse(args);
            result = await tool.ExecuteAsync(doc.RootElement, context, cancellationToken);
        }
        catch (JsonException ex)
        {
            result = $"Erro: os argumentos não são JSON válido ({ex.Message}).";
        }
        catch (ToolArgumentException ex)
        {
            result = $"Erro: argumento inválido: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = $"Erro ao executar {tool.Name}: {ex.Message}";
        }

        if (tool.IsReadOnly) cache[(context.Revision, tool.Name, args)] = result;
        steps.Add(new AgentStep(tool.Name, args, result, false, clock.Elapsed));
        return result;
    }

    /// <summary>Mesmos argumentos escritos de outro jeito (espaços, ordem) contam como a mesma consulta.</summary>
    private static string Normalize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return doc.RootElement.GetRawText();
            var ordered = doc.RootElement.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToDictionary(p => p.Name, p => p.Value.Clone());
            return JsonSerializer.Serialize(ordered);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
