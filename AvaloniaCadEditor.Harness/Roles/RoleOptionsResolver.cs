using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Harness.Agents;

namespace AvaloniaCadEditor.Harness.Roles;

/// <summary>As opções de um papel: o endpoint e até onde o agente pode ir.</summary>
public sealed record RoleOptions(AiEndpointProfile Profile, LlmOptions Options, AgentLimits Limits);

/// <summary>
/// Junta os perfis do app (<see cref="AiSettings"/>) com a escolha de papéis do harness
/// (<see cref="HarnessSettings"/>). Modelo local é grátis por chamada: ganha limites bem maiores.
/// </summary>
public static class RoleOptionsResolver
{
    /// <returns>Null sem erro = o papel não está configurado (ex.: sem Inspetor).</returns>
    public static (RoleOptions? Options, string? Error) Resolve(AiSettings app, HarnessSettings harness, AgentRole role)
    {
        app = AiSettingsMigration.Upgrade(app);
        string? id = harness.ProfileFor(role);

        AiEndpointProfile? profile;
        if (id == null)
        {
            if (role != AgentRole.Editor) return (null, null);
            profile = app.GetActiveProfile();
        }
        else
        {
            profile = app.Profiles.FirstOrDefault(p => p.Id == id);
            if (profile == null) return (null, $"O perfil '{id}' do papel {role} não existe mais. Escolha outro em ⚙ Settings.");
        }

        var (options, error) = AiEndpointOptions.Resolve(profile!, app.EnableReasoning);
        if (options == null) return (null, $"{role}: {error}");

        return (new RoleOptions(profile!, options, profile!.IsLocal ? AgentLimits.Local : AgentLimits.Remote), null);
    }
}
