using AiCore.Models;
using AiCore.Services;

namespace AvaloniaCadEditor.Harness.Tests.Agents;

/// <summary>Uma chamada que o "modelo" recebeu: tamanho do histórico, ferramentas oferecidas e o modelo pedido.</summary>
internal sealed record ModelCall(int Messages, int Tools, IReadOnlyList<string> ToolNames, string Model, string LastUser);

/// <summary>
/// Um modelo roteirizado: cada chamada consome a próxima resposta da fila (uma função do histórico
/// e das ferramentas oferecidas). Serve de <see cref="Harness.Agents.ToolSendDelegate"/>.
/// </summary>
internal sealed class ScriptedModel
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, LlmResult>> _replies = new();
    private int _ids;

    public List<ModelCall> Calls { get; } = new();

    public ScriptedModel Then(Func<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, LlmResult> reply)
    {
        _replies.Enqueue(reply);
        return this;
    }

    public ScriptedModel ThenCall(string tool, string args = "{}")
    {
        string id = $"c{++_ids}";
        return Then((_, _) => new LlmResult { FinishReason = "tool_calls", ToolCalls = new[] { new ToolCall(id, tool, args) } });
    }

    public ScriptedModel ThenSay(string text) => Then((_, _) => new LlmResult { Content = text, FinishReason = "stop" });

    public Task<LlmResult> Send(IReadOnlyList<ChatMessage> messages, LlmOptions options, IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
    {
        Calls.Add(new ModelCall(messages.Count, tools.Count, tools.Select(t => t.Name).ToList(), options.Model,
            messages.LastOrDefault(m => m.IsUser)?.Content ?? string.Empty));
        if (_replies.Count == 0) throw new InvalidOperationException("O roteiro acabou.");
        return Task.FromResult(_replies.Dequeue()(messages, tools));
    }
}

/// <summary>Despacha cada chamada para o roteiro do modelo pedido (Editor e Inspetor usam modelos diferentes).</summary>
internal sealed class ModelRouter
{
    private readonly Dictionary<string, ScriptedModel> _models = new();

    public ModelRouter Add(string model, ScriptedModel script)
    {
        _models[model] = script;
        return this;
    }

    public Task<LlmResult> Send(IReadOnlyList<ChatMessage> messages, LlmOptions options, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
        _models[options.Model].Send(messages, options, tools, ct);
}
