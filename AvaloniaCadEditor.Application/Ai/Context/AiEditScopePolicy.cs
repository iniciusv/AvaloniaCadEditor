using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Application.Ai.Context;

/// <summary>
/// Regras sobre os arquivos que a IA vê e altera: em que formato cada arquivo do projeto está e
/// quais arquivos uma resposta pode tocar.
/// </summary>
public static class AiEditScopePolicy
{
    /// <summary>Pasta das peças; o único lugar onde a IA pode criar arquivos.</summary>
    public const string PartsFolder = "Pecas/";

    /// <summary>
    /// Formato de um arquivo do projeto: o de entrada é script; um arquivo que não declara peça
    /// (ex.: Medidas.cs) é auxiliar; os demais são de peça.
    /// </summary>
    public static CadFileKind KindOf(string filePath, string entryFile, SceneSyntaxIndex index)
    {
        if (string.Equals(filePath, entryFile, StringComparison.OrdinalIgnoreCase)) return CadFileKind.SceneEntry;
        return index.Find(filePath) != null && !index.DeclaresPart(filePath) ? CadFileKind.SupportFile : CadFileKind.PartFile;
    }

    /// <summary>
    /// Dá a cada bloco devolvido o caminho do arquivo (bloco sem <c>file=</c> = arquivo ativo) e, no
    /// modo multi-arquivo, recusa arquivos fora do contexto, exceto uma peça nova em Pecas/.
    /// No modo de arquivo único, vale o último bloco da resposta, com ou sem <c>file=</c>.
    /// </summary>
    public static (IReadOnlyList<ScriptDocument> Changes, string? Error) ResolveEdits(CadScriptChanges payload, CadScriptContext context)
    {
        string activeFile = context.FileName ?? ScriptDocument.DefaultSceneFile;
        if (context.Scope == null)
        {
            return (new[] { new ScriptDocument(activeFile, payload.Files[^1].Code) }, null);
        }

        var changes = payload.Files
            .Select(f => new ScriptDocument(f.FilePath ?? activeFile, f.Code))
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var outOfScope = changes
            .Where(c => !context.Scope.Contains(c.FilePath) && !IsNewPartFile(c.FilePath, context.ProjectDocuments))
            .Select(c => c.FilePath)
            .ToList();

        return outOfScope.Count == 0
            ? (changes, null)
            : (changes, $"Os arquivos {string.Join(", ", outOfScope)} não estão no contexto e não podem ser alterados. " +
                        "Altere só os arquivos listados em \"### ARQUIVOS NO CONTEXTO\" (ou crie uma peça nova em Pecas/).");
    }

    /// <summary>Um .cs novo em Pecas/ (sem sair da pasta), que ainda não existe no projeto.</summary>
    public static bool IsNewPartFile(string filePath, IReadOnlyList<ScriptDocument>? projectDocuments) =>
        filePath.StartsWith(PartsFolder, StringComparison.OrdinalIgnoreCase) &&
        filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
        !filePath.Contains("..", StringComparison.Ordinal) &&
        !(projectDocuments ?? Array.Empty<ScriptDocument>())
            .Any(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
}
