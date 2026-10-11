using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>Os perfis de pré-prompt disponíveis. Para criar uma estratégia nova, adicione um perfil aqui.</summary>
public static class CadScriptPromptProfiles
{
    public static ICadScriptPromptProfile Default { get; } = new CurrentPromptProfile();

    public static IReadOnlyList<ICadScriptPromptProfile> All { get; } = new ICadScriptPromptProfile[]
    {
        Default,
        new LeanPromptProfile(),
        new SimplePromptProfile()
    };

    /// <summary>O perfil com esse id; vazio ou desconhecido (ex.: perfil removido) cai no <see cref="Default"/>.</summary>
    public static ICadScriptPromptProfile Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
