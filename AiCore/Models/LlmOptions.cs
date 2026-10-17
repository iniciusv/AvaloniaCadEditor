using System;

namespace AiCore.Models
{
    /// <summary>
    /// Como o campo <c>thinking</c> (toggle do thinking mode da DeepSeek) entra na requisição.
    /// </summary>
    public enum ThinkingControl
    {
        /// <summary>Decide pelo destino: envia se o modelo ou a URL forem da DeepSeek.</summary>
        Auto,

        /// <summary>Sempre envia <c>thinking: enabled|disabled</c>.</summary>
        DeepSeekToggle,

        /// <summary>Nunca envia (servidores locais e outros provedores podem rejeitar o campo).</summary>
        None
    }

    /// <summary>
    /// Configuração da chamada à LLM, desacoplada de qualquer mecanismo de persistência de settings.
    /// </summary>
    public class LlmOptions
    {
        /// <summary>Chave de autenticação (Bearer token) da API.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Se a chave é obrigatória. Servidores locais (Ollama, LM Studio, llama.cpp) não exigem
        /// chave: com <c>false</c> e chave vazia, a requisição sai sem o header <c>Authorization</c>.
        /// </summary>
        public bool ApiKeyRequired { get; set; } = true;

        /// <summary>URL base da API (sem o caminho do endpoint). Ex: "https://api.deepseek.com/", "http://localhost:11434/v1".</summary>
        public string BaseUrl { get; set; } = "https://api.deepseek.com/";

        /// <summary>Nome do modelo a ser usado.</summary>
        public string Model { get; set; } = "deepseek-v4-pro";

        /// <summary>Temperatura da amostragem (0.0 a 2.0). Valores baixos = respostas mais determinísticas.</summary>
        public double Temperature { get; set; } = 0.3;

        /// <summary>
        /// Limite máximo de tokens na resposta. 0 = omite <c>max_tokens</c> e vale o padrão do
        /// servidor (o default de 384000 é o da DeepSeek; modelos locais têm contexto bem menor).
        /// </summary>
        public int MaxTokens { get; set; } = 384000;

        /// <summary>Habilita o "thinking mode" em modelos compatíveis (ex: deepseek-v4, reasoner).</summary>
        public bool EnableThinkingMode { get; set; } = true;

        /// <summary>Quando o campo <c>thinking</c> vai na requisição. Ver <see cref="ThinkingControl"/>.</summary>
        public ThinkingControl Thinking { get; set; } = ThinkingControl.Auto;

        /// <summary>Tempo máximo de uma requisição. Modelos locais grandes podem precisar de mais.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(15);
    }
}
