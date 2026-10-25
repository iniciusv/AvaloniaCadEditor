using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AiCore.Models;
using AiCore.Services;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// O cliente fala com qualquer servidor OpenAI-compatível (DeepSeek, Ollama, LM Studio,
/// llama.cpp). Estes testes fixam o que muda entre eles: chave opcional, onde vem o raciocínio,
/// lista de modelos e timeout. Ver docs/referencias/servidores-locais-openai.md.
/// </summary>
public class LlmClientHttpTests
{
    private const string OkBody = """
        {"choices":[{"message":{"role":"assistant","content":"Pronto."},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":12,"completion_tokens":3}}
        """;

    private static LlmOptions LocalOptions() => new()
    {
        BaseUrl = "http://localhost:11434/v1",
        Model = "qwen3:8b",
        ApiKey = "",
        ApiKeyRequired = false,
        MaxTokens = 0,
        Thinking = ThinkingControl.None
    };

    private static readonly ChatMessage[] Pergunta = { new() { Role = "user", Content = "Oi" } };

    [Fact]
    public async Task SemChaveENaoObrigatoria_EnviaSemAuthorization()
    {
        var handler = new FakeHandler(OkBody);

        var result = await new LlmClient(handler).SendConversationAsync(Pergunta, LocalOptions());

        result.Content.Should().Be("Pronto.");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Authorization.Should().BeNull();
        handler.Requests[0].RequestUri!.ToString().Should().Be("http://localhost:11434/v1/chat/completions");
    }

    [Fact]
    public async Task SemChaveEObrigatoria_LancaSemChamarOServidor()
    {
        var handler = new FakeHandler(OkBody);
        var options = LocalOptions();
        options.ApiKeyRequired = true;

        var act = () => new LlmClient(handler).SendConversationAsync(Pergunta, options);

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ComChave_EnviaBearer()
    {
        var handler = new FakeHandler(OkBody);
        var options = new LlmOptions { ApiKey = "sk-teste" };

        await new LlmClient(handler).SendConversationAsync(Pergunta, options);

        handler.Requests[0].Headers.Authorization!.ToString().Should().Be("Bearer sk-teste");
    }

    [Fact]
    public async Task CorpoDoPerfilLocal_SemThinkingNemMaxTokens()
    {
        var handler = new FakeHandler(OkBody);

        await new LlmClient(handler).SendConversationAsync(Pergunta, LocalOptions());

        handler.Bodies[0].Should().NotContain("thinking").And.NotContain("max_tokens");
        handler.Bodies[0].Should().Contain("\"model\":\"qwen3:8b\"");
    }

    [Theory]
    [InlineData("reasoning_content")]   // DeepSeek, llama.cpp
    [InlineData("reasoning")]           // Ollama, LM Studio recente
    public async Task LeORaciocinioDosDoisCampos(string field)
    {
        string body = $$"""
            {"choices":[{"message":{"content":"Resposta","{{field}}":"pensei"},"finish_reason":"stop"}]}
            """;

        var result = await new LlmClient(new FakeHandler(body)).SendConversationAsync(Pergunta, LocalOptions());

        result.Content.Should().Be("Resposta");
        result.ReasoningContent.Should().Be("pensei");
        result.FinishReason.Should().Be("stop");
    }

    [Theory]
    [InlineData("<think>pensei</think>\nResposta")]
    [InlineData("pensei</think>Resposta")]   // template que já abre o <think> no prompt
    public async Task SeparaThinkEmbutidoNoConteudo(string content)
    {
        string body = "{\"choices\":[{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(content) + "}}]}";

        var result = await new LlmClient(new FakeHandler(body)).SendConversationAsync(Pergunta, LocalOptions());

        result.Content.Should().Be("Resposta");
        result.ReasoningContent.Should().Be("pensei");
    }

    [Fact]
    public async Task ConteudoNulo_ViraVazio()
    {
        const string body = """{"choices":[{"message":{"content":null,"reasoning":"só pensou"},"finish_reason":"length"}]}""";

        var result = await new LlmClient(new FakeHandler(body)).SendConversationAsync(Pergunta, LocalOptions());

        result.Content.Should().BeEmpty();
        result.ReasoningContent.Should().Be("só pensou");
        result.FinishReason.Should().Be("length");
    }

    [Fact]
    public async Task LeUsoDeTokens()
    {
        var result = await new LlmClient(new FakeHandler(OkBody)).SendConversationAsync(Pergunta, LocalOptions());

        result.PromptTokens.Should().Be(12);
        result.CompletionTokens.Should().Be(3);
    }

    [Fact]
    public async Task ErroHttp_LancaComStatus()
    {
        var handler = new FakeHandler("{\"error\":\"model not found\"}", HttpStatusCode.NotFound);

        var act = () => new LlmClient(handler).SendConversationAsync(Pergunta, LocalOptions());

        var error = await act.Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        error.Which.Message.Should().Contain("model not found");
    }

    [Fact]
    public async Task ListModels_LeOsIds()
    {
        const string body = """{"object":"list","data":[{"id":"qwen3:8b"},{"id":"gemma3:12b"}]}""";
        var handler = new FakeHandler(body);

        var models = await new LlmClient(handler).ListModelsAsync(LocalOptions());

        models.Should().Equal("qwen3:8b", "gemma3:12b");
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("http://localhost:11434/v1/models");
    }

    [Fact]
    public async Task ServidorLento_LancaTimeout()
    {
        var handler = new FakeHandler(OkBody) { Delay = TimeSpan.FromSeconds(5) };
        var options = LocalOptions();
        options.RequestTimeout = TimeSpan.FromMilliseconds(50);

        var act = () => new LlmClient(handler).SendConversationAsync(Pergunta, options);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task CancelamentoDoChamador_NaoViraTimeout()
    {
        var handler = new FakeHandler(OkBody) { Delay = TimeSpan.FromSeconds(5) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => new LlmClient(handler).SendConversationAsync(Pergunta, LocalOptions(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public FakeHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public TimeSpan Delay { get; init; }
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }
}
