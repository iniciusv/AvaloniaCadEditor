using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Library;

/// <summary>
/// Lê as peças de um conjunto de arquivos (a pasta da biblioteca ou os fontes da cena) e devolve o
/// catálogo. O catálogo é o próprio código, sem formato novo: a categoria é a subpasta, a descrição é o
/// <c>&lt;summary&gt;</c>, os parâmetros vêm do construtor do record (<see cref="PartSourceEditor.FindPartTypes"/>)
/// e os modelos prontos são as propriedades estáticas que devolvem o próprio tipo. Puro e sintático.
/// </summary>
public static class PartLibraryCatalog
{
    private const int ShortSummaryLength = 140;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest, kind: SourceCodeKind.Regular);

    /// <param name="documents">Arquivos com caminho relativo à pasta da origem, com '/' como separador.</param>
    public static IReadOnlyList<PartLibraryItem> Read(IReadOnlyList<ScriptDocument> documents, PartLibrarySource source)
    {
        var roots = documents
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => CSharpSyntaxTree.ParseText(g.First().Text, ParseOptions).GetRoot(), StringComparer.OrdinalIgnoreCase);

        var items = new List<PartLibraryItem>();
        foreach (var type in PartSourceEditor.FindPartTypes(documents))
        {
            var declaration = roots[type.FilePath].DescendantNodes().OfType<RecordDeclarationSyntax>()
                .First(r => r.Identifier.ValueText == type.Name
                    && r.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == type.Line);

            items.Add(new PartLibraryItem(
                type,
                source,
                CategoryOf(type.FilePath),
                SummaryOf(declaration),
                PresetsOf(declaration, type),
                IsCutter(declaration)));
        }

        return items
            .OrderBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>A subpasta do arquivo (<c>Perfis/Extrusao2020.cs</c> → <c>Perfis</c>); vazia na raiz.</summary>
    public static string CategoryOf(string relativePath)
    {
        int slash = relativePath.Replace('\\', '/').LastIndexOf('/');
        return slash < 0 ? string.Empty : relativePath[..slash].Replace('\\', '/');
    }

    /// <summary>A primeira frase, cortada em <see cref="ShortSummaryLength"/> caracteres.</summary>
    public static string FirstSentence(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var match = Regex.Match(text, @"^.*?[.!?](?=\s|$)");
        string sentence = match.Success ? match.Value : text;
        return sentence.Length <= ShortSummaryLength ? sentence : sentence[..(ShortSummaryLength - 1)].TrimEnd() + "…";
    }

    /// <summary>O texto do <c>&lt;summary&gt;</c> de uma declaração, sem tags e em uma linha.</summary>
    public static string SummaryOf(SyntaxNode declaration)
    {
        var comment = declaration.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .LastOrDefault();
        if (comment == null) return string.Empty;

        string xml = Regex.Replace(comment.ToFullString(), @"^\s*///", string.Empty, RegexOptions.Multiline);
        var summary = Regex.Match(xml, @"<summary>(.*?)</summary>", RegexOptions.Singleline);
        if (!summary.Success) return string.Empty;

        string text = Regex.Replace(summary.Groups[1].Value, @"<(see|seealso|paramref|typeparamref)\s+(cref|name|langword)=""([^""]*)""\s*/>", m => m.Groups[3].Value);
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// O record se declara vazio (<c>public override bool IsCutter =&gt; true;</c>, ou com <c>get { return true; }</c>).
    /// </summary>
    public static bool IsCutter(RecordDeclarationSyntax record)
    {
        var property = record.Members.OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(p => p.Identifier.ValueText == "IsCutter" && p.Modifiers.Any(SyntaxKind.OverrideKeyword));
        if (property == null) return false;

        var value = property.ExpressionBody?.Expression
            ?? property.AccessorList?.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) switch
            {
                { ExpressionBody: { } body } => body.Expression,
                { Body.Statements: [ReturnStatementSyntax { Expression: { } returned }] } => returned,
                _ => null
            };
        return value is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.TrueLiteralExpression);
    }

    private static IReadOnlyList<PartLibraryPreset> PresetsOf(RecordDeclarationSyntax record, PartTypeInfo type)
    {
        string typeName = record.Identifier.ValueText;
        var presets = new List<PartLibraryPreset>();
        foreach (var property in record.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!property.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;
            if (!property.Modifiers.Any(SyntaxKind.PublicKeyword)) continue;
            if (property.Type is not IdentifierNameSyntax returned || returned.Identifier.ValueText != typeName) continue;

            var value = property.ExpressionBody?.Expression ?? property.Initializer?.Value;
            presets.Add(new PartLibraryPreset(property.Identifier.ValueText, SummaryOf(property), ArgumentsOf(value, typeName, type)));
        }
        return presets;
    }

    /// <summary>
    /// Os argumentos de um <c>new(...)</c> ou <c>new Tipo(...)</c>, por nome de parâmetro. Vazio quando o
    /// valor é outra coisa (uma chamada, um <c>with</c>): o modelo continua inserível pelo nome.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ArgumentsOf(ExpressionSyntax? value, string typeName, PartTypeInfo type)
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        ArgumentListSyntax? list = value switch
        {
            ImplicitObjectCreationExpressionSyntax implicitNew => implicitNew.ArgumentList,
            ObjectCreationExpressionSyntax { Type: IdentifierNameSyntax t } explicitNew when t.Identifier.ValueText == typeName => explicitNew.ArgumentList,
            _ => null
        };
        if (list == null) return empty;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        int position = 0;
        foreach (var argument in list.Arguments)
        {
            string? name = argument.NameColon?.Name.Identifier.ValueText;
            if (name == null)
            {
                if (position >= type.Parameters.Count) return empty;
                name = type.Parameters[position++].Name;
            }
            if (type.Parameters.All(p => p.Name != name)) return empty;
            result[name] = argument.Expression.ToString();
        }
        return result;
    }
}
