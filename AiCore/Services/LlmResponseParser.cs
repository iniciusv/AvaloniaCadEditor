using System;
using System.Collections.Generic;
using System.Text.Json;

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
                RawBody = responseBody,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens
            };
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
