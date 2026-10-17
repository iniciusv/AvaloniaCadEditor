using System;
using System.Collections.Generic;
using System.Text.Json;
using AiCore.Models;

namespace AiCore.Services
{
    /// <summary>
    /// Lê a resposta de <c>/v1/chat/completions</c> tolerando as variações dos servidores
    /// OpenAI-compatíveis: o raciocínio pode vir em <c>reasoning_content</c> (DeepSeek, llama.cpp),
    /// em <c>reasoning</c> (Ollama, LM Studio recente) ou embutido no <c>content</c> entre
    /// <c>&lt;think&gt;…&lt;/think&gt;</c>.
    /// </summary>
    public static class LlmResponseParser
    {
        private const string ThinkOpen = "<think>";
        private const string ThinkClose = "</think>";

        public static LlmResult Parse(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var choice = root.GetProperty("choices")[0];
            var message = choice.GetProperty("message");

            string content = ReadString(message, "content");
            string reasoning = ReadString(message, "reasoning_content");
            if (reasoning.Length == 0) reasoning = ReadString(message, "reasoning");

            (content, string embedded) = SplitThink(content);
            if (reasoning.Length == 0) reasoning = embedded;

            int promptTokens = 0;
            int completionTokens = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                promptTokens = ReadInt(usage, "prompt_tokens");
                completionTokens = ReadInt(usage, "completion_tokens");
            }

            return new LlmResult
            {
                Content = content,
                ReasoningContent = reasoning,
                FinishReason = ReadString(choice, "finish_reason"),
                ToolCalls = ReadToolCalls(message),
                RawBody = responseBody,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens
            };
        }

        /// <summary>
        /// <c>message.tool_calls[]</c>. Tolerante: <c>arguments</c> pode vir como texto JSON (OpenAI,
        /// DeepSeek, LM Studio) ou como objeto; sem <c>id</c>, um é gerado pela posição.
        /// </summary>
        private static IReadOnlyList<ToolCall> ReadToolCalls(JsonElement message)
        {
            if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
                return Array.Empty<ToolCall>();

            var result = new List<ToolCall>();
            int index = 0;
            foreach (var call in calls.EnumerateArray())
            {
                if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
                    continue;

                string name = ReadString(function, "name");
                string arguments = "{}";
                if (function.TryGetProperty("arguments", out var args))
                {
                    arguments = args.ValueKind switch
                    {
                        JsonValueKind.String => args.GetString() ?? "{}",
                        JsonValueKind.Object or JsonValueKind.Array => args.GetRawText(),
                        _ => "{}"
                    };
                }

                string id = ReadString(call, "id");
                if (id.Length == 0) id = $"call_{index}";
                result.Add(new ToolCall(id, name, arguments));
                index++;
            }

            return result;
        }

        /// <summary>Ids de <c>GET /v1/models</c> (<c>{ "data": [ { "id": ... } ] }</c>).</summary>
        public static IReadOnlyList<string> ParseModelIds(string responseBody)
        {
            var ids = new List<string>();
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return ids;

            foreach (var model in data.EnumerateArray())
            {
                string id = ReadString(model, "id");
                if (id.Length > 0) ids.Add(id);
            }

            return ids;
        }

        /// <summary>
        /// Separa o bloco de raciocínio embutido no conteúdo. Alguns templates já abrem o
        /// <c>&lt;think&gt;</c> no prompt, e então a resposta traz só o fechamento.
        /// </summary>
        public static (string Content, string Reasoning) SplitThink(string content)
        {
            int close = content.IndexOf(ThinkClose, StringComparison.Ordinal);
            if (close < 0) return (content, string.Empty);

            int open = content.IndexOf(ThinkOpen, StringComparison.Ordinal);
            int start = open >= 0 && open < close ? open + ThinkOpen.Length : 0;
            string before = open >= 0 && open < close ? content.Substring(0, open) : string.Empty;

            string reasoning = content.Substring(start, close - start).Trim();
            string rest = (before + content.Substring(close + ThinkClose.Length)).Trim();
            return (rest, reasoning);
        }

        private static string ReadString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString() ?? string.Empty
                : string.Empty;

        private static int ReadInt(JsonElement element, string name) =>
            element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int value)
                ? value
                : 0;
    }
}
