namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>
/// Um item do contexto que foi efetivamente enviado à IA em uma mensagem (arquivo ou sólido selecionado),
/// mostrado como chip somente-leitura dentro do balão do usuário. Registro histórico: não tem comandos.
/// </summary>
public sealed class AiSentContextItem
{
    public AiSentContextItem(string displayName, string toolTip, bool isSolid)
    {
        DisplayName = displayName;
        ToolTip = toolTip;
        IsSolid = isSolid;
    }

    public string DisplayName { get; }
    public string ToolTip { get; }

    /// <summary>true = sólido/peça selecionado; false = arquivo de contexto.</summary>
    public bool IsSolid { get; }
}
