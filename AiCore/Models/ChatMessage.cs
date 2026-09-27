using System;

namespace AiCore.Models
{
    /// <summary>
    /// Mensagem de conversa agnóstica de UI (POCO puro, sem dependência de Mvvm/WinUI).
    /// Use Role = "system" | "user" | "assistant".
    /// </summary>
    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Conteúdo de raciocínio ("reasoning_content") retornado por modelos que suportam thinking mode.
        /// </summary>
        public string ReasoningContent { get; set; } = string.Empty;

        public bool IsSystem => string.Equals(Role, "system", StringComparison.OrdinalIgnoreCase);
        public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
        public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase);
    }
}
