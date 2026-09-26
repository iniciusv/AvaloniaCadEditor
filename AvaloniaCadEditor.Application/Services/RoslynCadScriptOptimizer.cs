using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Services;

public class RoslynCadScriptOptimizer : CSharpSyntaxRewriter
{
    private readonly Dictionary<string, string> _deduplicatedExpressions = new();
    private readonly HashSet<string> _declaredVariables = new();

    public static string OptimizeAndFormat(string csharpScript)
    {
        if (string.IsNullOrWhiteSpace(csharpScript))
            return string.Empty;

        try
        {
            var tree = CSharpSyntaxTree.ParseText(csharpScript);
            var root = tree.GetRoot();

            var optimizer = new RoslynCadScriptOptimizer();
            var rewrittenRoot = optimizer.Visit(root);

            return rewrittenRoot.NormalizeWhitespace().ToFullString();
        }
        catch
        {
            // If parsing fails for any reason, fallback to original script
            return csharpScript;
        }
    }

    public override SyntaxNode? VisitGlobalStatement(GlobalStatementSyntax node)
    {
        var statement = (StatementSyntax?)Visit(node.Statement);
        if (statement == null)
            return null;
        return node.WithStatement(statement);
    }

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var variable = node.Declaration.Variables.FirstOrDefault();
        if (variable?.Initializer?.Value != null)
        {
            var initExprSyntax = variable.Initializer.Value;
            var varName = variable.Identifier.Text;

            bool isFindFace = false;
            bool isLinkedWorkPlane = false;

            if (initExprSyntax is InvocationExpressionSyntax inv &&
                inv.Expression is MemberAccessExpressionSyntax memAccess &&
                memAccess.Name.Identifier.Text == "FindFace")
            {
                isFindFace = true;
            }
            else if (initExprSyntax is ObjectCreationExpressionSyntax objCreation &&
                     objCreation.Type is IdentifierNameSyntax typeId &&
                     typeId.Identifier.Text == "LinkedWorkPlane")
            {
                isLinkedWorkPlane = true;
            }

            // Check for duplicate FindFace or LinkedWorkPlane instantiations
            if (isFindFace || isLinkedWorkPlane)
            {
                var initExprStr = initExprSyntax.NormalizeWhitespace().ToFullString();
                if (_deduplicatedExpressions.TryGetValue(initExprStr, out var existingVarName))
                {
                    // Map this duplicate variable name to the first declared one
                    _deduplicatedExpressions[varName] = existingVarName;
                    // Remove this duplicate declaration node
                    return null;
                }
                else
                {
                    _deduplicatedExpressions[initExprStr] = varName;
                    _declaredVariables.Add(varName);
                }
            }
            else
            {
                _declaredVariables.Add(varName);
            }
        }

        return base.VisitLocalDeclarationStatement(node);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var text = node.Identifier.Text;
        if (_deduplicatedExpressions.TryGetValue(text, out var replacement))
        {
            return SyntaxFactory.IdentifierName(replacement);
        }
        return base.VisitIdentifierName(node);
    }
}
