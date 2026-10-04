using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Scenes;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// Garante que os três lugares que ensinam o CadScript — o system prompt do chat, o
/// <c>.cad/api.md</c> da cena e o <c>AGENTS.md</c> para IAs externas — usem o MESMO texto de
/// <see cref="CadScriptRules"/>, em vez de cada um ter a sua redação (que divergia com o tempo).
/// </summary>
public class CadScriptRulesTests
{
    private static readonly string[] SharedBlocks =
    {
        CadScriptRules.Common,
        CadScriptRules.TrackableSolids,
        CadScriptRules.SceneFile,
        CadScriptRules.PartFile,
        CadScriptRules.PartUsage
    };

    /// <summary>Os consumidores escrevem com terminadores diferentes; o texto é que precisa bater.</summary>
    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    [Fact]
    public void SystemPrompt_UsaOsBlocosCompartilhados()
    {
        string prompt = Normalize(new CadScriptProtocol().GetSystemPrompt());

        foreach (var block in SharedBlocks)
        {
            prompt.Should().Contain(Normalize(block));
        }
    }

    [Fact]
    public void SecaoDeRegrasDoApiMd_UsaOsBlocosCompartilhados()
    {
        string rules = Normalize(SceneProjectTemplate.BuildRulesSection());

        foreach (var block in SharedBlocks)
        {
            rules.Should().Contain(Normalize(block));
        }
    }

    [Fact]
    public void AgentsFile_UsaOsBlocosCompartilhados()
    {
        string agents = Normalize(SceneProjectTemplate.BuildAgentsFile("Cena", SceneToolPaths.FromCurrentProcess()));

        foreach (var block in SharedBlocks)
        {
            agents.Should().Contain(Normalize(block));
        }
    }

    [Fact]
    public void ApiReference_NaoRepeteAsRegras_SoASuperficieDaApi()
    {
        string reference = CadScriptApiReference.Build();

        reference.Should().NotContain("SÓLIDOS RASTREÁVEIS");
        reference.Should().NotContain("PEÇAS PARAMÉTRICAS");
        reference.Should().NotContain("sealed record");
    }

    [Fact]
    public void PartExamplesDoProtocolo_SaoOsDaFonteUnica()
    {
        CadScriptProtocol.PartExamples.Should().Equal(
            CadScriptRules.PartExamplePlaca,
            CadScriptRules.PartExampleChapaFurada,
            CadScriptRules.PartExampleBlocoQuatroFuros);

        CadScriptProtocol.SceneUsingPartExample.Should().Be(CadScriptRules.SceneUsingPartExample);
    }
}
