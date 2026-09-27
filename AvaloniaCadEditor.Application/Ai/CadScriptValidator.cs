using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Ai;

public sealed record CadScriptValidationResult(bool IsValid, string? Error);

/// <summary>
/// Validação sintática barata (sem compilar/executar) do script retornado pela IA,
/// para dar um erro claro e rápido antes de rodar o <c>ISceneEvaluator</c> de verdade.
/// </summary>
public static class CadScriptValidator
{
    public static CadScriptValidationResult Validate(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new CadScriptValidationResult(false, "O script está vazio.");
        }

        var options = new CSharpParseOptions(kind: SourceCodeKind.Script);
        var tree = CSharpSyntaxTree.ParseText(code, options);
        var root = tree.GetRoot();

        var parseErrors = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (parseErrors.Count > 0)
        {
            return new CadScriptValidationResult(false, "Erro de sintaxe: " + string.Join(" | ", parseErrors.Select(d => d.ToString())));
        }

        if (root.DescendantNodes().OfType<UsingDirectiveSyntax>().Any())
        {
            return new CadScriptValidationResult(false, "O script não pode conter diretivas 'using'.");
        }

        if (root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Any())
        {
            return new CadScriptValidationResult(false, "O script não pode declarar 'namespace'.");
        }

        if (root.DescendantNodes().OfType<TypeDeclarationSyntax>().Any())
        {
            return new CadScriptValidationResult(false, "O script não pode declarar 'class', 'struct', 'record' ou 'interface'.");
        }

        bool hasAddFeature = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Expression is IdentifierNameSyntax id && id.Identifier.Text == "AddFeature");
        if (!hasAddFeature)
        {
            return new CadScriptValidationResult(false, "O script precisa registrar ao menos uma feature com AddFeature(...).");
        }

        return new CadScriptValidationResult(true, null);
    }
}
