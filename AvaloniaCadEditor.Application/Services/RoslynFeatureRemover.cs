using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Services;

public class RoslynFeatureRemover : CSharpSyntaxRewriter
{
    private readonly HashSet<string> _variablesToRemove;
    public bool FoundNewVariablesToRemove { get; private set; }

    private RoslynFeatureRemover(HashSet<string> variablesToRemove)
    {
        _variablesToRemove = variablesToRemove;
    }

    public static string RemoveFeatureCode(string csharpScript, string featureName)
    {
        if (string.IsNullOrWhiteSpace(csharpScript) || string.IsNullOrWhiteSpace(featureName))
            return csharpScript;

        try
        {
            var targetVar = RoslynDependencyAnalyzer.ResolveVariableName(csharpScript, featureName);
            if (string.IsNullOrWhiteSpace(targetVar))
                return csharpScript;

            var tree = CSharpSyntaxTree.ParseText(csharpScript);
            var root = tree.GetRoot();

            var variablesToRemove = new HashSet<string> { targetVar };
            
            bool changed;
            do
            {
                var remover = new RoslynFeatureRemover(variablesToRemove);
                root = remover.Visit(root);
                changed = remover.FoundNewVariablesToRemove;
            } while (changed);

            return root.NormalizeWhitespace().ToFullString();
        }
        catch
        {
            return csharpScript;
        }
    }

    public override SyntaxNode? VisitGlobalStatement(GlobalStatementSyntax node)
    {
        // First check if this statement declares a target variable
        if (node.Statement is LocalDeclarationStatementSyntax localDecl)
        {
            var varName = localDecl.Declaration.Variables.FirstOrDefault()?.Identifier.Text;
            if (varName != null)
            {
                if (_variablesToRemove.Contains(varName) || _variablesToRemove.Any(v => varName.StartsWith(v + "_")))
                {
                    return null;
                }

                // Does the initializer reference any of the variables to remove?
                bool referencesTarget = localDecl.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Any(id => _variablesToRemove.Contains(id.Identifier.Text) || _variablesToRemove.Any(v => id.Identifier.Text.StartsWith(v + "_")));

                if (referencesTarget)
                {
                    _variablesToRemove.Add(varName);
                    FoundNewVariablesToRemove = true;
                    return null;
                }
            }
        }

        // Then check if it's an expression statement (like AddFeature or AlignToFace) referencing the target variable
        if (node.Statement is ExpressionStatementSyntax exprStmt)
        {
            bool referencesTarget = exprStmt.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(id => _variablesToRemove.Contains(id.Identifier.Text) || _variablesToRemove.Any(v => id.Identifier.Text.StartsWith(v + "_")));

            if (referencesTarget)
            {
                return null;
            }
        }

        return base.VisitGlobalStatement(node);
    }
}
