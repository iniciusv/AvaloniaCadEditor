using System.Collections.Generic;
using System.Text.Json;
using AiCore.Models;
using AiCore.Services;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// O thinking mode da DeepSeek é ligado por padrão no servidor: omitir o campo <c>thinking</c>
/// não o desliga. Estes testes fixam que o estado vai sempre explícito no corpo da requisição.
/// </summary>
public class LlmClientRequestBodyTests
{
    private const string DeepSeekUrl = "https://api.deepseek.com/";

    private static string BuildJson(
        string model,
        bool enableThinking,
        string baseUrl = DeepSeekUrl,
        List<object>? messages = null)
    {
        var body = LlmClient.BuildRequestBody(
            model,
            baseUrl,
            messages ?? new List<object> { new { role = "user", content = "Crie uma caixa" } },
            temperature: 0.3,
            maxTokens: 8192,
            enableThinking: enableThinking);

        return JsonSerializer.Serialize(body, LlmClient.SerializerOptions);
    }

    [Theory]
    [InlineData("deepseek-v4-pro")]
    [InlineData("deepseek-flash")]
    [InlineData("deepseek-v4-flash")]
    public void BuildRequestBody_ComThinkingDesligado_EnviaThinkingDisabled(string model)
    {
        BuildJson(model, enableThinking: false)
            .Should().Contain("\"thinking\":{\"type\":\"disabled\"}");
    }

    [Theory]
    [InlineData("deepseek-v4-pro")]
    [InlineData("deepseek-flash")]
    public void BuildRequestBody_ComThinkingLigado_EnviaThinkingEnabled(string model)
    {
        BuildJson(model, enableThinking: true)
            .Should().Contain("\"thinking\":{\"type\":\"enabled\"}");
    }

    [Fact]
    public void BuildRequestBody_ComDestinoNaoDeepSeek_NaoEnviaThinking()
    {
        BuildJson("gpt-4o", enableThinking: false, baseUrl: "https://api.openai.com/")
            .Should().NotContain("thinking");
    }

    [Fact]
    public void BuildRequestBody_ComBaseUrlDeepSeekEModeloCustomizado_EnviaThinking()
    {
        BuildJson("meu-modelo", enableThinking: false, baseUrl: DeepSeekUrl)
            .Should().Contain("\"thinking\":{\"type\":\"disabled\"}");
    }

    [Fact]
    public void BuildApiMessages_NaoReenviaReasoningContent()
    {
        var historico = new[]
        {
            new ChatMessage { Role = "user", Content = "Crie uma caixa" },
            new ChatMessage
            {
                Role = "assistant",
                Content = "Pronto.",
                ReasoningContent = "raciocínio interno, a API ignora se reenviado"
            }
        };

        string json = JsonSerializer.Serialize(
            LlmClient.BuildApiMessages(historico), LlmClient.SerializerOptions);

        json.Should().NotContain("reasoning");
        json.Should().Contain("\"content\":\"Pronto.\"");
    }

    [Fact]
    public void BuildRequestBody_ComThinkingNone_NaoEnviaThinkingMesmoNaDeepSeek()
    {
        var body = LlmClient.BuildRequestBody(
            "deepseek-v4-pro", DeepSeekUrl, new List<object>(), 0.3, 8192, enableThinking: true, ThinkingControl.None);

        JsonSerializer.Serialize(body, LlmClient.SerializerOptions).Should().NotContain("thinking");
    }

    [Fact]
    public void BuildRequestBody_ComThinkingDeepSeekToggle_EnviaThinkingEmQualquerDestino()
    {
        var body = LlmClient.BuildRequestBody(
            "qwen3", "http://localhost:11434/v1", new List<object>(), 0.3, 8192, enableThinking: false, ThinkingControl.DeepSeekToggle);

        JsonSerializer.Serialize(body, LlmClient.SerializerOptions)
            .Should().Contain("\"thinking\":{\"type\":\"disabled\"}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildRequestBody_ComMaxTokensZero_OmiteMaxTokens(int maxTokens)
    {
        var body = LlmClient.BuildRequestBody(
            "qwen3", "http://localhost:11434/v1", new List<object>(), 0.3, maxTokens, enableThinking: false);

        JsonSerializer.Serialize(body, LlmClient.SerializerOptions).Should().NotContain("max_tokens");
    }

    [Theory]
    [InlineData("https://api.deepseek.com/", "https://api.deepseek.com/v1/chat/completions")]
    [InlineData("https://api.deepseek.com", "https://api.deepseek.com/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/chat/completions")]
    [InlineData("http://localhost:1234/v1/", "http://localhost:1234/v1/chat/completions")]
    [InlineData("http://localhost:8080", "http://localhost:8080/v1/chat/completions")]
    [InlineData("", "https://api.deepseek.com/v1/chat/completions")]
    public void BuildEndpoint_AceitaUrlBaseComOuSemV1(string baseUrl, string expected)
    {
        LlmClient.BuildEndpoint(baseUrl, "chat/completions").Should().Be(expected);
    }

    [Fact]
    public void BuildRequestBody_PreservaModeloMensagensETemperatura()
    {
        string json = BuildJson("deepseek-flash", enableThinking: false);

        json.Should().Contain("\"model\":\"deepseek-flash\"");
        json.Should().Contain("\"content\":\"Crie uma caixa\"");
        json.Should().Contain("\"temperature\":0.3");
        json.Should().Contain("\"max_tokens\":8192");
        json.Should().Contain("\"stream\":false");
    }
}
