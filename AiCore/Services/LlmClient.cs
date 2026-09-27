using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AiCore.Models;

namespace AiCore.Services
{
    /// <summary>
    /// Resultado bruto de uma chamada de conversa à LLM.
    /// </summary>
    public class LlmResult
    {
        public string Content { get; set; } = string.Empty;
        public string ReasoningContent { get; set; } = string.Empty;
        public string RawBody { get; set; } = string.Empty;
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
    }

    /// <summary>
    /// Cliente HTTP genérico para APIs de chat compatíveis com o formato OpenAI
    /// (endpoint POST /v1/chat/completions). Sem dependências externas (apenas BCL).
    /// </summary>
    public class LlmClient
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public LlmClient()
        {
            if (_httpClient.Timeout < TimeSpan.FromMinutes(15))
            {
                _httpClient.Timeout = TimeSpan.FromMinutes(15);
            }
        }

        /// <summary>
        /// Envia uma conversa completa à LLM e retorna o conteúdo, raciocínio e uso de tokens.
        /// </summary>
        /// <param name="messages">Mensagens (system/user/assistant) da conversa.</param>
        /// <param name="options">Configuração da chamada (ApiKey, BaseUrl, Model, etc.).</param>
        /// <param name="temperature">Opcional. Sobrescreve a temperatura definida em <paramref name="options"/>.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        public async Task<LlmResult> SendConversationAsync(
            IEnumerable<ChatMessage> messages,
            LlmOptions options,
            double? temperature = null,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException("Chave de API não configurada.");
            }

            string baseUrl = options.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://api.deepseek.com/";
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            string endpoint = baseUrl + "v1/chat/completions";
            if (endpoint.Contains("/v1/v1/")) endpoint = endpoint.Replace("/v1/v1/", "/v1/");

            var apiMessages = new List<object>();
            foreach (var msg in messages)
            {
                if (!string.IsNullOrWhiteSpace(msg.ReasoningContent))
                {
                    apiMessages.Add(new { role = msg.Role, content = msg.Content, reasoning_content = msg.ReasoningContent });
                }
                else
                {
                    apiMessages.Add(new { role = msg.Role, content = msg.Content });
                }
            }

            string modelName = options.Model ?? "deepseek-v4-pro";
            double temp = temperature ?? options.Temperature;
            int maxTokens = options.MaxTokens > 0 ? options.MaxTokens : 384000;

            object requestBody;
            if (options.EnableThinkingMode && (modelName.Contains("deepseek-v4") || modelName.Contains("reasoner")))
            {
                requestBody = new
                {
                    model = modelName,
                    messages = apiMessages,
                    thinking = new { type = "enabled" },
                    temperature = temp,
                    max_tokens = maxTokens,
                    stream = false
                };
            }
            else
            {
                requestBody = new
                {
                    model = modelName,
                    messages = apiMessages,
                    temperature = temp,
                    max_tokens = maxTokens,
                    stream = false
                };
            }

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            string jsonContent = JsonSerializer.Serialize(requestBody, jsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Erro na API da IA ({(int)response.StatusCode}): {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var choice = doc.RootElement.GetProperty("choices")[0];
            var message = choice.GetProperty("message");

            string content = message.TryGetProperty("content", out var contentProp)
                ? contentProp.GetString() ?? ""
                : "";

            string reasoningContent = message.TryGetProperty("reasoning_content", out var rcProp)
                ? rcProp.GetString() ?? ""
                : "";

            int promptTokens = 0;
            int completionTokens = 0;
            if (doc.RootElement.TryGetProperty("usage", out var usageProp))
            {
                if (usageProp.TryGetProperty("prompt_tokens", out var ptProp))
                    promptTokens = ptProp.GetInt32();
                if (usageProp.TryGetProperty("completion_tokens", out var ctProp))
                    completionTokens = ctProp.GetInt32();
            }

            return new LlmResult
            {
                Content = content,
                ReasoningContent = reasoningContent,
                RawBody = responseBody,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens
            };
        }
    }
}
