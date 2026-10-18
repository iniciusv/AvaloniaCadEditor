namespace AvaloniaCadEditor.Harness.Skills;

/// <summary>
/// Um modo de trabalho que se liga e desliga: um pacote de instruções que muda como o Editor conduz
/// o turno (perguntar antes de editar, conferir o resultado...). Não traz ferramentas nem código: só
/// texto, que entra no system prompt (skill ligada) ou num único pedido (<c>/id</c> no começo).
/// </summary>
/// <param name="Id">Nome curto, minúsculo e sem espaços: é o que vai no <c>harness_settings.json</c> e no <c>/id</c>.</param>
/// <param name="Title">O rótulo na interface.</param>
/// <param name="Description">Uma frase para o usuário decidir se liga (tooltip).</param>
/// <param name="Instructions">O texto para o modelo.</param>
public sealed record HarnessSkill(string Id, string Title, string Description, string Instructions)
{
    /// <summary>A seção que vai ao modelo.</summary>
    public string Section() => $"### SKILL: {Id} ({Title})\n{Instructions.Trim()}";
}
