namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>
/// Uma opção selecionável no combo de modelo do chat de IA: um nome amigável
/// e o id exato aceito pelo campo "model" da API da DeepSeek.
/// </summary>
public sealed record AiModelOption(string DisplayName, string ModelId)
{
    public override string ToString() => DisplayName;
}
