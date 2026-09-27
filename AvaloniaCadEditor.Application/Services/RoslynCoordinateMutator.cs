using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class RoslynCoordinateMutator : CSharpSyntaxRewriter
{
    private readonly HashSet<string> _rootVariablesToMutate;
    private readonly Vector3D _translation;

    private bool _inTargetVariable = false;

    private RoslynCoordinateMutator(IEnumerable<string> rootVariables, Vector3D translation)
    {
        _rootVariablesToMutate = new HashSet<string>(rootVariables);
        _translation = translation;
    }

    public static string MutateCoordinates(string csharpScript, IEnumerable<string> rootVariables, Vector3D translation)
    {
        if (string.IsNullOrWhiteSpace(csharpScript) || !rootVariables.Any())
            return csharpScript;

        var tree = CSharpSyntaxTree.ParseText(csharpScript);
        var root = tree.GetRoot();

        var mutator = new RoslynCoordinateMutator(rootVariables, translation);
        var rewrittenRoot = mutator.Visit(root);

        // Keep formatting as original as possible, or normalize if needed.
        return rewrittenRoot.ToFullString();
    }

    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        var isTarget = _rootVariablesToMutate.Contains(node.Identifier.Text);
        if (isTarget)
        {
            _inTargetVariable = true;
            var result = base.VisitVariableDeclarator(node);
            _inTargetVariable = false;
            return result;
        }

        return base.VisitVariableDeclarator(node);
    }

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        if (_inTargetVariable && node.Type is IdentifierNameSyntax typeName && typeName.Identifier.Text == "Point3D")
        {
            if (node.ArgumentList != null && node.ArgumentList.Arguments.Count >= 2)
            {
                var args = node.ArgumentList.Arguments;
                
                var xArg = args.Count > 0 ? args[0] : null;
                var yArg = args.Count > 1 ? args[1] : null;
                var zArg = args.Count > 2 ? args[2] : null;

                var newX = TryMutateArgument(xArg, _translation.X);
                var newY = TryMutateArgument(yArg, _translation.Y);
                var newZ = TryMutateArgument(zArg, _translation.Z);

                var newArgs = new List<ArgumentSyntax>();
                if (newX != null) newArgs.Add(newX);
                if (newY != null) newArgs.Add(newY);
                if (newZ != null) newArgs.Add(newZ);

                var newArgumentList = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(newArgs))
                    .WithTriviaFrom(node.ArgumentList);

                return node.WithArgumentList(newArgumentList);
            }
        }

        return base.VisitObjectCreationExpression(node);
    }

    private ArgumentSyntax? TryMutateArgument(ArgumentSyntax? arg, double delta)
    {
        if (arg == null) return null;

        if (arg.Expression is LiteralExpressionSyntax literal && 
            (literal.IsKind(SyntaxKind.NumericLiteralExpression)))
        {
            double value = 0;
            if (literal.Token.Value is double d) value = d;
            else if (literal.Token.Value is int i) value = i;
            else if (double.TryParse(literal.Token.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) value = parsed;

            double newValue = value + delta;
            
            // Format to avoid .00000000001 precision issues, standardizing to a nice readable text
            string newText = Math.Abs(newValue % 1) <= (Double.Epsilon * 100) ? 
                Math.Round(newValue).ToString(CultureInfo.InvariantCulture) : 
                newValue.ToString("0.###", CultureInfo.InvariantCulture);

            var newLiteral = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(newText, newValue)
            ).WithTriviaFrom(literal);

            return arg.WithExpression(newLiteral);
        }
        else if (arg.Expression is PrefixUnaryExpressionSyntax prefix && 
                 prefix.IsKind(SyntaxKind.UnaryMinusExpression) && 
                 prefix.Operand is LiteralExpressionSyntax negLiteral)
        {
            // Handle negative numbers e.g. -10
            double value = 0;
            if (negLiteral.Token.Value is double d) value = -d;
            else if (negLiteral.Token.Value is int i) value = -i;
            else if (double.TryParse(negLiteral.Token.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) value = -parsed;

            double newValue = value + delta;

            string newText = Math.Abs(newValue % 1) <= (Double.Epsilon * 100) ? 
                Math.Round(Math.Abs(newValue)).ToString(CultureInfo.InvariantCulture) : 
                Math.Abs(newValue).ToString("0.###", CultureInfo.InvariantCulture);

            ExpressionSyntax newExpr;
            if (newValue < 0)
            {
                newExpr = SyntaxFactory.PrefixUnaryExpression(
                    SyntaxKind.UnaryMinusExpression,
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(newText, Math.Abs(newValue))
                    )
                );
            }
            else
            {
                newExpr = SyntaxFactory.LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal(newText, Math.Abs(newValue))
                );
            }

            newExpr = newExpr.WithTriviaFrom(prefix);
            return arg.WithExpression(newExpr);
        }

        // Fallback (if the argument is a variable itself, we can't easily mutate it here, return as is)
        return arg;
    }
}
