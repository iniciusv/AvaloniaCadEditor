using System.Collections.Generic;

namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>Que tipo de arquivo um exemplo few-shot representa — define como o teste o compila.</summary>
public enum CadScriptExampleKind
{
    /// <summary>Arquivo único / de entrada (statements soltos): compila sozinho.</summary>
    Script,

    /// <summary>Arquivo de peça (Pecas/): um record que herda de Part.</summary>
    Part,

    /// <summary>Arquivo de entrada que usa peças declaradas nos exemplos <see cref="Part"/> do mesmo perfil.</summary>
    SceneUsingPart
}

/// <summary>Um exemplo few-shot mostrado à IA no system prompt.</summary>
public sealed record CadScriptPromptExample(CadScriptExampleKind Kind, string Code);

/// <summary>
/// Uma versão do pré-prompt (system prompt) do chat de IA. Só o system prompt varia entre perfis:
/// a mensagem do usuário, o parser da resposta e o auto-fix são os mesmos do <see cref="CadScriptProtocol"/>,
/// por isso todo perfil deve incluir <see cref="CadScriptResponseFormat.Text"/>.
/// </summary>
public interface ICadScriptPromptProfile
{
    /// <summary>Identificador estável, gravado nas configurações (ex.: "atual").</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Uma frase sobre a estratégia do perfil, mostrada na interface.</summary>
    string Description { get; }

    /// <summary>Os exemplos few-shot do perfil — um teste garante que todos compilam e executam.</summary>
    IReadOnlyList<CadScriptPromptExample> Examples { get; }

    string BuildSystemPrompt();
}
