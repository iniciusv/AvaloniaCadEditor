using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Ai.Context;

/// <summary>
/// Resumo de uma peça para ir ao lado da assinatura dela no pedido à IA: a primeira frase do
/// <c>/// &lt;summary&gt;</c> e tudo o que o código diz sobre a ORIGEM local da peça (no summary ou em
/// comentários do corpo). Sem isso, a IA só vê <c>CarrinhoMgn15H(double Largura = 32, …)</c> e não tem
/// como saber que a origem fica na base do trilho — e erra a montagem, mesmo com ferramentas.
/// </summary>
public static class PartDocSummary
{
    public const int MaxLength = 450;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest, kind: SourceCodeKind.Regular);

    // Fim de frase: ponto/ponto e vírgula seguido de espaço e maiúscula (decimais usam vírgula: "58,8 mm").
    private static readonly Regex SentenceBreak = new(@"(?<=[.;])\s+(?=[A-ZÀ-Ý(])", RegexOptions.CultureInvariant);
    private static readonly Regex Origin = new(@"\borigem\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>O resumo da peça <paramref name="partTypeName"/> declarada em <paramref name="source"/>, ou null sem comentários.</summary>
    public static string? Extract(string source, string partTypeName)
    {
        var root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();
        var type = root.DescendantNodes().OfType<RecordDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.ValueText == partTypeName);
        if (type == null) return null;

        var sentences = SentenceBreak.Split(DocText(type))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var picked = new List<string>();
        if (sentences.Count > 0) picked.Add(sentences[0]);
        picked.AddRange(sentences.Skip(1).Where(s => Origin.IsMatch(s)));
        picked.AddRange(BodyComments(type).Where(c => Origin.IsMatch(c)).Where(c => !picked.Contains(c)));

        if (picked.Count == 0) return null;
        return Truncate(string.Join(" ", picked.Select(EndWithPeriod)));
    }

    /// <summary>O texto do <c>&lt;summary&gt;</c>, sem as barras e com <c>&lt;see cref="X"/&gt;</c> → X.</summary>
    private static string DocText(RecordDeclarationSyntax type)
    {
        var doc = type.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();
        if (doc == null) return string.Empty;

        var summary = doc.Content.OfType<XmlElementSyntax>()
            .FirstOrDefault(e => e.StartTag.Name.LocalName.ValueText == "summary");
        string text = summary?.Content.ToFullString() ?? string.Empty;

        text = Regex.Replace(text, @"^\s*///", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"<see\s+cref=""([^""]+)""\s*/>", "$1");
        text = Regex.Replace(text, @"<[^>]+>", "");
        return Collapse(System.Net.WebUtility.HtmlDecode(text));
    }

    /// <summary>Comentários <c>//</c> do corpo da peça, um por bloco de linhas seguidas.</summary>
    private static IEnumerable<string> BodyComments(RecordDeclarationSyntax type)
    {
        var block = new List<string>();
        int lastLine = -2;
        foreach (var trivia in type.DescendantTrivia().Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia)))
        {
            int line = trivia.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (line != lastLine + 1 && block.Count > 0)
            {
                yield return Collapse(string.Join(" ", block));
                block.Clear();
            }
            block.Add(trivia.ToString().TrimStart('/').Trim());
            lastLine = line;
        }
        if (block.Count > 0) yield return Collapse(string.Join(" ", block));
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string EndWithPeriod(string s) => s.EndsWith('.') || s.EndsWith(';') || s.EndsWith(':') ? s : s + ".";

    private static string Truncate(string text)
    {
        if (text.Length <= MaxLength) return text;
        int cut = text.LastIndexOf(' ', MaxLength - 1);
        return text[..(cut > 0 ? cut : MaxLength)] + " …";
    }
}
