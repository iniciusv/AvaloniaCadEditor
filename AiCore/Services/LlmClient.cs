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

        /// <summary><c>finish_reason</c> da primeira escolha ("stop", "length", ...). Vazio se o servidor não informar.</summary>
        public string FinishReason { get; set; } = string.Empty;

        public string RawBody { get; set; } = string.Empty;
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
    }

    /// <summary>
    /// Cliente HTTP genérico para APIs de chat compatíveis com o formato OpenAI
    /// (endpoint POST /v1/chat/completions): DeepSeek, Ollama, LM Studio, llama.cpp etc.
    /// Sem dependências externas (apenas BCL).
    /// </summary>
    public class LlmClient
    {
        private const string DefaultBaseUrl = "https://api.deepseek.com/";

        // Sem timeout global: cada requisição usa o LlmOptions.RequestTimeout.
        private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        private readonly HttpClient _httpClient;

        public LlmClient()
        {
            _httpClient = SharedHttpClient;
        }

        /// <summary>Usa um handler próprio (testes, proxies).</summary>
        public LlmClient(HttpMessageHandler handler)
        {
            _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
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
        /// Com <paramref name="maxTokens"/> &lt;= 0 o campo <c>max_tokens</c> é omitido.
        /// </summary>
        public static object BuildRequestBody(
            string modelName,
            string baseUrl,
            List<object> apiMessages,
            double temperature,
            int maxTokens,
            bool enableThinking,
            ThinkingControl thinking = ThinkingControl.Auto)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = modelName,
                ["messages"] = apiMessages
            };

            bool sendThinking = thinking switch
            {
                ThinkingControl.DeepSeekToggle => true,
                ThinkingControl.None => false,
                _ => SupportsThinkingToggle(modelName, baseUrl)
            };
            if (sendThinking)
            {
                body["thinking"] = new { type = enableThinking ? "enabled" : "disabled" };
            }

            body["temperature"] = temperature;
            if (maxTokens > 0)
            {
                body["max_tokens"] = maxTokens;
            }

            body["stream"] = false;
            return body;
        }

        /// <summary>
        /// URL completa de um caminho da API (<c>chat/completions</c>, <c>models</c>) a partir da
        /// URL base, com ou sem o <c>/v1</c> no fim. Ex: <c>http://localhost:11434/v1</c> e
        /// <c>http://localhost:11434/</c> dão o mesmo endpoint.
        /// </summary>
        public static string BuildEndpoint(string baseUrl, string path)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = DefaultBaseUrl;
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            string endpoint = baseUrl + "v1/" + path;
            if (endpoint.Contains("/v1/v1/")) endpoint = endpoint.Replace("/v1/v1/", "/v1/");
            return endpoint;
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

            if (options.ApiKeyRequired && string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException("Chave de API não configurada.");
            }

            string baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? DefaultBaseUrl : options.BaseUrl;
            string modelName = options.Model ?? "deepseek-v4-pro";

            object requestBody = BuildRequestBody(
                modelName,
                baseUrl,
                BuildApiMessages(messages),
                temperature ?? options.Temperature,
                options.MaxTokens,
                options.EnableThinkingMode,
                options.Thinking);

            string jsonContent = JsonSerializer.Serialize(requestBody, SerializerOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(baseUrl, "chat/completions"));
            Authorize(request, options);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            string responseBody = await SendAsync(request, options.RequestTimeout, cancellationToken);
            return LlmResponseParser.Parse(responseBody);
        }

        /// <summary>
        /// Lista os modelos do servidor (<c>GET /v1/models</c>). Útil para servidores locais,
        /// onde o id do modelo depende do que está instalado.
        /// </summary>
        public async Task<IReadOnlyList<string>> ListModelsAsync(LlmOptions options, CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, BuildEndpoint(options.BaseUrl, "models"));
            Authorize(request, options);

            string responseBody = await SendAsync(request, options.RequestTimeout, cancellationToken);
            return LlmResponseParser.ParseModelIds(responseBody);
        }

        private static void Authorize(HttpRequestMessage request, LlmOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        }

        private async Task<string> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                timeoutSource.CancelAfter(timeout);
            }

            try
            {
                using var response = await _httpClient.SendAsync(request, timeoutSource.Token);
                string responseBody = await response.Content.ReadAsStringAsync(timeoutSource.Token);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Erro na API da IA ({(int)response.StatusCode}): {responseBody}", null, response.StatusCode);
                }

                return responseBody;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"A API da IA não respondeu em {timeout.TotalSeconds:0} s ({request.RequestUri}).");
            }
        }
    }
}
