using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// Garante que os exemplos few-shot ensinados à IA no system prompt (<see cref="CadScriptProtocol.Examples"/> e
/// <see cref="CadScriptProtocol.PartExamples"/>) realmente compilam e executam no <see cref="RoslynSceneEvaluator"/>
/// — ou seja, os dois "dialetos" de CadScript (script e peça) ensinados à IA são sempre válidos em relação ao
/// Domain atual do projeto.
/// </summary>
public class CadScriptProtocolExamplesTests
{
    public static IEnumerable<object[]> Examples => CadScriptProtocol.Examples.Select(e => new object[] { e });

    public static IEnumerable<object[]> PartExamples => CadScriptProtocol.PartExamples.Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Exemplo_DoSystemPrompt_CompilaEExecutaComSucesso(string example)
    {
        var evaluator = new RoslynSceneEvaluator();

        var result = await evaluator.EvaluateAsync(example);

        result.Success.Should().BeTrue(because: result.ErrorMessage ?? "esperava sucesso na avaliação do exemplo");
        result.Features.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(PartExamples))]
    public async Task ExemploDePeca_DoSystemPrompt_CompilaEExecutaComSucesso(string example)
    {
        var evaluator = new RoslynSceneEvaluator();
        var partDoc = new ScriptDocument("Pecas/Peca.cs", example);
        var types = PartSourceEditor.FindPartTypes(new[] { partDoc });
        types.Should().HaveCount(1, because: "cada exemplo de peça deve declarar exatamente um record : Part");

        // Arquivo de entrada sintético (o mesmo truque usado por CadScriptChatSession): só instancia a
        // peça, para o Build() rodar de verdade — o exemplo em si nunca chama Add/AddFeature.
        var entryDoc = new ScriptDocument(ScriptDocument.DefaultSceneFile, PartSourceEditor.BuildAddStatement(types[0]));

        var result = await evaluator.EvaluateAsync(new[] { partDoc, entryDoc });

        result.Success.Should().BeTrue(because: result.ErrorMessage ?? "esperava sucesso na avaliação do exemplo de peça");
        result.Features.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExemploDeUsoDePeca_JuntoDoExemploDePecaQueDeclaraPlaca_CompilaEExecutaComSucesso()
    {
        var evaluator = new RoslynSceneEvaluator();
        var partDoc = new ScriptDocument("Pecas/Placa.cs", CadScriptProtocol.PartExamples[0]);
        var sceneDoc = new ScriptDocument(ScriptDocument.DefaultSceneFile, CadScriptProtocol.SceneUsingPartExample);

        var result = await evaluator.EvaluateAsync(new[] { partDoc, sceneDoc });

        result.Success.Should().BeTrue(because: result.ErrorMessage ?? "esperava sucesso ao usar a peça já declarada");
        result.Features.Should().NotBeEmpty();
    }
}
