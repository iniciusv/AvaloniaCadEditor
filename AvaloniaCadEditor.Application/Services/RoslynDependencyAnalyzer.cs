using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Services;

public class RoslynDependencyAnalyzer
{
    public IEnumerable<string> GetRootVariableNames(string csharpScript, string targetVariableName)
    {
        return AnalyzeDependencies(csharpScript, targetVariableName).roots;
    }

    public IEnumerable<string> GetFullDependencyGraph(string csharpScript, string targetVariableName)
    {
        return AnalyzeDependencies(csharpScript, targetVariableName).visited;
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
