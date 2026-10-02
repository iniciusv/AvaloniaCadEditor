using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Ai.Context;

/// <summary>
/// Árvores sintáticas dos arquivos da cena, analisadas uma única vez por montagem de contexto:
/// quais tipos cada arquivo declara, quais peças existem e quais nomes cada arquivo usa.
/// Só sintaxe — não compila nada.
/// </summary>
public sealed class SceneSyntaxIndex
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest, kind: SourceCodeKind.Regular);

    private readonly Dictionary<string, ScriptDocument> _documents;
    private readonly Dictionary<string, CompilationUnitSyntax> _roots;
    private readonly Dictionary<string, string> _typeFiles = new(StringComparer.Ordinal);

    public SceneSyntaxIndex(IReadOnlyList<ScriptDocument> documents)
    {
        Documents = documents;
        _documents = documents.ToDictionary(d => d.FilePath, StringComparer.OrdinalIgnoreCase);
        _roots = documents.ToDictionary(
            d => d.FilePath,
            d => (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(d.Text, ParseOptions).GetRoot(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (path, root) in _roots)
        {
            foreach (var type in root.Members.OfType<BaseTypeDeclarationSyntax>())
            {
                _typeFiles.TryAdd(type.Identifier.ValueText, path);
            }
        }

        PartTypes = PartSourceEditor.FindPartTypes(documents);
    }

    public IReadOnlyList<ScriptDocument> Documents { get; }

    /// <summary>Peças (records que herdam de Part) declaradas na cena.</summary>
    public IReadOnlyList<PartTypeInfo> PartTypes { get; }

    /// <summary>O documento com esse caminho (sem diferenciar maiúsculas), ou null.</summary>
    public ScriptDocument? Find(string? filePath) =>
        filePath != null && _documents.TryGetValue(filePath, out var doc) ? doc : null;

    /// <summary>Arquivo que declara o tipo de nível superior <paramref name="typeName"/>, ou null.</summary>
    public string? FileDeclaring(string typeName) => _typeFiles.TryGetValue(typeName, out var file) ? file : null;

    /// <summary>Arquivo que declara a peça <paramref name="partTypeName"/>, ou null.</summary>
    public string? FileDeclaringPart(string partTypeName) =>
        PartTypes.FirstOrDefault(t => t.Name == partTypeName)?.FilePath;

    /// <summary>True se o arquivo declara ao menos uma peça.</summary>
    public bool DeclaresPart(string filePath) =>
        PartTypes.Any(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Nomes simples (identificadores) usados no arquivo, sem repetição.</summary>
    public IEnumerable<string> NamesUsedIn(string filePath) =>
        _roots.TryGetValue(filePath, out var root)
            ? root.DescendantNodes().OfType<SimpleNameSyntax>().Select(n => n.Identifier.ValueText).Distinct(StringComparer.Ordinal)
            : Enumerable.Empty<string>();
}
