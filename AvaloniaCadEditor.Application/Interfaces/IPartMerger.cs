using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Interfaces;

/// <summary>Como o sólido incorporado entra na booleana que o <c>Build()</c> da peça devolve.</summary>
public enum PartMergeMode
{
    /// <summary>
    /// Une o sólido ao resultado final da peça, DEPOIS dos cortes: os vazios que a peça já tem não o
    /// apagam (ex.: um dente desenhado dentro de um rasgo).
    /// </summary>
    UnionAfterCuts,

    /// <summary>
    /// Entra nas bases da <c>BooleanFeature.CreateDifference</c> final, ANTES dos cortes: os vazios da
    /// peça também furam o sólido novo. Só quando o <c>Build()</c> devolve uma <c>CreateDifference</c>.
    /// </summary>
    UnionBeforeCuts,

    /// <summary>O sólido vira ferramenta de corte (furo/rasgo) da peça.</summary>
    SubtractAsTool
}

/// <summary>
/// O que incorporar e onde.
/// </summary>
/// <param name="FilePath">Arquivo de entrada da cena.</param>
/// <param name="Line">Linha do <c>AddFeature</c> do sólido solto.</param>
/// <param name="PartTypeName">O record da peça que recebe o sólido.</param>
/// <param name="TargetPlacement">
/// O placement AVALIADO da instância alvo; o inverso dele (translação e rotação) leva o mundo para o
/// referencial local da peça.
/// </param>
/// <param name="RegisteredMove">
/// O placement avaliado de <c>AddFeature(x.Transformed(P))</c>, quando o sólido foi registrado deslocado
/// (é a <see cref="Domain.Features.TransformedFeature"/> de topo da cena); null se não foi.
/// </param>
public sealed record PartMergeRequest(
    string FilePath, int Line, string PartTypeName, Placement TargetPlacement, Placement? RegisteredMove, PartMergeMode Mode);

/// <summary>
/// Resultado de <see cref="IPartMerger.Merge"/>: os arquivos alterados (a cena e o arquivo da peça) ou o
/// motivo da recusa — nunca os dois. <see cref="Warnings"/> não impedem a incorporação.
/// </summary>
public sealed record PartMerge(IReadOnlyList<ScriptDocument> Changes, string? Refusal, IReadOnlyList<string> Warnings, string? InsertedName)
{
    public static PartMerge Refuse(string reason) => new(Array.Empty<ScriptDocument>(), reason, Array.Empty<string>(), null);

    /// <summary>Os documentos da cena com as mudanças aplicadas.</summary>
    public IReadOnlyList<ScriptDocument> ApplyTo(IReadOnlyList<ScriptDocument> documents)
    {
        var changed = Changes.ToDictionary(c => c.FilePath, StringComparer.OrdinalIgnoreCase);
        return documents.Select(d => changed.TryGetValue(d.FilePath, out var c) ? c : d).ToList();
    }
}

/// <summary>
/// Incorpora um sólido solto da cena (registrado com <c>AddFeature</c>) ao <c>Build()</c> de uma peça
/// que já existe. As coordenadas são reescritas para o referencial LOCAL da peça com a conta exata
/// (o inverso de <see cref="PartMergeRequest.TargetPlacement"/>), e não "de cabeça": é aqui que a rotação do placement
/// costuma ser esquecida. Literais continuam literais; o que não dá para reescrever com segurança fica
/// como estava, envolvido num <c>.Transformed(...)</c>.
/// </summary>
public interface IPartMerger
{
    /// <summary>
    /// Não grava nem executa: quem chama avalia o resultado (e confere a geometria) antes de aplicar.
    /// </summary>
    PartMerge Merge(IReadOnlyList<ScriptDocument> documents, PartMergeRequest request);
}
