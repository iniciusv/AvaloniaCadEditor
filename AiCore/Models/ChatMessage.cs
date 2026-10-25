using System;
using System.Collections.Generic;

namespace AiCore.Models
{
    /// <summary>
    /// Mensagem de conversa agnóstica de UI (POCO puro, sem dependência de Mvvm/WinUI).
    /// Use Role = "system" | "user" | "assistant" | "tool".
    /// </summary>
    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Conteúdo de raciocínio ("reasoning_content") retornado por modelos que suportam thinking mode.
        /// </summary>
        public string ReasoningContent { get; set; } = string.Empty;

        /// <summary>Chamadas de ferramenta pedidas nesta resposta (só em <c>assistant</c>).</summary>
        public IReadOnlyList<ToolCall> ToolCalls { get; set; } = Array.Empty<ToolCall>();

        /// <summary>A chamada que esta mensagem responde (só em <c>tool</c>).</summary>
        public string ToolCallId { get; set; } = string.Empty;

        public bool IsSystem => string.Equals(Role, "system", StringComparison.OrdinalIgnoreCase);
        public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
        public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase);
        public bool IsTool => string.Equals(Role, "tool", StringComparison.OrdinalIgnoreCase);
    }
}
