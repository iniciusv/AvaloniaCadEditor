using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Scenes;

/// <summary>
/// Uma cena em disco: uma pasta com um .csproj SDK-style e arquivos .cs. O .csproj é lido
/// por convenção (sem MSBuild): os fontes são todos os *.cs da pasta, exceto bin/, obj/ e .cad/,
/// exatamente como o globbing padrão do SDK faz.
/// </summary>
public sealed class SceneProject
{
    /// <summary>Arquivo de entrada padrão (top-level statements).</summary>
    public const string DefaultEntryFile = "Cena.cs";

    /// <summary>Pasta de arquivos gerados pelo app (referência de API, estado para IAs).</summary>
    public const string GeneratedFolder = ".cad";

    private static readonly HashSet<string> ExcludedFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", GeneratedFolder, ".git", ".vs" };

    private SceneProject(string rootPath, string projectFile)
    {
        RootPath = rootPath;
        ProjectFile = projectFile;
    }

    /// <summary>Caminho absoluto da pasta da cena.</summary>
    public string RootPath { get; }

    /// <summary>Caminho absoluto do .csproj.</summary>
    public string ProjectFile { get; }

    public string Name => Path.GetFileNameWithoutExtension(ProjectFile);

    /// <summary>True se a pasta contém exatamente um .csproj na raiz.</summary>
    public static bool IsSceneFolder(string folder) =>
        Directory.Exists(folder) && Directory.GetFiles(folder, "*.csproj").Length == 1;

    public static SceneProject Load(string folder)
    {
        string root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Pasta da cena não encontrada: {root}");
        }

        var projects = Directory.GetFiles(root, "*.csproj");
        if (projects.Length != 1)
        {
            throw new InvalidOperationException(projects.Length == 0
                ? $"A pasta não contém um .csproj de cena: {root}"
                : $"A pasta contém mais de um .csproj; deixe apenas o da cena: {root}");
        }

        return new SceneProject(root, projects[0]);
    }

    /// <summary>Arquivos .cs da cena, relativos à raiz, com '/' como separador, em ordem estável.</summary>
    public IReadOnlyList<string> GetSourceFiles()
    {
        return Directory.EnumerateFiles(RootPath, "*.cs", SearchOption.AllDirectories)
            .Select(ToRelativePath)
            .Where(rel => !rel.Split('/').SkipLast(1).Any(ExcludedFolders.Contains))
            .OrderBy(rel => rel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Lê todos os fontes do disco.</summary>
    public IReadOnlyList<ScriptDocument> ReadDocuments() =>
        GetSourceFiles().Select(rel => new ScriptDocument(rel, File.ReadAllText(ToFullPath(rel)))).ToList();

    /// <summary>
    /// Arquivo com top-level statements (o ponto de entrada da cena). Sem nenhum, usa
    /// <see cref="DefaultEntryFile"/>.
    /// </summary>
    public static string FindEntryFile(IEnumerable<ScriptDocument> documents)
    {
        foreach (var doc in documents)
        {
            var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(doc.Text).GetRoot();
            if (root.Members.OfType<GlobalStatementSyntax>().Any())
            {
                return doc.FilePath;
            }
        }
        return DefaultEntryFile;
    }

    /// <summary>
    /// Nomes dos projetos referenciados via &lt;ProjectReference&gt;. Ainda não são carregados
    /// (Fase 4); o app só avisa que existem.
    /// </summary>
    public IReadOnlyList<string> GetProjectReferences()
    {
        try
        {
            return XDocument.Load(ProjectFile)
                .Descendants()
                .Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference")
                .Select(e => (string?)e.Attribute("Include") ?? "?")
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public string ToFullPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Converte um caminho (absoluto ou relativo à raiz) para o formato relativo com '/'.</summary>
    public string ToRelativePath(string path)
    {
        string full = Path.IsPathRooted(path) ? path : Path.Combine(RootPath, path);
        return Path.GetRelativePath(RootPath, full).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>True se o caminho está dentro de uma pasta ignorada (bin/, obj/, .cad/...).</summary>
    public bool IsIgnoredPath(string path)
    {
        string rel = ToRelativePath(path);
        return rel.StartsWith("..", StringComparison.Ordinal)
            || rel.Split('/').SkipLast(1).Any(ExcludedFolders.Contains);
    }
}
