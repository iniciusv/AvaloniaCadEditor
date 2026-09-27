using System;
using AvaloniaCadEditor.Application.Services;

class Program {
    static void Main() {
        string script = "var carriage = BooleanFeature.CreateDifference(\"Carrinho_MGN9C\", blockBase, railTool, hole1, hole2, hole3, hole4);";
        string result = RoslynDependencyAnalyzer.ResolveVariableName(script, "Carrinho_MGN9C");
        Console.WriteLine("Result: " + result);
    }
}
