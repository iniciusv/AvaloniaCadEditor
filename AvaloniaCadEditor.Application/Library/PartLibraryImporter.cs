using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Library;

/// <summary>
/// O que inserir uma peça da biblioteca faz nos arquivos da cena.
/// </summary>
/// <param name="NewFiles">Arquivos a criar na cena (caminho relativo à cena). Vazio quando tudo já está lá.</param>
/// <param name="Warnings">Avisos que não impedem a inserção (ex.: a cena já tem uma versão diferente da peça).</param>
/// <param name="Refusal">Por que não dá para inserir, ou null.</param>
public sealed record PartLibraryImport(IReadOnlyList<ScriptDocument> NewFiles, IReadOnlyList<string> Warnings, string? Refusal)
{
    public static PartLibraryImport Refused(string reason) => new(Array.Empty<ScriptDocument>(), Array.Empty<string>(), reason);
}

/// <summary>
/// Leva uma peça da biblioteca para a cena: o arquivo dela e o das peças e tipos de que ela depende (fecho
/// transitivo dos tipos declarados na biblioteca, inclusive enums e records auxiliares) são copiados para
/// <c>Pecas/</c>. A cena não guarda caminho para a biblioteca: depois da cópia ela é completa na própria pasta.
/// <para>
/// Nunca sobrescreve: um tipo que a cena já declara fica com a versão da cena (com aviso, se o texto difere),
/// e um arquivo que só em parte já existe na cena, ou um caminho de destino ocupado por outro conteúdo, recusa.
/// </para>
/// Puro: recebe e devolve textos.
/// </summary>
public static class PartLibraryImporter
{
    /// <summary>Pasta da cena que recebe as cópias.</summary>
    public const string TargetFolder = "Pecas";

    /// <param name="library">Arquivos da biblioteca, com caminho relativo à pasta dela.</param>
    /// <param name="scene">Fontes atuais da cena, com caminho relativo à pasta dela.</param>
    /// <param name="typeName">A peça a inserir.</param>
    public static PartLibraryImport Plan(IReadOnlyList<ScriptDocument> library, IReadOnlyList<ScriptDocument> scene, string typeName)
    {
        var libraryIndex = PartDependencyIndex.Of(library);
        var files = libraryIndex.FilesDeclaring(typeName);
        if (files.Count == 0) return PartLibraryImport.Refused($"A biblioteca não tem a peça {typeName}.");

        var closure = libraryIndex.Dependencies(typeName, out string? ambiguity);
        if (ambiguity != null) return PartLibraryImport.Refused(ambiguity);

        var sceneIndex = PartDependencyIndex.Of(scene);
        var sceneByPath = scene.ToDictionary(d => d.FilePath, d => d.Text, StringComparer.OrdinalIgnoreCase);
        var newFiles = new List<ScriptDocument>();
        var warnings = new List<string>();

        foreach (var file in closure)
        {
            var declared = libraryIndex.TypesIn(file.FilePath);
            var alreadyInScene = declared.Where(t => sceneIndex.FilesDeclaring(t).Count > 0).ToList();

            if (alreadyInScene.Count == declared.Count && declared.Count > 0)
            {
                var sceneFiles = alreadyInScene.SelectMany(sceneIndex.FilesDeclaring).Distinct().ToList();
                bool same = sceneFiles.Count == 1 && sceneByPath.TryGetValue(sceneFiles[0], out var sceneText) && SameText(sceneText, file.Text);
                if (!same)
                {
                    warnings.Add($"A cena já tem {string.Join(", ", alreadyInScene)} ({string.Join(", ", sceneFiles)}), diferente da biblioteca: ficou a versão da cena.");
                }
                continue;
            }

            if (alreadyInScene.Count > 0)
            {
                return PartLibraryImport.Refused(
                    $"{file.FilePath} declara {string.Join(", ", declared)}, mas a cena já tem {string.Join(", ", alreadyInScene)}: " +
                    "copiar o arquivo repetiria esses tipos. Resolva na cena antes de inserir.");
            }

            string target = TargetPath(file.FilePath);
            // Arquivo vazio no destino é sobra de um Ctrl+Z da inserção (desfazer esvazia, não apaga): pode ocupar.
            bool occupied = sceneByPath.TryGetValue(target, out var existing) && !string.IsNullOrWhiteSpace(existing);
            if (occupied || newFiles.Any(f => string.Equals(f.FilePath, target, StringComparison.OrdinalIgnoreCase)))
            {
                return PartLibraryImport.Refused($"A cena já tem um {target} com outro conteúdo; renomeie um dos dois antes de inserir.");
            }
            newFiles.Add(new ScriptDocument(target, file.Text));
        }

        return new PartLibraryImport(newFiles, warnings, null);
    }

