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

    private static string FormatNumber(double value)
    {
        // Avoid .00000000001 precision issues, standardizing to a nice readable text
        return Math.Abs(value % 1) <= (Double.Epsilon * 100) ?
            Math.Round(value).ToString(CultureInfo.InvariantCulture) :
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private ArgumentSyntax? TryMutateArgument(ArgumentSyntax? arg, double delta)
    {
        if (arg == null) return null;
        if (Math.Abs(delta) < 1e-12) return arg;

        if (arg.Expression is LiteralExpressionSyntax literal &&
            (literal.IsKind(SyntaxKind.NumericLiteralExpression)))
        {
            double value = 0;
            if (literal.Token.Value is double d) value = d;
            else if (literal.Token.Value is int i) value = i;
            else if (double.TryParse(literal.Token.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) value = parsed;

            double newValue = value + delta;
            string newText = FormatNumber(newValue);

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
            string newText = FormatNumber(Math.Abs(newValue));

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
        else if (arg.Expression is BinaryExpressionSyntax binary &&
                 (binary.IsKind(SyntaxKind.AddExpression) || binary.IsKind(SyntaxKind.SubtractExpression)) &&
                 binary.Right is LiteralExpressionSyntax rightLiteral &&
                 rightLiteral.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            // "expr + literal" or "expr - literal" (e.g. base_z + 5): sum the delta into the literal
            // instead of chaining another "+ delta", so repeated moves don't pile up terms.
            double value = 0;
            if (rightLiteral.Token.Value is double d) value = d;
            else if (rightLiteral.Token.Value is int i) value = i;
            else if (double.TryParse(rightLiteral.Token.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) value = parsed;

            double signedValue = binary.IsKind(SyntaxKind.SubtractExpression) ? -value : value;
            double newSignedValue = signedValue + delta;

            if (Math.Abs(newSignedValue) < 1e-9)
            {
                // The literal term cancels out entirely: just "expr".
                return arg.WithExpression(binary.Left.WithTriviaFrom(binary));
            }

            var op = newSignedValue < 0 ? SyntaxKind.SubtractExpression : SyntaxKind.AddExpression;
            var opToken = newSignedValue < 0 ? SyntaxFactory.Token(SyntaxKind.MinusToken) : SyntaxFactory.Token(SyntaxKind.PlusToken);
            string newText = FormatNumber(Math.Abs(newSignedValue));

            var newRight = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(newText, Math.Abs(newSignedValue)));

            var newBinary = SyntaxFactory.BinaryExpression(op, binary.Left, opToken, newRight)
                .WithTriviaFrom(binary);
            return arg.WithExpression(newBinary);
        }
        else
        {
            // The argument is an arbitrary expression (a variable, a method call, ...): we can't fold
            // the delta into a literal, so wrap it as "expr + delta" (or "expr - delta") instead of
            // silently dropping the offset.
            bool needsParens = arg.Expression is BinaryExpressionSyntax or ConditionalExpressionSyntax
                or AssignmentExpressionSyntax or LambdaExpressionSyntax or CastExpressionSyntax
                or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax;

            ExpressionSyntax baseExpr = needsParens
                ? SyntaxFactory.ParenthesizedExpression(arg.Expression.WithoutTrivia())
                : arg.Expression.WithoutTrivia();

            var op = delta < 0 ? SyntaxKind.SubtractExpression : SyntaxKind.AddExpression;
            var opToken = delta < 0 ? SyntaxFactory.Token(SyntaxKind.MinusToken) : SyntaxFactory.Token(SyntaxKind.PlusToken);
            string newText = FormatNumber(Math.Abs(delta));

            var deltaLiteral = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(newText, Math.Abs(delta)));

            var newBinary = SyntaxFactory.BinaryExpression(op, baseExpr, opToken, deltaLiteral)
                .WithTriviaFrom(arg.Expression);
            return arg.WithExpression(newBinary);
        }
    }
}
