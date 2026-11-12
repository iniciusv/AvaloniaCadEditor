using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Library;

/// <summary>Como uma peça está na cena em relação à biblioteca.</summary>
public enum PartLibraryState
{
    /// <summary>Só na biblioteca.</summary>
    LibraryOnly,

    /// <summary>Só na cena: pode ser enviada para a biblioteca.</summary>
    SceneOnly,

    /// <summary>Nas duas, com o mesmo texto ("na cena").</summary>
    Same,

    /// <summary>Nas duas, com texto diferente ("alterada"): atualizar de lá ou enviar para lá.</summary>
    Altered
}

/// <summary>Arquivos a gravar na biblioteca (caminho relativo a ela).</summary>
/// <param name="Writes">Os arquivos, com o texto da cena.</param>
/// <param name="Warnings">Avisos que não impedem o envio.</param>
/// <param name="Refusal">Por que não dá para enviar, ou null.</param>
public sealed record PartLibraryExport(IReadOnlyList<ScriptDocument> Writes, IReadOnlyList<string> Warnings, string? Refusal)
{
    public static PartLibraryExport Refused(string reason) => new(Array.Empty<ScriptDocument>(), Array.Empty<string>(), reason);
}

/// <summary>
/// O caminho de volta e a comparação: enviar uma peça da cena para a biblioteca, trazer a versão da biblioteca
/// para a cena e dizer, por tipo, se as duas estão iguais. A ligação entre a cópia da cena e a da biblioteca é
/// o nome do tipo (o tipo é global na cena, então não há ambiguidade do lado dela); a cópia não leva cabeçalho
/// nem caminho da biblioteca. Puro: recebe e devolve textos.
/// </summary>
public static class PartLibrarySync
{
    /// <summary>Estado de cada tipo de peça que aparece em pelo menos um dos lados.</summary>
    public static IReadOnlyDictionary<string, PartLibraryState> Compare(IReadOnlyList<ScriptDocument> scene, IReadOnlyList<ScriptDocument> library)
    {
        var sceneIndex = PartDependencyIndex.Of(scene);
        var libraryIndex = PartDependencyIndex.Of(library);
        var sceneParts = PartLibraryCatalog.Read(scene, PartLibrarySource.Scene).Select(i => i.Name);
        var libraryParts = PartLibraryCatalog.Read(library, PartLibrarySource.Library).Select(i => i.Name);

        var result = new Dictionary<string, PartLibraryState>(StringComparer.Ordinal);
        foreach (var name in sceneParts.Concat(libraryParts).Distinct(StringComparer.Ordinal))
        {
            var inScene = sceneIndex.FilesDeclaring(name);
            var inLibrary = libraryIndex.FilesDeclaring(name);
            result[name] = (inScene.Count, inLibrary.Count) switch
            {
                (0, _) => PartLibraryState.LibraryOnly,
                (_, 0) => PartLibraryState.SceneOnly,
                _ => SameText(sceneIndex.Document(inScene[0]).Text, libraryIndex.Document(inLibrary[0]).Text)
                    ? PartLibraryState.Same
                    : PartLibraryState.Altered
            };
        }
        return result;
    }

