using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiCore.Models;
using AiCore.Services;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// Function calling no formato OpenAI: <c>tools</c> na requisição, <c>tool_calls</c> na resposta e o
/// histórico com <c>assistant(tool_calls)</c> + <c>tool</c>. Na DeepSeek, com <c>tools</c>, o
/// <c>reasoning_content</c> dos turnos anteriores passa a ser obrigatório (400 sem ele).
/// </summary>
public class LlmClientToolCallingTests
{
    private static readonly ToolDefinition GetView = new(
        "get_view",
        "Vista em arame do alvo, em texto.",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["target"] = new JsonObject { ["type"] = "string" } },
            ["required"] = new JsonArray("target")
        });

    private static string Json(object value) => JsonSerializer.Serialize(value, LlmClient.SerializerOptions);

    [Fact]
    public void BuildRequestBody_ComTools_EnviaNoFormatoOpenAI()
    {
        var body = LlmClient.BuildRequestBody("deepseek-v4-pro", "https://api.deepseek.com/", new List<object>(), 0.3, 0,
            enableThinking: true, tools: new[] { GetView });

        string json = Json(body);
        json.Should().Contain("\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"get_view\"");
        json.Should().Contain("\"parameters\":{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\"}},\"required\":[\"target\"]}");
        json.Should().NotContain("strict", "o modo estrito só vai quando pedido");
    }

    [Fact]
    public void BuildRequestBody_SemTools_NaoEnviaOCampo()
    {
        Json(LlmClient.BuildRequestBody("qwen3", "http://localhost:11434/v1", new List<object>(), 0.3, 0, false))
            .Should().NotContain("tools");
    }

    [Fact]
    public void BuildRequestBody_ToolEstrita_EnviaStrict()
    {
        var strict = new ToolDefinition("f", "d", new JsonObject { ["type"] = "object" }, strict: true);

        Json(LlmClient.BuildRequestBody("deepseek-v4-pro", "https://api.deepseek.com/beta", new List<object>(), 0.3, 0, false, tools: new[] { strict }))
            .Should().Contain("\"strict\":true");
    }

    [Fact]
    public void BuildApiMessages_ComChamadasEResultados_SerializaToolCallsEToolCallId()
    {
        var history = new[]
        {
            new ChatMessage { Role = "user", Content = "Onde fica o furo?" },
            new ChatMessage
            {
                Role = "assistant",
                Content = "",
                ReasoningContent = "preciso ver a vista",
                ToolCalls = new[] { new ToolCall("call_1", "get_view", "{\"target\":\"placa\"}") }
            },
            new ChatMessage { Role = "tool", ToolCallId = "call_1", Content = "# Vista Top" }
        };

        string json = Json(LlmClient.BuildApiMessages(history, includeReasoning: true));

        json.Should().Contain("\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"get_view\",\"arguments\":\"{\\u0022target\\u0022:\\u0022placa\\u0022}\"}}]");
        json.Should().Contain("\"reasoning_content\":\"preciso ver a vista\"");
        json.Should().Contain("\"role\":\"tool\",\"content\":\"# Vista Top\",\"tool_call_id\":\"call_1\"");
    }

    [Fact]
    public void BuildApiMessages_SemIncludeReasoning_ContinuaSemReasoning()
    {
        var history = new[] { new ChatMessage { Role = "assistant", Content = "ok", ReasoningContent = "r" } };

        Json(LlmClient.BuildApiMessages(history)).Should().NotContain("reasoning");
    }

    [Fact]
    public void Parse_ToolCallsComArgumentosEmTexto()
    {
        const string body = """
            {"choices":[{"message":{"role":"assistant","content":"","tool_calls":[
              {"id":"call_a","type":"function","function":{"name":"get_view","arguments":"{\"target\":\"placa\",\"view\":\"Top\"}"}}
            ]},"finish_reason":"tool_calls"}]}
            """;

        var result = LlmResponseParser.Parse(body);

        result.FinishReason.Should().Be("tool_calls");
        var call = result.ToolCalls.Should().ContainSingle().Subject;
        call.Id.Should().Be("call_a");
        call.Name.Should().Be("get_view");
        JsonDocument.Parse(call.ArgumentsJson).RootElement.GetProperty("view").GetString().Should().Be("Top");
    }

    [Fact]
    public void Parse_ToolCallsComArgumentosEmObjetoESemId()
    {
        // Alguns servidores locais mandam o objeto direto e omitem o id.
        const string body = """
            {"choices":[{"message":{"content":null,"tool_calls":[
              {"function":{"name":"list_targets","arguments":{}}},
              {"function":{"name":"get_view","arguments":{"target":"x"}}}
            ]},"finish_reason":"tool_calls"}]}
            """;

        var result = LlmResponseParser.Parse(body);

        result.ToolCalls.Should().HaveCount(2);
        result.ToolCalls[0].Id.Should().Be("call_0");
        result.ToolCalls[0].ArgumentsJson.Should().Be("{}");
        result.ToolCalls[1].Id.Should().Be("call_1");
        JsonDocument.Parse(result.ToolCalls[1].ArgumentsJson).RootElement.GetProperty("target").GetString().Should().Be("x");
    }

    [Fact]
    public void Parse_SemToolCalls_ListaVazia()
    {
        LlmResponseParser.Parse("""{"choices":[{"message":{"content":"oi"}}]}""").ToolCalls.Should().BeEmpty();
    }
}
