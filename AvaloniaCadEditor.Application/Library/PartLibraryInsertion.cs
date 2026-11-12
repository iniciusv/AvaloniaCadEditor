using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AvaloniaCadEditor.Application.Scenes;

namespace AvaloniaCadEditor.Application.Library;

/// <summary>
/// O código que o detalhe da biblioteca grava na cena: <c>Add(new Tipo(Largura: 50), at: ...)</c>, ou o
/// modelo pronto pelo nome (<c>Rolamento.F623ZZ</c>) quando nenhum valor saiu dele. Só os parâmetros
/// diferentes do padrão do record vão para a chamada, nomeados. Puro.
/// </summary>
public static class PartLibraryInsertion
{
    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "double", "float", "decimal", "int", "long", "short", "byte", "Double", "Single", "Decimal", "Int32", "Int64"
    };

    /// <summary>
    /// O valor inicial de cada parâmetro: o do modelo pronto, senão o padrão do record, senão o mínimo do
    /// [Range], senão um valor neutro que compila.
    /// </summary>
    public static IReadOnlyDictionary<string, string> InitialValues(PartLibraryItem item, string? presetName = null)
    {
        var preset = item.Presets.FirstOrDefault(p => p.Name == presetName);
        return item.Parameters.ToDictionary(
            p => p.Name,
            p => preset != null && preset.Arguments.TryGetValue(p.Name, out var fromPreset)
                ? fromPreset
                : p.DefaultValue ?? p.RangeMinimum ?? PartSourceEditor.NeutralValue(p.TypeName),
            StringComparer.Ordinal);
    }

    /// <summary>Texto digitado → código C#: tira espaços, aceita vírgula decimal e completa o nome do enum.</summary>
    public static string Normalize(PartTypeParameter parameter, string? text)
    {
        string value = (text ?? string.Empty).Trim();
        if (NumericTypes.Contains(parameter.TypeName) && Regex.IsMatch(value, @"^-?\d+,\d+$"))
        {
            value = value.Replace(',', '.');
        }

        // Enum: "M4" vira "Rosca.M4" quando o padrão do record está escrito como "Rosca.M3".
        if (parameter.DefaultValue is { } d && d.StartsWith(parameter.TypeName + ".", StringComparison.Ordinal)
            && Regex.IsMatch(value, @"^[A-Za-z_]\w*$") && value is not ("true" or "false" or "default" or "null"))
        {
            value = parameter.TypeName + "." + value;
        }
        return value;
    }

    /// <summary>Por que o valor não serve, ou null. Só os tipos simples são conferidos; o resto o compilador confere.</summary>
    public static string? Validate(PartTypeParameter parameter, string? text)
    {
        string value = Normalize(parameter, text);
        if (value.Length == 0) return $"{parameter.Name}: falta o valor.";
        if (NumericTypes.Contains(parameter.TypeName)
            && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return $"{parameter.Name}: \"{text}\" não é um número.";
        }
        if (parameter.TypeName is "bool" or "Boolean" && value is not ("true" or "false"))
        {
            return $"{parameter.Name}: use true ou false.";
        }
        return null;
    }

    /// <summary>
    /// A expressão da peça. Com um modelo pronto escolhido e nenhum valor mudado depois, é o modelo pelo nome;
    /// senão, <c>new Tipo(...)</c> com os parâmetros que diferem do padrão (os obrigatórios sempre).
    /// </summary>
    public static string PartExpression(PartLibraryItem item, IReadOnlyDictionary<string, string> values, string? presetName = null)
    {
        if (presetName != null && item.Presets.Any(p => p.Name == presetName))
        {
            var fromPreset = InitialValues(item, presetName);
            bool untouched = item.Parameters.All(p =>
                Normalize(p, values.TryGetValue(p.Name, out var v) ? v : null) == Normalize(p, fromPreset[p.Name]));
            if (untouched) return $"{item.Name}.{presetName}";
        }

        var arguments = new List<string>();
        foreach (var parameter in item.Parameters)
        {
            string value = values.TryGetValue(parameter.Name, out var typed)
                ? Normalize(parameter, typed)
                : parameter.DefaultValue ?? parameter.RangeMinimum ?? PartSourceEditor.NeutralValue(parameter.TypeName);
            if (parameter.DefaultValue != null && SameValue(parameter, value, parameter.DefaultValue)) continue;
            arguments.Add($"{parameter.Name}: {value}");
        }
        return $"new {item.Name}({string.Join(", ", arguments)})";
    }

    /// <summary>A instrução que vai para o arquivo de entrada.</summary>
    public static string AddStatement(string partExpression, double x = 0, double y = 0, double z = 0) =>
        $"Add({partExpression}, at: {FormattableString.Invariant($"Placement.At({x:0.###}, {y:0.###}, {z:0.###})")});";

    private static bool SameValue(PartTypeParameter parameter, string a, string b)
    {
        if (a == b) return true;
        return NumericTypes.Contains(parameter.TypeName)
            && double.TryParse(a.TrimEnd('d', 'D', 'f', 'F', 'm', 'M'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(b.TrimEnd('d', 'D', 'f', 'F', 'm', 'M'), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && x == y;
    }
}
