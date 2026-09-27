namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Contexto de domínio passado à IA a cada round da conversa: o script C# vigente,
/// a feature atualmente selecionada na UI (se houver) e, em rounds de correção
/// automática, o erro retornado pela última tentativa.
/// </summary>
public sealed record CadScriptContext(
    string CurrentCode,
    string? SelectedFeatureName = null,
    string? ErrorFeedback = null);