    /// <summary>
    /// Enviar a peça <paramref name="typeName"/> da cena para a biblioteca, com as peças e tipos de que ela depende.
    /// A peça vai para o arquivo que já a declara na biblioteca (substitui: o usuário mandou, e a pasta é
    /// versionável), ou para <paramref name="folderForNew"/> se for nova. Uma dependência que a biblioteca já tem
    /// fica como está (com aviso, se diferente): só a peça escolhida substitui o que há lá.
    /// </summary>
    /// <param name="entryFile">O arquivo de entrada da cena: ele nunca vai para a biblioteca.</param>
    public static PartLibraryExport PlanSend(IReadOnlyList<ScriptDocument> scene, IReadOnlyList<ScriptDocument> library,
        string typeName, string folderForNew, string entryFile)
    {
        var sceneIndex = PartDependencyIndex.Of(scene);
        var files = sceneIndex.Dependencies(typeName, out var ambiguity);
        if (ambiguity != null) return PartLibraryExport.Refused(ambiguity);
        if (files.Count == 0) return PartLibraryExport.Refused($"A cena não declara a peça {typeName}.");

        var entryHit = files.FirstOrDefault(f => string.Equals(f.FilePath, entryFile, StringComparison.OrdinalIgnoreCase) || HasTopLevelStatements(f.Text));
        if (entryHit != null)
        {
            return PartLibraryExport.Refused(
                $"{typeName} usa um tipo declarado em {entryHit.FilePath}, o arquivo de entrada da cena, que não vai para a biblioteca. " +
                "Mova esse tipo para um arquivo em Pecas/ antes de enviar.");
        }

        var libraryIndex = PartDependencyIndex.Of(library);
        var libraryByPath = library.ToDictionary(d => d.FilePath, d => d.Text, StringComparer.OrdinalIgnoreCase);
        var writes = new List<ScriptDocument>();
        var warnings = new List<string>();
        string folder = NormalizeFolder(folderForNew);

        foreach (var file in files)
        {
            bool isMain = file == files[0];
            var declared = sceneIndex.TypesIn(file.FilePath);
            var libraryFiles = declared.SelectMany(libraryIndex.FilesDeclaring).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (libraryFiles.Count > 1)
            {
                return PartLibraryExport.Refused(
                    $"Na biblioteca, os tipos de {file.FilePath} ({string.Join(", ", declared)}) estão em mais de um arquivo ({string.Join(", ", libraryFiles)}); " +
                    "junte-os ou renomeie antes de enviar.");
            }

            if (libraryFiles.Count == 1)
            {
                string target = libraryFiles[0];
                var there = libraryIndex.TypesIn(target);
                if (!there.All(declared.Contains))
                {
                    return PartLibraryExport.Refused(
                        $"{target}, na biblioteca, declara também {string.Join(", ", there.Except(declared))}, que {file.FilePath} não tem: substituí-lo apagaria esses tipos.");
                }
                if (SameText(libraryByPath[target], file.Text)) continue;
                if (!isMain)
                {
                    warnings.Add($"{string.Join(", ", declared)} já está na biblioteca ({target}), diferente da cena: ficou o da biblioteca. Envie essa peça também se quiser substituí-la.");
                    continue;
                }
                writes.Add(new ScriptDocument(target, file.Text));
                continue;
            }

            string path = folder + FileName(file.FilePath);
            if (libraryByPath.TryGetValue(path, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                return PartLibraryExport.Refused($"A biblioteca já tem um {path} com outro conteúdo; renomeie um dos dois antes de enviar.");
            }
            writes.Add(new ScriptDocument(path, file.Text));
        }

        return new PartLibraryExport(writes, warnings, null);
    }

    /// <summary>
    /// Trazer a versão da biblioteca para a cena: o arquivo da cena que declara a peça recebe o texto da biblioteca
    /// (no mesmo caminho da cena), e as dependências que faltarem são copiadas como no Inserir.
    /// </summary>
    public static PartLibraryImport PlanUpdate(IReadOnlyList<ScriptDocument> scene, IReadOnlyList<ScriptDocument> library, string typeName)
    {
        var sceneIndex = PartDependencyIndex.Of(scene);
        var libraryIndex = PartDependencyIndex.Of(library);
        var sceneFiles = sceneIndex.FilesDeclaring(typeName);
        var libraryFiles = libraryIndex.FilesDeclaring(typeName);
        if (sceneFiles.Count != 1) return PartLibraryImport.Refused($"A cena não tem {typeName} num arquivo só.");
        if (libraryFiles.Count != 1) return PartLibraryImport.Refused($"A biblioteca não tem {typeName} num arquivo só.");

        var sceneTypes = sceneIndex.TypesIn(sceneFiles[0]);
        var libraryTypes = libraryIndex.TypesIn(libraryFiles[0]);
        if (!sceneTypes.All(libraryTypes.Contains))
        {
            return PartLibraryImport.Refused(
                $"{sceneFiles[0]} declara também {string.Join(", ", sceneTypes.Except(libraryTypes))}, que a versão da biblioteca não tem: atualizar apagaria esses tipos.");
        }

        var updated = new ScriptDocument(sceneFiles[0], libraryIndex.Document(libraryFiles[0]).Text);
        var overlaid = scene.Select(d => string.Equals(d.FilePath, updated.FilePath, StringComparison.OrdinalIgnoreCase) ? updated : d).ToList();
        var missing = PartLibraryImporter.Plan(library, overlaid, typeName);
        if (missing.Refusal != null) return missing;

        return new PartLibraryImport(missing.NewFiles.Prepend(updated).ToList(), missing.Warnings, null);
    }

    private static bool HasTopLevelStatements(string text) =>
        ((CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text, LibraryParse.Options).GetRoot()).Members.OfType<GlobalStatementSyntax>().Any();

    private static string NormalizeFolder(string folder)
    {
        string trimmed = (folder ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
        return trimmed.Length == 0 ? string.Empty : trimmed + "/";
    }

    private static string FileName(string path) => path.Replace('\\', '/').Split('/').Last();

    internal static bool SameText(string a, string b) =>
        string.Equals(a.Replace("\r\n", "\n").TrimEnd(), b.Replace("\r\n", "\n").TrimEnd(), StringComparison.Ordinal);
}

/// <summary>
/// Uma peça da biblioteca citada no chat. Para a IA, ela aparece como se já estivesse na cena, no caminho onde a
/// cópia ficaria (<c>Pecas/X.cs</c>): a resposta que a usa compila na validação, e a biblioteca nunca é escrita
/// pela IA. Ao aplicar a resposta, os arquivos que ela usa e a cena ainda não tem são copiados antes.
/// </summary>
public static class PartLibraryCitation
{
    /// <summary>Os arquivos das peças citadas (e dependências) que a cena ainda não tem, nos caminhos da cena.</summary>
    public static IReadOnlyList<ScriptDocument> ContextDocuments(IReadOnlyList<ScriptDocument> library, IReadOnlyList<ScriptDocument> scene,
        IEnumerable<string> citedTypes)
    {
        var added = new List<ScriptDocument>();
        foreach (var type in citedTypes)
        {
            var import = PartLibraryImporter.Plan(library, scene.Concat(added).ToList(), type);
            if (import.Refusal == null) added.AddRange(import.NewFiles);
        }
        return added;
    }

    /// <summary>
    /// As mudanças de uma resposta da IA mais as cópias das peças citadas que ela usa e que nem a cena nem a
    /// própria resposta declaram. As cópias vêm antes, para a cena compilar já na primeira execução.
    /// </summary>
    public static IReadOnlyList<ScriptDocument> CompleteChanges(IReadOnlyList<ScriptDocument> library, IReadOnlyList<ScriptDocument> scene,
        IReadOnlyList<ScriptDocument> changes, IEnumerable<string> citedTypes)
    {
        var overlaid = scene
            .Where(d => !changes.Any(c => string.Equals(c.FilePath, d.FilePath, StringComparison.OrdinalIgnoreCase)))
            .Concat(changes)
            .ToList();
        var used = changes
            .SelectMany(c => CSharpSyntaxTree.ParseText(c.Text, LibraryParse.Options).GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            .Select(n => n.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        var copies = new List<ScriptDocument>();
        foreach (var type in citedTypes.Where(used.Contains))
        {
            var import = PartLibraryImporter.Plan(library, overlaid.Concat(copies).ToList(), type);
            if (import.Refusal == null) copies.AddRange(import.NewFiles);
        }
        return copies.Concat(changes).ToList();
    }
}
