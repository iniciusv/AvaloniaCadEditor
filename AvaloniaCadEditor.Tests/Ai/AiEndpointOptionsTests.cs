using System;
using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Domain.Settings;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

public class AiEndpointOptionsTests
{
    [Fact]
    public void PerfilLocal_SemChave_SaiSemExigirChaveESemThinking()
    {
        var profile = AiEndpointProfile.Local("ollama", "Ollama");
        profile.Model = "qwen3:8b";

        var (options, error) = AiEndpointOptions.Resolve(profile, enableReasoning: true);

        error.Should().BeNull();
        options!.ApiKey.Should().BeEmpty();
        options.ApiKeyRequired.Should().BeFalse();
        options.Thinking.Should().Be(ThinkingControl.None);
        options.MaxTokens.Should().Be(AiEndpointProfile.DefaultLocalMaxTokens, "um modelo local pode pensar sem parar");
        options.ReasoningEffort.Should().BeEmpty("com o raciocínio ligado nas settings, não desliga");
        options.BaseUrl.Should().Be("http://localhost:11434/v1");
        options.Model.Should().Be("qwen3:8b");
        options.RequestTimeout.Should().Be(TimeSpan.FromSeconds(1800));
    }

    [Fact]
    public void PerfilLocal_ComRaciocinioDesligado_PedeReasoningEffortNone()
    {
        var profile = AiEndpointProfile.Local("lmstudio", "LM Studio");
        profile.Model = "google/gemma-4-12b-qat";

        AiEndpointOptions.Resolve(profile, enableReasoning: false).Options!.ReasoningEffort.Should().Be("none");
    }

    [Fact]
    public void PerfilDeepSeek_NuncaUsaReasoningEffort_UsaOThinking()
    {
        var profile = AiEndpointProfile.DeepSeek();
        profile.ApiKey = "k";

        var options = AiEndpointOptions.Resolve(profile, enableReasoning: false).Options!;

        options.ReasoningEffort.Should().BeEmpty();
        options.EnableThinkingMode.Should().BeFalse();
    }

    [Fact]
    public void PerfilLocal_SemModelo_RetornaErro()
    {
        var (options, error) = AiEndpointOptions.Resolve(AiEndpointProfile.Local("ollama", "Ollama"), enableReasoning: false);

        options.Should().BeNull();
        error.Should().Contain("model");
    }

    [Fact]
    public void PerfilLocal_SemUrl_RetornaErroEmVezDeCairNaDeepSeek()
    {
        var profile = AiEndpointProfile.Local("lm", "LM Studio");
        profile.BaseUrl = "";
        profile.Model = "gemma";

        var (options, error) = AiEndpointOptions.Resolve(profile, false);

        options.Should().BeNull();
        error.Should().Contain("base URL");
    }

    [Fact]
    public void ChaveVazia_LeDaVariavelDoPerfil()
    {
        string variable = "AVALONIACAD_TEST_KEY_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(variable, "env-key");
            var profile = AiEndpointProfile.DeepSeek();
            profile.ApiKeyEnvVar = variable;

            AiEndpointOptions.Resolve(profile, false).Options!.ApiKey.Should().Be("env-key");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void PerfilDeepSeek_MantemThinkingAutoEMaxTokens()
    {
        var profile = AiEndpointProfile.DeepSeek();
        profile.ApiKey = "k";

        var options = AiEndpointOptions.Resolve(profile, enableReasoning: true).Options!;

        options.ApiKeyRequired.Should().BeTrue();
        options.Thinking.Should().Be(ThinkingControl.Auto);
        options.EnableThinkingMode.Should().BeTrue();
        options.MaxTokens.Should().Be(384000);
    }

    [Fact]
    public void PerfilRemoto_SemChaveNemVariavel_RetornaErroCitandoAVariavel()
    {
        // Variável que não existe: não mexe nas variáveis reais (os testes rodam em paralelo).
        var profile = AiEndpointProfile.DeepSeek();
        profile.ApiKeyEnvVar = "AVALONIACAD_TEST_UNSET_" + Guid.NewGuid().ToString("N");

        var (options, error) = AiEndpointOptions.Resolve(profile, false);

        options.Should().BeNull();
        error.Should().Contain(profile.ApiKeyEnvVar);
    }
}
