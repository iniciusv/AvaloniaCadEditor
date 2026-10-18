namespace AvaloniaCadEditor.Harness.Roles;

/// <summary>
/// Configuração do harness, separada da do app (<c>harness_settings.json</c>): o app só conhece os
/// perfis de endpoint e o perfil ativo; quem decide papéis e ferramentas é o harness.
/// </summary>
public class HarnessSettings
{
    /// <summary>Usar o chat com ferramentas. Desligado = o chat de sempre (arquivo completo + auto-fix).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Papel → id do perfil de endpoint (<c>AiSettings.Profiles</c>). Sem Editor = o perfil ativo do
    /// chat; sem Inspector = sem Inspetor.
    /// </summary>
    public Dictionary<string, string> RoleProfiles { get; set; } = new();

    /// <summary>Rodar o Inspetor antes de cada pedido e mandar o resumo ao Editor ("Notas geométricas").</summary>
    public bool PreInspection { get; set; } = true;

    public string? ProfileFor(AgentRole role) =>
        RoleProfiles.TryGetValue(role.ToString(), out var id) && !string.IsNullOrWhiteSpace(id) ? id : null;

    public void SetProfile(AgentRole role, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) RoleProfiles.Remove(role.ToString());
        else RoleProfiles[role.ToString()] = profileId;
    }
}
