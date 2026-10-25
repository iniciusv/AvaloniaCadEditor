using AiCore.Models;

namespace AvaloniaCadEditor.Harness.Agents;

/// <summary>
/// O histórico da conversa agrupado por turno da UI. Um turno ocupa vários itens: o pedido, as
/// mensagens assistant(tool_calls) e tool, as correções automáticas e a resposta final. Apagar um
/// turno apaga todos eles, e os demais continuam coerentes.
/// </summary>
public sealed class TurnHistory
{
    private readonly List<int> _turnStarts = new();

    public List<ChatMessage> Messages { get; } = new();

    public void Reset(string systemPrompt)
    {
        Messages.Clear();
        _turnStarts.Clear();
        Messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });
    }

    /// <summary>Marca o início de um turno novo (antes de adicionar o pedido).</summary>
    public void BeginTurn() => _turnStarts.Add(Messages.Count);

    /// <param name="turnIndex">Posição ordinal do turno (0 = primeiro turno ainda presente).</param>
    public void DeleteTurn(int turnIndex)
    {
        if (turnIndex < 0 || turnIndex >= _turnStarts.Count) return;

        int start = _turnStarts[turnIndex];
        int end = turnIndex + 1 < _turnStarts.Count ? _turnStarts[turnIndex + 1] : Messages.Count;
        int removed = end - start;

        Messages.RemoveRange(start, removed);
        _turnStarts.RemoveAt(turnIndex);
        for (int i = turnIndex; i < _turnStarts.Count; i++) _turnStarts[i] -= removed;
    }
}
