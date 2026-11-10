using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Application.Levers;

/// <summary>
/// Grava os valores novos no fonte com edição pontual: troca só o texto de cada literal e deixa o resto do
/// arquivo (formatação, comentários) como estava. Puro: devolve os documentos alterados sem gravar nada.
/// </summary>
public static class LeverSourceEditor
{
    /// <summary>
    /// Os documentos que mudam, com o texto novo. <c>null</c> se algum literal não estiver mais onde a sonda o
    /// viu (o código mudou depois do estudo): gravar ali poderia acertar o número errado.
    /// </summary>
    public static IReadOnlyList<ScriptDocument>? Apply(IReadOnlyList<ScriptDocument> documents, IReadOnlyList<LeverChange> changes)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(changes);

        var result = new List<ScriptDocument>();
        foreach (var file in changes.GroupBy(c => c.Lever.Location.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var document = documents.FirstOrDefault(d => string.Equals(d.FilePath, file.Key, StringComparison.OrdinalIgnoreCase));
            if (document == null) return null;

            var text = document.Text ?? string.Empty;
            foreach (var change in file.OrderByDescending(c => c.Lever.Location.Start))
            {
                var location = change.Lever.Location;
                if (location.Start < 0 || location.Start + location.Length > text.Length ||
                    string.CompareOrdinal(text, location.Start, change.Lever.Text, 0, location.Length) != 0 ||
                    change.Lever.Text.Length != location.Length)
                {
                    return null;
                }

                text = text[..location.Start] + Replacement(text, location.Start, change.Text) + text[(location.Start + location.Length)..];
            }

            if (text != document.Text) result.Add(document with { Text = text });
        }
        return result;
    }

    /// <summary>Número negativo logo depois de <c>-</c> ou <c>+</c> vai entre parênteses: <c>x-5</c> com
    /// −3 viraria <c>x--3</c>, que é o operador de decremento.</summary>
    private static string Replacement(string text, int start, string value)
    {
        if (!value.StartsWith('-')) return value;
        int i = start - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
        return i >= 0 && text[i] is '-' or '+' ? $"({value})" : value;
    }
}
