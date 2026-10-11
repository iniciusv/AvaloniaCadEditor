using AvaloniaCadEditor.Application.Ai.Recording;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>
/// Um cenário de avaliação: uma conversa (gravada pelo chat ou montada a partir de uma fixture) e as
/// verificações que a resposta deve cumprir. Cada round é reexecutado de forma independente.
/// </summary>
/// <param name="Source">"fixture" ou "gravado".</param>
internal sealed record EvalCase(string Name, string Source, string? Description, AiConversationRecord Record, CaseChecks Checks);
