using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Services;

public class RoslynDependencyAnalyzer
{
    public static string ResolveVariableName(string code, string featureName)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(featureName))
            return featureName;

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        // 1. Direct variable name match (fallback)
        var declarator = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Identifier.Text == featureName);
        if (declarator != null)
            return featureName;

        // 2. Argument literal match (e.g., passed to constructor or method)
        var literal = root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault(l => l.Token.ValueText == featureName);

        if (literal != null)
        {
            var decl = literal.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
            if (decl != null)
            {
                return decl.Identifier.Text;
            }
        }

        // 3. Property assignment match (e.g., x.Name = "featureName")
        var propAssignment = root.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(a => a.Left is IdentifierNameSyntax id && id.Identifier.Text == "Name" &&
                                 a.Right is LiteralExpressionSyntax lit && lit.Token.ValueText == featureName);

        if (propAssignment != null)
        {
            var statement = propAssignment.FirstAncestorOrSelf<ExpressionStatementSyntax>();
            if (statement?.Expression is AssignmentExpressionSyntax assign)
            {
                if (assign.Left is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax objId)
                {
                    return objId.Identifier.Text;
                }
            }
        }

        return featureName;
    }

    public IEnumerable<string> GetRootVariableNames(string csharpScript, string targetVariableName)
    {
        string resolvedName = ResolveVariableName(csharpScript, targetVariableName);
        return AnalyzeDependencies(csharpScript, resolvedName).roots;
    }

    public IEnumerable<string> GetFullDependencyGraph(string csharpScript, string targetVariableName)
    {
        string resolvedName = ResolveVariableName(csharpScript, targetVariableName);
        return AnalyzeDependencies(csharpScript, resolvedName).visited;
    }

    /// <summary>
    /// Variáveis do grafo de dependências (não só as raízes) cujo inicializador contém
    /// diretamente um <c>new Point3D(...)</c> — são as que <see cref="RoslynCoordinateMutator"/>
    /// sabe deslocar. Usado pelo Move: reescrever só as raízes deixa de fora variáveis
    /// intermediárias (ex.: um perfil que depende de uma variável de altura) que também têm
    /// coordenadas próprias e precisam se mover junto.
    /// </summary>
    public IEnumerable<string> GetMovableVariableNames(string csharpScript, string targetVariableName)
    {
        string resolvedName = ResolveVariableName(csharpScript, targetVariableName);
        var (_, visited) = AnalyzeDependencies(csharpScript, resolvedName);

        if (visited.Count == 0 || string.IsNullOrWhiteSpace(csharpScript))
            return Enumerable.Empty<string>();

        var tree = CSharpSyntaxTree.ParseText(csharpScript);
        var root = tree.GetRoot();

        var declarators = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(v => visited.Contains(v.Identifier.Text))
            .ToList();

        return declarators
            .Where(v => v.Initializer?.Value is ExpressionSyntax rhs &&
                        rhs.DescendantNodesAndSelf()
                            .OfType<ObjectCreationExpressionSyntax>()
                            .Any(o => o.Type is IdentifierNameSyntax { Identifier.Text: "Point3D" }))
            .Select(v => v.Identifier.Text)
            .ToList();
    }

    private (HashSet<string> roots, HashSet<string> visited) AnalyzeDependencies(string csharpScript, string targetVariableName)
    {
        var roots = new HashSet<string>();
        var visited = new HashSet<string>();

        if (string.IsNullOrWhiteSpace(csharpScript) || string.IsNullOrWhiteSpace(targetVariableName))
            return (roots, visited);

        var tree = CSharpSyntaxTree.ParseText(csharpScript);
        var root = tree.GetRoot();

        // Map variable declarations
        var variables = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .ToDictionary(v => v.Identifier.Text, v => v);

        var queue = new Queue<string>();
        queue.Enqueue(targetVariableName);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (visited.Contains(current)) continue;
            visited.Add(current);

            if (!variables.TryGetValue(current, out var declarator))
            {
                continue; // Not found in script, maybe a built-in or error
            }

            // Find all identifiers used in the right-hand side of this variable declaration
            if (declarator.Initializer?.Value is ExpressionSyntax rhs)
            {
                var dependencies = rhs.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Select(id => id.Identifier.Text)
                    .Where(name => variables.ContainsKey(name)) // Only consider dependencies that are other variables in our script
                    .ToList();

                if (dependencies.Count == 0)
                {
                    // No internal dependencies = it's a Root!
                    roots.Add(current);
                }
                else
                {
                    foreach (var dep in dependencies)
                    {
                        queue.Enqueue(dep);
                    }
                }
            }
        }

        return (roots, visited);
    }
}
