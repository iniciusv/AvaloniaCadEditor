using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Ai.Prompts;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// Garante que os exemplos few-shot ensinados à IA por TODOS os perfis de pré-prompt
/// (<see cref="CadScriptPromptProfiles.All"/>) realmente compilam e executam no <see cref="RoslynSceneEvaluator"/>
/// — ou seja, os dois "dialetos" de CadScript (script e peça) ensinados à IA são sempre válidos em relação ao
/// Domain atual do projeto.
/// </summary>
public class CadScriptProtocolExamplesTests
{
    private static IEnumerable<object[]> ExamplesOf(CadScriptExampleKind kind) =>
        CadScriptPromptProfiles.All.SelectMany(profile => profile.Examples
            .Where(e => e.Kind == kind)
            .Select((e, i) => new object[] { profile.Id, i, e.Code }));

    public static IEnumerable<object[]> Examples => ExamplesOf(CadScriptExampleKind.Script);

    public static IEnumerable<object[]> PartExamples => ExamplesOf(CadScriptExampleKind.Part);

    public static IEnumerable<object[]> SceneUsingPartExamples => ExamplesOf(CadScriptExampleKind.SceneUsingPart);

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Exemplo_DoSystemPrompt_CompilaEExecutaComSucesso(string profileId, int index, string example)
    {
        var evaluator = new RoslynSceneEvaluator();

        var result = await evaluator.EvaluateAsync(example);

        result.Success.Should().BeTrue(because: result.ErrorMessage ?? $"esperava sucesso no exemplo {index} do perfil {profileId}");
        result.Features.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(PartExamples))]
    public async Task ExemploDePeca_DoSystemPrompt_CompilaEExecutaComSucesso(string profileId, int index, string example)
    {
        var evaluator = new RoslynSceneEvaluator();
        var partDoc = new ScriptDocument("Pecas/Peca.cs", example);
        var types = PartSourceEditor.FindPartTypes(new[] { partDoc });
        types.Should().HaveCount(1, because: "cada exemplo de peça deve declarar exatamente um record : Part");

        // Arquivo de entrada sintético (o mesmo truque usado por CadScriptChatSession): só instancia a
        // peça, para o Build() rodar de verdade — o exemplo em si nunca chama Add/AddFeature.
        var entryDoc = new ScriptDocument(ScriptDocument.DefaultSceneFile, PartSourceEditor.BuildAddStatement(types[0]));

        var result = await evaluator.EvaluateAsync(new[] { partDoc, entryDoc });

        result.Success.Should().BeTrue(because: result.ErrorMessage ?? $"esperava sucesso no exemplo de peça {index} do perfil {profileId}");
        result.Features.Should().NotBeEmpty();
    }

    /// <summary>O exemplo de uso não redeclara a peça: compila junto com os exemplos de peça do mesmo perfil.</summary>
    [Theory]
    [MemberData(nameof(SceneUsingPartExamples))]
    public async Task ExemploDeUsoDePeca_JuntoDasPecasDoMesmoPerfil_CompilaEExecutaComSucesso(string profileId, int index, string example)
    {
        var evaluator = new RoslynSceneEvaluator();
        var profile = CadScriptPromptProfiles.Find(profileId);
        var docs = profile.Examples
            .Where(e => e.Kind == CadScriptExampleKind.Part)
            .Select((e, i) => new ScriptDocument($"Pecas/Peca{i}.cs", e.Code))
            .Append(new ScriptDocument(ScriptDocument.DefaultSceneFile, example))
            .ToList();

        var result = await evaluator.EvaluateAsync(docs);

        result.Success.Should().BeTrue(because: result.ErrorMessage ?? $"esperava sucesso no exemplo de uso {index} do perfil {profileId}");
        result.Features.Should().NotBeEmpty();
    }
}
