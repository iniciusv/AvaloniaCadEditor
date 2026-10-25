using System.Text.Json.Nodes;

namespace AiCore.Models
{
    /// <summary>
    /// Uma função que o modelo pode chamar (<c>tools[].function</c> do formato OpenAI). O modelo
    /// escolhe a ferramenta lendo <see cref="Description"/>; <see cref="Parameters"/> é um JSON Schema.
    /// </summary>
    public sealed class ToolDefinition
    {
        public ToolDefinition(string name, string description, JsonObject parameters, bool strict = false)
        {
            Name = name;
            Description = description;
            Parameters = parameters;
            Strict = strict;
        }

        public string Name { get; }
        public string Description { get; }
        public JsonObject Parameters { get; }

        /// <summary>
        /// Modo estrito (DeepSeek beta): o servidor garante que os argumentos seguem o schema. Exige
        /// todas as propriedades em <c>required</c> e <c>additionalProperties: false</c>.
        /// </summary>
        public bool Strict { get; }
    }
}
