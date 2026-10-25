using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaCadEditor.Application.Services;

/// <summary>
/// Põe um chanfro/arredondamento de todas as arestas ANTES de uma subtração já existente no script, em
/// vez de depois dela: a declaração do fillet entra logo acima da booleana e a <c>baseFeature</c> da
/// booleana passa a ser o fillet. O sólido final continua sendo a booleana, com o mesmo nome.
/// <para>
/// O motivo é custo, não estética: sobre um corpo já furado o fillet cai no caminho genérico por malha
/// (<c>OcctFilletBuilder</c>), que faz uma booleana por aresta da tesselação, e a boca de cada furo
/// redondo tem uma aresta por segmento. Quatro furos bastavam para travar a UI por minutos. No corpo
/// bruto são poucas arestas, e as bocas dos furos ficam vivas.
/// </para>
/// </summary>
public static class RoslynFilletBeforeCutInserter
{
    /// <summary>
    /// Insere <paramref name="filletCode"/> (que declara <paramref name="filletVar"/> sobre
    /// <paramref name="baseVar"/>) antes da declaração de <paramref name="cutVar"/> e troca a base desta
    /// por <paramref name="filletVar"/>. Devolve <c>false</c>, sem alterar nada, se a booleana não estiver
    /// num formato reconhecido ou se a base dela não for exatamente <paramref name="baseVar"/>.
    /// </summary>
    public static bool TryInsert(string script, string cutVar, string baseVar, string filletVar, string filletCode, out string result)
    {
        result = script;
        if (string.IsNullOrWhiteSpace(script) || string.IsNullOrWhiteSpace(cutVar) ||
            string.IsNullOrWhiteSpace(baseVar) || string.IsNullOrWhiteSpace(filletVar) || string.IsNullOrWhiteSpace(filletCode))
            return false;

        try
        {
            var root = CSharpSyntaxTree.ParseText(script).GetRoot();

            var declarators = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToList();
            if (declarators.Any(d => d.Identifier.Text == filletVar)) return false;

            var declarator = declarators.FirstOrDefault(d => d.Identifier.Text == cutVar);
            if (declarator?.Initializer?.Value is not { } value) return false;

            var baseArgument = BaseArgument(value);
            if (baseArgument?.Expression is not IdentifierNameSyntax baseName || baseName.Identifier.Text != baseVar) return false;

            // Só dá para inserir antes de uma instrução que esteja numa sequência de instruções: o corpo
            // de um bloco ou o nível de cima do script.
            if (declarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is not { } statement) return false;
            SyntaxNode anchor = statement.Parent is GlobalStatementSyntax global ? global : statement;
            if (anchor.Parent is not (BlockSyntax or CompilationUnitSyntax)) return false;

            // Antes dos comentários que precedem a booleana: eles falam dela, então continuam colados nela.
            string newLine = script.Contains("\r\n") ? "\r\n" : "\n";
            string indent = Indentation(statement.GetFirstToken());
            var lines = filletCode.Replace("\r\n", "\n").Split('\n');
            string inserted = string.Concat(lines.Select(l => indent + l + newLine));
            // Booleana na mesma linha da instrução anterior ("AddFeature(a); var corte = ..."): o fillet
            // começa numa linha própria.
            if (anchor.FullSpan.Start > 0 && script[anchor.FullSpan.Start - 1] != '\n') inserted = newLine + inserted;

            var text = SourceText.From(script);
            var changed = text.WithChanges(
                new TextChange(new TextSpan(anchor.FullSpan.Start, 0), inserted),
                new TextChange(baseName.Span, filletVar));
            result = changed.ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// O argumento com a base de uma subtração de base única: <c>baseFeature:</c> nomeado, o 3º posicional
    /// do construtor (name, operation, baseFeature, toolFeatures) ou o 2º de <c>CreateDifference</c>.
    /// </summary>
    private static ArgumentSyntax? BaseArgument(ExpressionSyntax value)
    {
        if (value is ObjectCreationExpressionSyntax { Type: IdentifierNameSyntax { Identifier.Text: "BooleanFeature" }, ArgumentList: { } ctorArgs })
        {
            var args = ctorArgs.Arguments;
            var named = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == "baseFeature");
            if (named != null) return named;
            return args.Count >= 4 && args.All(a => a.NameColon == null) ? args[2] : null;
        }

        if (value is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "BooleanFeature" }, Name.Identifier.Text: "CreateDifference" },
                ArgumentList: { } factoryArgs,
            } && factoryArgs.Arguments.Count >= 2 && factoryArgs.Arguments.All(a => a.NameColon == null))
        {
            return factoryArgs.Arguments[1];
        }

        return null;
    }

    /// <summary>Espaços/tabs que antecedem <paramref name="token"/> na linha dele.</summary>
    private static string Indentation(SyntaxToken token)
    {
        var leading = token.LeadingTrivia;
        return leading.Count > 0 && leading[^1].IsKind(SyntaxKind.WhitespaceTrivia) ? leading[^1].ToString() : string.Empty;
    }
}
