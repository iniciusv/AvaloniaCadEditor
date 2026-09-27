using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniaCadEditor.Application.Services;

public class RoslynFeatureHighlighter
{
    private string _lastCode = string.Empty;
    private SyntaxTree? _cachedTree;

    private SyntaxTree GetTree(string code)
    {
        if (_cachedTree == null || _lastCode != code)
        {
            _cachedTree = CSharpSyntaxTree.ParseText(code);
            _lastCode = code;
        }
        return _cachedTree;
    }

    /// <summary>
    /// Encontra os TextSpans discretos (declaracao e invocacao) de uma feature. 
    /// Utiliza cache da SyntaxTree para maior eficiencia.
    /// </summary>
    public IEnumerable<TextSpan> GetFeatureSpans(string code, string featureName)
    {
        var spans = new List<TextSpan>();
        string targetVar = RoslynDependencyAnalyzer.ResolveVariableName(code, featureName);
        if (string.IsNullOrWhiteSpace(targetVar))
            return spans;

        var root = GetTree(code).GetRoot();

        var declarator = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Identifier.Text == targetVar);

        if (declarator != null)
        {
            var statement = declarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
            if (statement != null)
                spans.Add(statement.Span);
            else
                spans.Add(declarator.Span);
        }
        else
        {
            var propAssignment = root.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(a => a.Left is IdentifierNameSyntax id && id.Identifier.Text == "Name" &&
                                     a.Right is LiteralExpressionSyntax lit && lit.Token.ValueText == featureName);
                                     
            if (propAssignment != null)
            {
                var statement = propAssignment.FirstAncestorOrSelf<StatementSyntax>();
                if (statement != null)
                    spans.Add(statement.Span);
                else
                    spans.Add(propAssignment.Span);
            }
        }

        // Add AddFeature call as a separate span
        var addFeatureCall = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv => inv.Expression is IdentifierNameSyntax id && id.Identifier.Text == "AddFeature" &&
                                   inv.ArgumentList.Arguments.Count == 1 &&
                                   inv.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax argId &&
                                   argId.Identifier.Text == targetVar);

        if (addFeatureCall != null)
        {
            var stmt = addFeatureCall.FirstAncestorOrSelf<GlobalStatementSyntax>() 
                       ?? (SyntaxNode?)addFeatureCall.FirstAncestorOrSelf<ExpressionStatementSyntax>();
            
            if (stmt != null)
            {
                spans.Add(stmt.Span);
            }
        }

        return spans;
    }

    public List<TextSpan> FindFeatureSpans(string code, string featureName)
    {
        var spans = new List<TextSpan>();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(featureName))
            return spans;

        string targetVar = RoslynDependencyAnalyzer.ResolveVariableName(code, featureName);
        if (string.IsNullOrWhiteSpace(targetVar))
            return spans;

        var analyzer = new RoslynDependencyAnalyzer();
        var dagNodes = analyzer.GetFullDependencyGraph(code, targetVar);

        foreach (var node in dagNodes)
        {
            spans.AddRange(GetFeatureSpans(code, node));
        }

        // Always include the target node even if it's not a standard variable declaration (e.g. fallback)
        if (!dagNodes.Contains(targetVar))
        {
            spans.AddRange(GetFeatureSpans(code, targetVar));
        }

        return spans.Distinct().ToList();
    }
}
