using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// Garante que os exemplos few-shot ensinados à IA no system prompt (<see cref="CadScriptProtocol.Examples"/>)
/// realmente compilam e executam no <see cref="RoslynSceneEvaluator"/> — ou seja, o "dialeto" de CadScript
/// ensinado à IA é sempre válido em relação ao Domain atual do projeto.
/// </summary>
public class CadScriptProtocolExamplesTests
{
    public static IEnumerable<object[]> Examples => CadScriptProtocol.Examples.Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Exemplo_DoSystemPrompt_CompilaEExecutaComSucesso(string example)
    {
        var evaluator = new RoslynSceneEvaluator();

        var result = await evaluator.EvaluateAsync(example);

        result.Success.Should().BeTrue(because: result.ErrorMessage ?? "esperava sucesso na avaliação do exemplo");
        result.Features.Should().NotBeEmpty();
    }
}
