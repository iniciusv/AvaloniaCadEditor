using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniaCadEditor.Application.Ai.Recording;

/// <summary>
/// Acumula os rounds da conversa em andamento no chat, na ordem, para poder gravá-la como
/// <see cref="AiConversationRecord"/>. Acompanha o chat: turno apagado sai, "New chat" limpa.
/// </summary>
public sealed class AiConversationRecorder
{
    private readonly List<(int TurnId, RecordedRound Round)> _rounds = new();

    public int Count => _rounds.Count;

    /// <param name="turnId">Id do turno na interface (o mesmo usado para apagá-lo).</param>
    /// <param name="sentUserMessage">A mensagem de usuário como foi enviada à IA.</param>
    /// <param name="rawResponse">O conteúdo bruto da resposta final da IA.</param>
    public void Record(int turnId, string instruction, CadScriptContext context, string sentUserMessage,
        CadScriptTurnResult result, string rawResponse)
    {
        Remove(turnId);
        _rounds.Add((turnId, new RecordedRound
        {
            Instruction = instruction,
            Context = RecordedContext.From(context),
            SentUserMessage = sentUserMessage,
            Response = new RecordedResponse
            {
                RawContent = rawResponse,
                ChatText = result.ChatText,
                Changes = result.Changes.ToList(),
                IsValid = result.IsValid,
                Error = result.Error,
                Attempts = result.Attempts,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens
            }
        }));
    }

    public void Remove(int turnId) => _rounds.RemoveAll(r => r.TurnId == turnId);

    public void Clear() => _rounds.Clear();

    public AiConversationRecord ToRecord(string modelId, string profileId, DateTimeOffset createdAt) => new()
    {
        CreatedAt = createdAt,
        ModelId = modelId,
        ProfileId = profileId,
        Rounds = _rounds.Select(r => r.Round).ToList()
    };
}
