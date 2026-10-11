using System;
using System.IO;
using System.Linq;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Prompts;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

public class CadScriptPromptProfilesTests
{
    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    /// <summary>
    /// O prompt de referência não pode mudar sem querer. O golden guarda o texto com a API trocada por
    /// <c>{{API}}</c>: mudanças no Domain (que a API reflete) não o quebram; mudanças nas regras, sim.
    /// Mudança intencional? Atualize Ai/Golden/system-prompt-atual.txt junto.
    /// </summary>
    [Fact]
    public void PerfilAtual_IgualAoGolden()
    {
        string golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ai", "Golden", "system-prompt-atual.txt"));
        string expected = Normalize(golden).Replace("{{API}}", Normalize(CadScriptApiReference.Build()));

        Normalize(new CurrentPromptProfile().BuildSystemPrompt()).Should().Be(expected);
    }

    [Fact]
    public void ProtocoloSemPerfil_UsaOPerfilAtual()
    {
        var protocol = new CadScriptProtocol();

        protocol.Profile.Id.Should().Be(CurrentPromptProfile.ProfileId);
        protocol.GetSystemPrompt().Should().Be(new CurrentPromptProfile().BuildSystemPrompt());
    }

    [Fact]
    public void TodoPerfil_IncluiOFormatoDaRespostaQueOParserLe()
    {
        foreach (var profile in CadScriptPromptProfiles.All)
        {
            Normalize(profile.BuildSystemPrompt()).Should().Contain(Normalize(CadScriptResponseFormat.Text), because: profile.Id);
        }
    }

    [Fact]
    public void Perfis_TemIdsUnicosEExemplosDeCadaTipo()
    {
        CadScriptPromptProfiles.All.Select(p => p.Id).Should().OnlyHaveUniqueItems();

        foreach (var profile in CadScriptPromptProfiles.All)
        {
            profile.Examples.Select(e => e.Kind).Distinct().Should().BeEquivalentTo(
                Enum.GetValues<CadScriptExampleKind>(), because: profile.Id);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("perfil-removido")]
    public void Find_IdVazioOuDesconhecido_CaiNoPadrao(string? id)
    {
        CadScriptPromptProfiles.Find(id).Should().BeSameAs(CadScriptPromptProfiles.Default);
    }

    [Fact]
    public void Find_PorId_IgnoraMaiusculas()
    {
        CadScriptPromptProfiles.Find("ENXUTO").Id.Should().Be(LeanPromptProfile.ProfileId);
    }
}