    /// <summary>
    /// Os arquivos de que a peça precisa para compilar, começando pelo dela: segue todo nome usado no código
    /// que é um tipo declarado em outro arquivo do conjunto. Serve à biblioteca e à cena (miniaturas).
    /// </summary>
    public static IReadOnlyList<ScriptDocument> Dependencies(IReadOnlyList<ScriptDocument> documents, string typeName, out string? ambiguity) =>
        PartDependencyIndex.Of(documents).Dependencies(typeName, out ambiguity);

    /// <summary>Onde o arquivo da biblioteca fica na cena: <c>Pecas/</c> + o nome do arquivo, sem a subpasta.</summary>
    public static string TargetPath(string libraryPath)
    {
        string name = libraryPath.Replace('\\', '/').Split('/').Last();
        return $"{TargetFolder}/{name}";
    }

    private static bool SameText(string a, string b) =>
        string.Equals(a.Replace("\r\n", "\n").TrimEnd(), b.Replace("\r\n", "\n").TrimEnd(), StringComparison.Ordinal);
}

/// <summary>
/// Quais tipos cada arquivo declara e quais nomes ele usa: o grafo de dependências de um conjunto de arquivos
/// (biblioteca ou cena). Montado uma vez e consultado para cada peça.
/// </summary>
public sealed class PartDependencyIndex
{
    private readonly Dictionary<string, ScriptDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _declaredIn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _typesByFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _namesByFile = new(StringComparer.OrdinalIgnoreCase);

    public static PartDependencyIndex Of(IReadOnlyList<ScriptDocument> documents)
    {
        var index = new PartDependencyIndex();
        foreach (var doc in documents)
        {
            if (index._documents.ContainsKey(doc.FilePath)) continue;
            index._documents[doc.FilePath] = doc;

            var root = CSharpSyntaxTree.ParseText(doc.Text, LibraryParse.Options).GetRoot();
            var types = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                .Where(t => t.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
                .Select(t => t.Identifier.ValueText)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var delegates = root.DescendantNodes().OfType<DelegateDeclarationSyntax>()
                .Where(t => t.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
                .Select(t => t.Identifier.ValueText);
            types.AddRange(delegates.Where(d => !types.Contains(d)));

            index._typesByFile[doc.FilePath] = types;
            foreach (var type in types)
            {
                if (!index._declaredIn.TryGetValue(type, out var files)) index._declaredIn[type] = files = new List<string>();
                files.Add(doc.FilePath);
            }

            index._namesByFile[doc.FilePath] = root.DescendantNodes()
                .OfType<SimpleNameSyntax>()
                .Select(n => n.Identifier.ValueText)
                .Where(n => !types.Contains(n))
                .ToHashSet(StringComparer.Ordinal);
        }
        return index;
    }

    /// <summary>
    /// Os arquivos de que a peça precisa para compilar, começando pelo dela: segue todo nome usado no código
    /// que é um tipo declarado em outro arquivo do conjunto.
    /// </summary>
    public IReadOnlyList<ScriptDocument> Dependencies(string typeName, out string? ambiguity)
    {
        ambiguity = null;
        var result = new List<ScriptDocument>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(typeName);
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            string type = pending.Dequeue();
            if (!seenTypes.Add(type)) continue;

            var files = FilesDeclaring(type);
            if (files.Count == 0) continue;
            if (files.Count > 1)
            {
                ambiguity = $"{type} está declarado em mais de um arquivo ({string.Join(", ", files)}); deixe um só.";
                return Array.Empty<ScriptDocument>();
            }

            string file = files[0];
            if (!visited.Add(file)) continue;
            result.Add(Document(file));
            foreach (var used in NamesUsedIn(file))
            {
                if (FilesDeclaring(used).Count > 0) pending.Enqueue(used);
            }
        }
        return result;
    }

    public IReadOnlyList<string> FilesDeclaring(string type) =>
        _declaredIn.TryGetValue(type, out var files) ? files : (IReadOnlyList<string>)Array.Empty<string>();

    public IReadOnlyList<string> TypesIn(string file) =>
        _typesByFile.TryGetValue(file, out var types) ? types : (IReadOnlyList<string>)Array.Empty<string>();

    public IEnumerable<string> NamesUsedIn(string file) =>
        _namesByFile.TryGetValue(file, out var names) ? names : Enumerable.Empty<string>();

    public ScriptDocument Document(string file) => _documents[file];
}

internal static class LibraryParse
{
    public static readonly CSharpParseOptions Options = new(LanguageVersion.Latest, kind: SourceCodeKind.Regular);
}
