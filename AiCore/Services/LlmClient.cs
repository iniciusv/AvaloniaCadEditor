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

        /// <summary>Opções de serialização do corpo da requisição.</summary>
        public static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Converte as mensagens da conversa para o formato da API.
        /// O <c>reasoning_content</c> dos turnos anteriores não é reenviado: sem o parâmetro
        /// <c>tools</c> na requisição a API o ignora e não o concatena ao contexto, então enviá-lo
        /// apenas infla o payload (o raciocínio costuma ser maior que a resposta).
        /// Atenção: se este cliente passar a enviar <c>tools</c>, a regra inverte — aí o
        /// <c>reasoning_content</c> de todos os turnos anteriores torna-se obrigatório e a API
        /// responde 400 sem ele.
        /// </summary>
        public static List<object> BuildApiMessages(IEnumerable<ChatMessage> messages)
        {
            var apiMessages = new List<object>();
            foreach (var msg in messages)
            {
                apiMessages.Add(new { role = msg.Role, content = msg.Content });
            }

            return apiMessages;
        }

        /// <summary>
        /// Indica se o destino aceita o toggle <c>thinking</c> da DeepSeek. Endpoints
        /// OpenAI-compatíveis de outros provedores rejeitam o campo, por isso o gate.
        /// </summary>
        public static bool SupportsThinkingToggle(string modelName, string baseUrl) =>
            (modelName ?? string.Empty).StartsWith("deepseek", StringComparison.OrdinalIgnoreCase) ||
            (baseUrl ?? string.Empty).Contains("deepseek", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Monta o corpo da requisição de chat. O campo <c>thinking</c> é sempre declarado
        /// explicitamente (<c>enabled</c>/<c>disabled</c>) nos destinos DeepSeek: omiti-lo não
        /// desliga o raciocínio, pois o thinking mode é ligado por padrão no servidor.
        /// </summary>
        public static object BuildRequestBody(
            string modelName,
            string baseUrl,
            List<object> apiMessages,
            double temperature,
            int maxTokens,
            bool enableThinking)
        {
            if (!SupportsThinkingToggle(modelName, baseUrl))
            {
                return new
                {
                    model = modelName,
                    messages = apiMessages,
                    temperature = temperature,
                    max_tokens = maxTokens,
                    stream = false
                };
            }

            return new
            {
                model = modelName,
                messages = apiMessages,
                thinking = new { type = enableThinking ? "enabled" : "disabled" },
                temperature = temperature,
                max_tokens = maxTokens,
                stream = false
            };
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

            List<object> apiMessages = BuildApiMessages(messages);

            string modelName = options.Model ?? "deepseek-v4-pro";
            double temp = temperature ?? options.Temperature;
            int maxTokens = options.MaxTokens > 0 ? options.MaxTokens : 384000;

            object requestBody = BuildRequestBody(
                modelName, baseUrl, apiMessages, temp, maxTokens, options.EnableThinkingMode);

            string jsonContent = JsonSerializer.Serialize(requestBody, SerializerOptions);

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
