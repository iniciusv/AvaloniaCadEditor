using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Services;

/// <summary>
/// Acrescenta uma ferramenta à lista <c>toolFeatures</c> de uma <c>BooleanFeature</c> já existente no
/// script, em vez de encadear uma nova operação em cima do resultado. Usado quando o usuário repete uma
/// operação booleana (ex.: Subtract) tendo como base o resultado de uma booleana anterior do mesmo tipo:
/// o corte final vira "base menos N ferramentas" numa feature só, em vez de N cortes aninhados.
/// </summary>
public static class RoslynBooleanOperandAppender
{
    /// <summary>Acrescenta várias ferramentas de uma vez (ex.: seleção múltipla com Ctrl). Falha em
    /// qualquer uma delas devolve <c>false</c> e não altera o script.</summary>
    public static bool TryAppendTools(string script, string booleanVar, IEnumerable<string> toolVars, out string result)
    {
        result = script;
        var vars = toolVars?.ToList() ?? new List<string>();
        if (vars.Count == 0) return false;

        string current = script;
        foreach (var toolVar in vars)
        {
            if (!TryAppendTool(current, booleanVar, toolVar, out current))
            {
                return false;
            }
        }

        result = current;
        return true;
    }

    public static bool TryAppendTool(string script, string booleanVar, string toolVar, out string result)
    {
        result = script;
        if (string.IsNullOrWhiteSpace(script) || string.IsNullOrWhiteSpace(booleanVar) || string.IsNullOrWhiteSpace(toolVar))
            return false;

        try
        {
            var tree = CSharpSyntaxTree.ParseText(script);
            var root = tree.GetRoot();

            var declarator = root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault(d => d.Identifier.Text == booleanVar);

            if (declarator?.Initializer?.Value == null) return false;

            ExpressionSyntax newValue;

            if (declarator.Initializer.Value is ObjectCreationExpressionSyntax objCreation &&
                objCreation.Type is IdentifierNameSyntax typeName && typeName.Identifier.Text == "BooleanFeature")
            {
                if (!TryAppendToToolFeaturesArgument(objCreation.ArgumentList, toolVar, out var newArgList))
                    return false;

                newValue = objCreation.WithArgumentList(newArgList);
            }
            else if (declarator.Initializer.Value is InvocationExpressionSyntax invocation &&
                     invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                     memberAccess.Expression is IdentifierNameSyntax targetType && targetType.Identifier.Text == "BooleanFeature" &&
                     memberAccess.Name.Identifier.Text is "CreateUnion" or "CreateDifference" or "CreateIntersection")
            {
                // CreateUnion/CreateDifference/CreateIntersection(name, base(s), params tools): as
                // ferramentas são sempre os argumentos finais, então basta acrescentar mais um.
                if (invocation.ArgumentList == null || invocation.ArgumentList.Arguments.Count < 2)
                    return false;

                var newArgList = invocation.ArgumentList.WithArguments(
                    AppendWithComma(invocation.ArgumentList.Arguments, SyntaxFactory.Argument(SyntaxFactory.IdentifierName(toolVar))));
                newValue = invocation.WithArgumentList(newArgList);
            }
            else
            {
                return false;
            }

            var newDeclarator = declarator.WithInitializer(declarator.Initializer.WithValue(newValue));
            var newRoot = root.ReplaceNode(declarator, newDeclarator);
            result = newRoot.ToFullString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAppendToToolFeaturesArgument(ArgumentListSyntax? argumentList, string toolVar, out ArgumentListSyntax newArgumentList)
    {
        newArgumentList = argumentList ?? SyntaxFactory.ArgumentList();
        if (argumentList == null) return false;

        var args = argumentList.Arguments;

        // toolFeatures é sempre o último parâmetro do construtor: nomeado ("toolFeatures:") ou,
        // sem nomes, o 4º argumento posicional (name, operation, baseFeature(s), toolFeatures).
        int index = -1;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].NameColon?.Name.Identifier.Text == "toolFeatures")
            {
                index = i;
                break;
            }
        }
        if (index < 0 && args.Count >= 4 && args.All(a => a.NameColon == null))
        {
            index = 3;
        }
        if (index < 0 || index >= args.Count) return false;

        var toolArg = args[index];
        var newExpression = AppendToCollectionExpression(toolArg.Expression, toolVar);
        if (newExpression == null) return false;

        var newArg = toolArg.WithExpression(newExpression);
        newArgumentList = argumentList.WithArguments(args.Replace(toolArg, newArg));
        return true;
    }

    private static ExpressionSyntax? AppendToCollectionExpression(ExpressionSyntax expression, string toolVar)
    {
        var newElement = SyntaxFactory.IdentifierName(toolVar);

        if (expression is ArrayCreationExpressionSyntax arrayCreation && arrayCreation.Initializer != null)
        {
            return arrayCreation.WithInitializer(
                arrayCreation.Initializer.WithExpressions(AppendWithComma(arrayCreation.Initializer.Expressions, newElement)));
        }

        if (expression is ImplicitArrayCreationExpressionSyntax implicitArray)
        {
            return implicitArray.WithInitializer(
                implicitArray.Initializer.WithExpressions(AppendWithComma(implicitArray.Initializer.Expressions, newElement)));
        }

        // Outros formatos (ex.: uma variável, Array.Empty<IFeature>()) não são reescritos: melhor
        // desistir e deixar o chamador cair de volta para gerar uma feature nova encadeada.
        return null;
    }

    /// <summary>
    /// Acrescenta um elemento a uma lista separada por vírgula, com o mesmo espaçamento
    /// "vírgula + espaço" que o resto do código gerado usa (<c>Add*</c> do Roslyn não poria o espaço).
    /// </summary>
    private static SeparatedSyntaxList<T> AppendWithComma<T>(SeparatedSyntaxList<T> list, T newElement) where T : SyntaxNode
    {
        var separators = list.GetSeparators().ToList();
        separators.Add(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space));

        var nodes = list.ToList();
        // O último elemento pode ter trivia à direita (ex.: o espaço antes do "}" em "{ a }"); a
        // vírgula nova já traz seu próprio espaço, então essa trivia passa para o novo último
        // elemento (senão duplicaria o espaço antes da vírgula, ou perderia o espaço antes do "}").
        int lastIndex = nodes.Count - 1;
        var trailingTrivia = nodes[lastIndex].GetTrailingTrivia();
        nodes[lastIndex] = (T)nodes[lastIndex].WithTrailingTrivia();
        nodes.Add((T)newElement.WithTrailingTrivia(trailingTrivia));

        return SyntaxFactory.SeparatedList(nodes, separators);
    }
}
