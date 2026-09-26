using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Linq;

namespace AvaloniaCadEditor.Application.Services;

public class RoslynFeatureHighlighter
{
    private string _lastCode = string.Empty;
    private SyntaxTree? _cachedTree;

    /// <summary>
    /// Encontra o TextSpan da declaracao de uma feature. 
    /// Utiliza cache da SyntaxTree para maior eficiencia.
    /// </summary>
    public TextSpan? FindFeatureSpan(string code, string featureName)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(featureName))
            return null;

        if (_cachedTree == null || _lastCode != code)
        {
            _cachedTree = CSharpSyntaxTree.ParseText(code);
            _lastCode = code;
        }

        var root = _cachedTree.GetRoot();

        var declarator = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Identifier.Text == featureName);

        if (declarator != null)
        {
            var statement = declarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
            var span = statement?.Span ?? declarator.Span;
            return ExpandIfAddFeature(root, span, featureName);
        }

        var propAssignment = root.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(a => a.Left is IdentifierNameSyntax id && id.Identifier.Text == "Name" &&
                                 a.Right is LiteralExpressionSyntax lit && lit.Token.ValueText == featureName);
                                 
        if (propAssignment != null)
        {
            var statement = propAssignment.FirstAncestorOrSelf<StatementSyntax>();
            var span = statement?.Span ?? propAssignment.Span;
            return ExpandIfAddFeature(root, span, featureName);
        }

        return null;
    }

    private TextSpan ExpandIfAddFeature(SyntaxNode root, TextSpan currentSpan, string featureName)
    {
        var addFeatureCall = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv => inv.Expression is IdentifierNameSyntax id && id.Identifier.Text == "AddFeature" &&
                                   inv.ArgumentList.Arguments.Count == 1 &&
                                   inv.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax argId &&
                                   argId.Identifier.Text == featureName);

        if (addFeatureCall != null)
        {
            var stmt = addFeatureCall.FirstAncestorOrSelf<GlobalStatementSyntax>() 
                       ?? (SyntaxNode?)addFeatureCall.FirstAncestorOrSelf<ExpressionStatementSyntax>();
            
            if (stmt != null)
            {
                int start = System.Math.Min(currentSpan.Start, stmt.SpanStart);
                int end = System.Math.Max(currentSpan.End, stmt.Span.End);
                return TextSpan.FromBounds(start, end);
            }
        }

        return currentSpan;
    }
}
