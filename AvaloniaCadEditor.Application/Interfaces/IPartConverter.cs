using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniaCadEditor.Application.Interfaces;

/// <summary>
/// Resultado de <see cref="IPartConverter.Convert"/>: os arquivos alterados (o da cena e a peça nova
/// em Pecas/) ou, se a conversão não é segura, o motivo — nunca os dois.
/// </summary>
public sealed record PartConversion(IReadOnlyList<ScriptDocument> Changes, string? Refusal)
{
    public static PartConversion Refuse(string reason) => new(Array.Empty<ScriptDocument>(), reason);

    /// <summary>Os documentos da cena com as mudanças aplicadas (arquivos novos entram no fim).</summary>
    public IReadOnlyList<ScriptDocument> ApplyTo(IReadOnlyList<ScriptDocument> documents)
    {
        var changed = Changes.ToDictionary(c => c.FilePath, StringComparer.OrdinalIgnoreCase);
        return documents
            .Select(d => changed.TryGetValue(d.FilePath, out var c) ? c : d)
            .Concat(Changes.Where(c => !documents.Any(d => string.Equals(d.FilePath, c.FilePath, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }
}

/// <summary>
/// Converte um sólido solto da cena (registrado com <c>AddFeature</c>) numa peça: o código que o
/// produz vai para o <c>Build()</c> de um record novo em Pecas/ e a cena passa a chamar
/// <c>Add(new Peca(...))</c>. Mecânico: literais continuam literais; só as variáveis da cena que o
/// sólido lê (e que ficam na cena) viram parâmetros do record.
/// </summary>
public interface IPartConverter
{
    /// <summary>
    /// Converte o sólido registrado pelo <c>AddFeature(...)</c> da linha <paramref name="line"/> de
    /// <paramref name="filePath"/> no record <paramref name="typeName"/>. Não grava nem executa:
    /// quem chama avalia o resultado antes de aplicar.
    /// </summary>
    PartConversion Convert(IReadOnlyList<ScriptDocument> documents, string filePath, int line, string typeName);
}
