namespace AiCore.Models
{
    /// <summary>
    /// Uma chamada de ferramenta pedida pelo modelo (<c>message.tool_calls[]</c>). O resultado volta
    /// numa mensagem <c>role = "tool"</c> com o mesmo <see cref="Id"/> em <see cref="ChatMessage.ToolCallId"/>.
    /// </summary>
    public sealed class ToolCall
    {
        public ToolCall(string id, string name, string argumentsJson)
        {
            Id = id;
            Name = name;
            ArgumentsJson = argumentsJson;
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>Argumentos em JSON (sempre texto, mesmo quando o servidor manda um objeto).</summary>
        public string ArgumentsJson { get; }
    }
}
