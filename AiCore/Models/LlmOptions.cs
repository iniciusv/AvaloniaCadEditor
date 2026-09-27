namespace AiCore.Models
{
    /// <summary>
    /// Configuração da chamada à LLM, desacoplada de qualquer mecanismo de persistência de settings.
    /// </summary>
    public class LlmOptions
    {
        /// <summary>Chave de autenticação (Bearer token) da API.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>URL base da API (sem o caminho do endpoint). Ex: "https://api.deepseek.com/".</summary>
        public string BaseUrl { get; set; } = "https://api.deepseek.com/";

        /// <summary>Nome do modelo a ser usado.</summary>
        public string Model { get; set; } = "deepseek-v4-pro";

        /// <summary>Temperatura da amostragem (0.0 a 2.0). Valores baixos = respostas mais determinísticas.</summary>
        public double Temperature { get; set; } = 0.3;

        /// <summary>Limite máximo de tokens na resposta. 0 = usa o default do cliente (384000).</summary>
        public int MaxTokens { get; set; } = 384000;

        /// <summary>Habilita o "thinking mode" em modelos compatíveis (ex: deepseek-v4, reasoner).</summary>
        public bool EnableThinkingMode { get; set; } = true;
    }
}
