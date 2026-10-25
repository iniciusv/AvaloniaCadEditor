using System.Globalization;
using System.Text.Json;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Harness.Tools;

/// <summary>Argumento inválido: a mensagem volta ao modelo como "Erro: ...".</summary>
public sealed class ToolArgumentException(string message) : Exception(message);

/// <summary>
/// Leitura tolerante dos argumentos de uma chamada: modelos locais às vezes mandam número como
/// texto ("12.5") ou a vista em português ("frente"). O que não dá para entender vira
/// <see cref="ToolArgumentException"/>.
/// </summary>
public readonly struct ToolArgs
{
    private readonly JsonElement _root;

    public ToolArgs(JsonElement root) => _root = root;

    private bool TryGet(string name, out JsonElement value)
    {
        value = default;
        return _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;
    }

    public string? OptionalString(string name) =>
        TryGet(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;

    public string String(string name) =>
        OptionalString(name) is { Length: > 0 } s ? s : throw new ToolArgumentException($"falta o argumento '{name}'.");

    public double? OptionalNumber(string name)
    {
        if (!TryGet(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
        throw new ToolArgumentException($"'{name}' deveria ser um número.");
    }

    public double Number(string name) => OptionalNumber(name) ?? throw new ToolArgumentException($"falta o argumento '{name}'.");

    public bool Bool(string name, bool defaultValue)
    {
        if (!TryGet(name, out var v)) return defaultValue;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(v.GetString(), out bool b) => b,
            _ => throw new ToolArgumentException($"'{name}' deveria ser true ou false.")
        };
    }

    public int Int(string name, int defaultValue) => OptionalNumber(name) is { } d ? (int)Math.Round(d) : defaultValue;

    /// <summary>[x, y, z] (também aceita {"x":..,"y":..,"z":..}).</summary>
    public Vector3D? OptionalVector(string name)
    {
        if (!TryGet(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3)
            return new Vector3D(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("x", out var x) && v.TryGetProperty("y", out var y) && v.TryGetProperty("z", out var z))
            return new Vector3D(x.GetDouble(), y.GetDouble(), z.GetDouble());
        throw new ToolArgumentException($"'{name}' deveria ser [x, y, z].");
    }

    /// <summary>"X", "-y", "z" ou [x, y, z].</summary>
    public Vector3D Axis(string name)
    {
        if (TryGet(name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString()!.Trim().ToUpperInvariant() switch
            {
                "X" or "+X" => Vector3D.UnitX,
                "-X" => new Vector3D(-1, 0, 0),
                "Y" or "+Y" => Vector3D.UnitY,
                "-Y" => new Vector3D(0, -1, 0),
                "Z" or "+Z" => Vector3D.UnitZ,
                "-Z" => new Vector3D(0, 0, -1),
                _ => throw new ToolArgumentException($"'{name}' deveria ser X, Y, Z (com sinal opcional) ou [x, y, z].")
            };
        }
        var vector = OptionalVector(name) ?? throw new ToolArgumentException($"falta o argumento '{name}'.");
        if (vector.Length < 1e-12) throw new ToolArgumentException($"'{name}' não pode ser o vetor nulo.");
        return vector.Normalize();
    }

    public StandardView View(string name, StandardView defaultValue)
    {
        string? text = OptionalString(name);
        if (string.IsNullOrWhiteSpace(text)) return defaultValue;
        return text.Trim().ToLowerInvariant() switch
        {
            "front" or "frente" or "frontal" => StandardView.Front,
            "back" or "tras" or "trás" or "traseira" => StandardView.Back,
            "right" or "direita" or "lateral" => StandardView.Right,
            "left" or "esquerda" => StandardView.Left,
            "top" or "topo" or "cima" or "superior" => StandardView.Top,
            "bottom" or "baixo" or "inferior" => StandardView.Bottom,
            "iso" or "isometric" or "isométrica" or "isometrica" => StandardView.Isometric,
            _ => throw new ToolArgumentException($"vista '{text}' desconhecida. Use Front, Back, Right, Left, Top, Bottom ou Isometric.")
        };
    }

    public static readonly string[] ViewNames = { "Front", "Back", "Right", "Left", "Top", "Bottom", "Isometric" };
}
