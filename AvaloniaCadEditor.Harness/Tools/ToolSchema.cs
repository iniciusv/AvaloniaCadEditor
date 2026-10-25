using System.Text.Json.Nodes;

namespace AvaloniaCadEditor.Harness.Tools;

/// <summary>Um parâmetro de ferramenta: nome, schema e se é obrigatório.</summary>
public sealed record ToolParameter(string Name, JsonObject Schema, bool Required);

/// <summary>
/// Monta os JSON Schemas dos argumentos num lugar só, sempre com <c>additionalProperties: false</c>
/// (o que o modo estrito da DeepSeek exige, junto com todas as propriedades em <c>required</c>).
/// </summary>
public static class ToolSchema
{
    public static JsonObject Object(params ToolParameter[] parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var p in parameters)
        {
            properties[p.Name] = p.Schema;
            if (p.Required) required.Add(p.Name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };
    }

    public static ToolParameter String(string name, string description, bool required = true, IEnumerable<string>? values = null)
    {
        var schema = new JsonObject { ["type"] = "string", ["description"] = description };
        if (values != null) schema["enum"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        return new ToolParameter(name, schema, required);
    }

    public static ToolParameter Number(string name, string description, bool required = true) =>
        new(name, new JsonObject { ["type"] = "number", ["description"] = description }, required);

    public static ToolParameter Integer(string name, string description, bool required = true) =>
        new(name, new JsonObject { ["type"] = "integer", ["description"] = description }, required);

    public static ToolParameter Boolean(string name, string description, bool required = true) =>
        new(name, new JsonObject { ["type"] = "boolean", ["description"] = description }, required);

    /// <summary>Um ponto ou vetor [x, y, z].</summary>
    public static ToolParameter Vector(string name, string description, bool required = true) =>
        new(name, new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject { ["type"] = "number" },
            ["description"] = description + " Formato [x, y, z]."
        }, required);
}
