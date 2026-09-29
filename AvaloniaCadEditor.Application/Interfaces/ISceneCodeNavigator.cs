using System.Collections.Generic;

namespace AvaloniaCadEditor.Application.Interfaces;

/// <summary>Um trecho de código de um arquivo da cena.</summary>
/// <param name="FilePath">Arquivo, relativo à cena (ex.: "Pecas/Flange.cs").</param>
/// <param name="Start">Posição do primeiro caractere no texto do arquivo.</param>
/// <param name="Length">Tamanho do trecho.</param>
/// <param name="Line">Linha, começando em 1.</param>
/// <param name="Column">Coluna, começando em 1.</param>
/// <param name="LineText">Texto da linha (sem espaços nas pontas), para listas de resultado.</param>
public sealed record CodeLocation(string FilePath, int Start, int Length, int Line, int Column, string LineText)
{
    public override string ToString() => $"{FilePath}({Line}): {LineText}";
}

/// <summary>Onde um símbolo é declarado e todos os lugares em que aparece.</summary>
/// <param name="SymbolName">Nome do símbolo (ex.: "Flange", "Espessura").</param>
/// <param name="Kind">Tipo do símbolo em português (tipo, parâmetro, variável...).</param>
/// <param name="IsInScene">True se é declarado nos arquivos da cena (e portanto pode ser renomeado).</param>
/// <param name="Definitions">Declarações nos arquivos da cena (vazio para símbolos da API).</param>
/// <param name="Occurrences">Todas as ocorrências, declarações incluídas, em ordem de arquivo e posição.</param>
public sealed record SymbolReferences(
    string SymbolName,
    string Kind,
    bool IsInScene,
    IReadOnlyList<CodeLocation> Definitions,
    IReadOnlyList<CodeLocation> Occurrences);

/// <summary>Resultado de renomear: o novo texto de cada arquivo alterado.</summary>
public sealed record RenameResult(string OldName, string NewName, IReadOnlyDictionary<string, string> ChangedDocuments, int Occurrences);

/// <summary>
/// Navegação semântica no código da cena (ir para a definição, achar referências, renomear),
/// usando o mesmo compilador que executa a cena. Entende o C# de verdade: dois símbolos com o
/// mesmo nome em lugares diferentes não se confundem.
/// </summary>
public interface ISceneCodeNavigator
{
    /// <summary>O símbolo na posição dada e suas ocorrências; null se não há símbolo ali.</summary>
    SymbolReferences? FindReferences(IReadOnlyList<ScriptDocument> documents, string filePath, int position);

    /// <summary>
    /// Renomeia o símbolo na posição dada em todos os arquivos. Lança InvalidOperationException
    /// com uma mensagem para o usuário quando não é possível (nome inválido, símbolo da API,
    /// conflito que quebraria a compilação).
    /// </summary>
    RenameResult Rename(IReadOnlyList<ScriptDocument> documents, string filePath, int position, string newName);
}
