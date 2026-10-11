using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AvaloniaCadEditor.Application.Ai.Recording;

/// <summary>Lê e grava <see cref="AiConversationRecord"/> como JSON legível (código e acentos sem escape).</summary>
public static class AiConversationRecordStore
{
    /// <summary>Onde o chat grava as conversas: %AppData%/AvaloniaCadEditor/ai-cases.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AvaloniaCadEditor", "ai-cases");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(AiConversationRecord record) => JsonSerializer.Serialize(record, Options);

    public static AiConversationRecord Deserialize(string json) =>
        JsonSerializer.Deserialize<AiConversationRecord>(json, Options)
        ?? throw new InvalidDataException("Arquivo de conversa vazio.");

    public static AiConversationRecord Load(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>Grava em <paramref name="directory"/> como "yyyyMMdd-HHmmss-&lt;primeiro pedido&gt;.json" e devolve o caminho.</summary>
    public static string Save(AiConversationRecord record, string directory)
    {
        Directory.CreateDirectory(directory);
        string slug = Slug(record.Rounds.FirstOrDefault()?.Instruction ?? "conversa");
        string path = Path.Combine(directory, $"{record.CreatedAt:yyyyMMdd-HHmmss}-{slug}.json");
        File.WriteAllText(path, Serialize(record));
        return path;
    }

    /// <summary>"Crie um suporte em L!" → "crie-um-suporte-em-l" (sem acentos, até 40 caracteres).</summary>
    private static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            if (sb.Length >= 40) break;
        }
        string slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "conversa" : slug;
    }
}
