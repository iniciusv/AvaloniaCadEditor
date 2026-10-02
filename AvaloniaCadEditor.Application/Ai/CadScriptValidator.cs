using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Ai;

public sealed record CadScriptValidationResult(bool IsValid, string? Error);

/// <summary>
/// Validação sintática barata (sem compilar/executar) do arquivo retornado pela IA,
/// para dar um erro claro e rápido antes de rodar o <c>ISceneEvaluator</c> de verdade.
/// As regras dependem do formato esperado do arquivo (<see cref="CadFileKind"/>): um script solto
/// (statements top-level + AddFeature/Add) ou um arquivo de peça (um único record : Part).
/// </summary>
public static class CadScriptValidator
{
    public static CadScriptValidationResult Validate(string code, CadFileKind kind = CadFileKind.StandaloneScript)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new CadScriptValidationResult(false,
                kind == CadFileKind.PartFile ? "O arquivo da peça está vazio." : "O script está vazio.");
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
            return new CadScriptValidationResult(false,
                "O arquivo não pode conter diretivas 'using' (use o nome totalmente qualificado quando precisar, ex.: [System.ComponentModel.DataAnnotations.Range(10, 500)]).");
        }

        if (root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Any())
        {
            return new CadScriptValidationResult(false, "O arquivo não pode declarar 'namespace'.");
        }

        return kind switch
        {
            CadFileKind.PartFile => ValidatePartFile(root),
            CadFileKind.SupportFile => ValidateSupportFile(root),
            _ => ValidateScriptFile(root, kind)
        };
    }

    private static CadScriptValidationResult ValidateScriptFile(SyntaxNode root, CadFileKind kind)
    {
        if (root.DescendantNodes().OfType<TypeDeclarationSyntax>().Any())
        {
            return new CadScriptValidationResult(false,
                "O script não pode declarar 'class', 'struct', 'record' ou 'interface' — peças reutilizáveis vão num arquivo próprio em Pecas/ (um record que herda de Part).");
        }

        // O arquivo de entrada de um projeto pode registrar só instâncias de peça, com Add(...).
        bool hasAddFeature = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Expression is IdentifierNameSyntax id &&
                        (id.Identifier.Text == "AddFeature" || (kind == CadFileKind.SceneEntry && id.Identifier.Text == "Add")));
        if (!hasAddFeature)
        {
            return new CadScriptValidationResult(false, "O script precisa registrar ao menos uma feature com AddFeature(...).");
        }

        return new CadScriptValidationResult(true, null);
    }

    private static CadScriptValidationResult ValidatePartFile(SyntaxNode root)
    {
        if (root.DescendantNodes().OfType<GlobalStatementSyntax>().Any())
        {
            return new CadScriptValidationResult(false,
                "Um arquivo de peça não pode ter statements soltos (AddFeature, Add, var no nível superior) — isso pertence ao arquivo de entrada da cena (ex.: Cena.cs).");
        }

        if (root.DescendantNodes().OfType<TypeDeclarationSyntax>().Any(t => t is not RecordDeclarationSyntax))
        {
            return new CadScriptValidationResult(false,
                "Um arquivo de peça só pode declarar um 'record' que herda de Part — nada de 'class', 'struct' ou 'interface' soltos.");
        }

        var records = root.DescendantNodes().OfType<RecordDeclarationSyntax>()
            .Where(r => !r.Modifiers.Any(SyntaxKind.AbstractKeyword))
            .Where(r => r.BaseList?.Types.Any(t => SimpleBaseName(t.Type) == "Part") == true)
            .ToList();

        if (records.Count != 1)
        {
            return new CadScriptValidationResult(false, records.Count == 0
                ? "O arquivo precisa declarar um 'record' que herda de Part (ex.: public sealed record NomeDaPeca(double Largura = 50) : Part)."
                : $"O arquivo deve declarar exatamente um record que herda de Part (encontrados {records.Count}).");
        }

        bool hasBuild = records[0].Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "Build");
        if (!hasBuild)
        {
            return new CadScriptValidationResult(false, $"{records[0].Identifier.Text} precisa sobrescrever 'protected override IFeature Build()'.");
        }

        bool callsSceneMethods = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Expression is IdentifierNameSyntax id && (id.Identifier.Text == "AddFeature" || id.Identifier.Text == "Add"));
        if (callsSceneMethods)
        {
            return new CadScriptValidationResult(false,
                "Build() não deve chamar AddFeature/Add — só devolve a IFeature da peça; quem registra e posiciona a instância é o arquivo de entrada da cena.");
        }

        return new CadScriptValidationResult(true, null);
    }

    private static CadScriptValidationResult ValidateSupportFile(SyntaxNode root)
    {
        if (root.DescendantNodes().OfType<GlobalStatementSyntax>().Any())
        {
            return new CadScriptValidationResult(false,
                "Um arquivo auxiliar (tipos/constantes) não pode ter statements soltos — isso pertence ao arquivo de entrada da cena.");
        }

        return new CadScriptValidationResult(true, null);
    }

    private static string? SimpleBaseName(TypeSyntax type) => type switch
    {
        SimpleNameSyntax s => s.Identifier.Text,
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        _ => null
    };
}
