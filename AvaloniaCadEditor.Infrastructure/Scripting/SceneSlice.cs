using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

// Recorte de um sólido solto no arquivo de entrada da cena: compartilhado por quem tira o sólido
// da cena para dentro de uma peça (RoslynPartConverter, RoslynPartMerger).

/// <summary>Um <c>AddFeature(x)</c> ou <c>AddFeature(x.Transformed(P))</c> no topo da cena.</summary>
internal sealed record SceneRegistration(ExpressionStatementSyntax Statement, ILocalSymbol Local, ExpressionSyntax? Placement);

/// <summary>
/// As instruções top-level do arquivo e, para cada uma, as variáveis/funções locais da cena que
/// ela lê — a base do recorte.
/// </summary>
internal sealed class SceneStatements
{
    public SceneStatements(SyntaxTree tree, CSharpCompilation compilation)
    {
        Tree = tree;
        Model = compilation.GetSemanticModel(tree);
        FeatureType = compilation.GetTypeByMetadataName("AvaloniaCadEditor.Domain.Features.IFeature");
        Statements = ((CompilationUnitSyntax)tree.GetRoot()).Members
            .OfType<GlobalStatementSyntax>()
            .Select(g => g.Statement)
            .ToList();

        foreach (var statement in Statements)
        {
            References[statement] = ReferencedSymbols(statement);

            switch (statement)
            {
                case LocalDeclarationStatementSyntax { Declaration.Variables: { Count: 1 } variables }
                    when Model.GetDeclaredSymbol(variables[0]) is ILocalSymbol local:
                    Declarations[local] = statement;
                    break;
                case LocalFunctionStatementSyntax function when Model.GetDeclaredSymbol(function) is IMethodSymbol method:
                    Declarations[method] = statement;
                    break;
                case ExpressionStatementSyntax expression when AsRegistration(expression) is { } registration:
                    Registrations.Add(registration);
                    break;
                case ExpressionStatementSyntax expression when Subject(expression.Expression) is { } subject:
                    Mutations.Add((subject, statement));
                    break;
            }
        }
    }

    public SyntaxTree Tree { get; }
    public SemanticModel Model { get; }
    public INamedTypeSymbol? FeatureType { get; }
    public IReadOnlyList<StatementSyntax> Statements { get; }

    /// <summary>Variáveis e funções locais da cena lidas por cada instrução.</summary>
    public Dictionary<StatementSyntax, HashSet<ISymbol>> References { get; } = new();

    /// <summary>Onde cada variável movível (uma por declaração) e cada função local é declarada.</summary>
    public Dictionary<ISymbol, StatementSyntax> Declarations { get; } = new(SymbolEqualityComparer.Default);

    public List<SceneRegistration> Registrations { get; } = new();

    /// <summary>Instruções do tipo <c>x.Prop = ...;</c> / <c>x.Metodo(...);</c>, pela variável que alteram.</summary>
    public List<(ISymbol Subject, StatementSyntax Statement)> Mutations { get; } = new();

    public bool IsFeature(ITypeSymbol? type) =>
        type != null && FeatureType != null &&
        (SymbolEqualityComparer.Default.Equals(type, FeatureType) ||
         type.AllInterfaces.Contains(FeatureType, SymbolEqualityComparer.Default));

    public int LineOf(SyntaxNode node) => Tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;

    public bool Covers(SyntaxNode node, int line)
    {
        var span = Tree.GetLineSpan(node.Span);
        return line >= span.StartLinePosition.Line + 1 && line <= span.EndLinePosition.Line + 1;
    }

    public ISymbol? SceneSymbolOf(SyntaxNode node)
    {
        var info = Model.GetSymbolInfo(node);
        var symbol = (info.Symbol ?? info.CandidateSymbols.FirstOrDefault())?.OriginalDefinition;
        return symbol is ILocalSymbol or IMethodSymbol { MethodKind: MethodKind.LocalFunction } && IsTopLevel(symbol)
            ? symbol
            : null;
    }

    /// <summary>Declarado direto no topo do arquivo (não dentro de um laço, lambda ou função local).</summary>
    private bool IsTopLevel(ISymbol symbol) =>
        symbol.DeclaringSyntaxReferences.Any(r =>
            r.SyntaxTree == Tree &&
            r.GetSyntax() switch
            {
                VariableDeclaratorSyntax declarator => declarator.Parent?.Parent?.Parent is GlobalStatementSyntax,
                LocalFunctionStatementSyntax function => function.Parent is GlobalStatementSyntax,
                SingleVariableDesignationSyntax designation => designation.FirstAncestorOrSelf<StatementSyntax>()?.Parent is GlobalStatementSyntax,
                _ => false
            });

    private HashSet<ISymbol> ReferencedSymbols(StatementSyntax statement)
    {
        var found = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var identifier in statement.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (SceneSymbolOf(identifier) is { } symbol) found.Add(symbol);
        }
        return found;
    }

    private SceneRegistration? AsRegistration(ExpressionStatementSyntax statement)
    {
        if (statement.Expression is not InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "AddFeature" },
                ArgumentList.Arguments: { Count: 1 } arguments
            })
            return null;

        var argument = arguments[0].Expression;
        if (argument is IdentifierNameSyntax identifier)
        {
            return SceneSymbolOf(identifier) is ILocalSymbol local ? new SceneRegistration(statement, local, null) : null;
        }
        if (argument is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transformed", Expression: IdentifierNameSyntax inner },
                ArgumentList.Arguments: { Count: 1 } transformedArguments
            } &&
            SceneSymbolOf(inner) is ILocalSymbol transformedLocal)
        {
            return new SceneRegistration(statement, transformedLocal, transformedArguments[0].Expression);
        }
        return null;
    }

    /// <summary>A variável no começo do alvo de <c>x.A.B = ...</c>, <c>x = ...</c> ou <c>x.Metodo(...)</c>.</summary>
    private ISymbol? Subject(ExpressionSyntax expression)
    {
        var target = expression switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left,
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } => member.Expression,
            _ => null
        };
        while (target is MemberAccessExpressionSyntax access) target = access.Expression;
        return target is IdentifierNameSyntax identifier ? SceneSymbolOf(identifier) as ILocalSymbol : null;
    }
}

internal sealed class SceneSlice
{
    private SceneSlice(string? refusal) => Refusal = refusal;

    public string? Refusal { get; }

    /// <summary>Instruções que vão para o Build(), na ordem do arquivo.</summary>
    public List<StatementSyntax> Statements { get; } = new();

    /// <summary>Instruções a apagar da cena: o recorte (menos funções locais que a cena ainda usa) e os AddFeature dos sólidos internos.</summary>
    public HashSet<StatementSyntax> Removed { get; } = new();

    /// <summary>Variáveis da cena que o Build() lê mas que ficam na cena: viram parâmetros.</summary>
    public List<ILocalSymbol> Inputs { get; } = new();

    public static SceneSlice Compute(SceneStatements scene, SceneRegistration root)
    {
        var cut = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var registrationsOf = scene.Registrations
            .Where(r => r != root)
            .ToLookup(r => (ISymbol)r.Local, SymbolEqualityComparer.Default);

        while (true)
        {
            var (statements, symbols) = Close(scene, root.Local, cut);

            // Quem fica na cena: tudo fora do recorte, exceto os AddFeature dos sólidos que entram
            // nele (somem com eles). O AddFeature da raiz fica (vira o Add) e o placement dele
            // continua lendo as variáveis da cena.
            var outside = scene.Statements
                .Where(s => !statements.Contains(s))
                .Where(s => !symbols.Any(sym => registrationsOf[sym].Any(r => r.Statement == s && r.Placement == null)))
                .ToList();

            var grow = symbols
                .OfType<ILocalSymbol>()
                .Where(local => !SymbolEqualityComparer.Default.Equals(local, root.Local))
                .Where(local => !scene.IsFeature(local.Type))
                .Where(local => !scene.Declarations.ContainsKey(local) || outside.Any(s => scene.References[s].Contains(local)))
                .Where(local => !cut.Contains(local))
                .ToList();
            if (grow.Count > 0)
            {
                cut.UnionWith(grow);
                continue;
            }

            foreach (var local in symbols.OfType<ILocalSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(local, root.Local) || !scene.IsFeature(local.Type)) continue;

                if (!scene.Declarations.ContainsKey(local))
                    return new SceneSlice($"'{local.Name}' é um sólido declarado de um jeito que não dá para mover (declare uma variável por instrução).");

                var user = outside.FirstOrDefault(s => scene.References[s].Contains(local));
                if (user != null)
                {
                    string why = registrationsOf[local].Any(r => r.Statement == user)
                        ? "é registrado deslocado (AddFeature(...Transformed(...)))"
                        : "também é usado";
                    return new SceneSlice($"'{local.Name}' {why} na linha {scene.LineOf(user)}; a peça precisa ser dona dele. Tire essa dependência antes de converter.");
                }
            }

            var slice = new SceneSlice(null);
            slice.Statements.AddRange(scene.Statements.Where(statements.Contains));
            foreach (var statement in slice.Statements)
            {
                bool sharedFunction = statement is LocalFunctionStatementSyntax function &&
                    scene.Model.GetDeclaredSymbol(function) is { } method &&
                    outside.Any(s => scene.References[s].Contains(method));
                if (!sharedFunction) slice.Removed.Add(statement);
            }
            foreach (var local in symbols)
            {
                foreach (var registration in registrationsOf[local]) slice.Removed.Add(registration.Statement);
            }
            slice.Inputs.AddRange(cut.OfType<ILocalSymbol>()
                .Where(local => slice.Statements.Any(s => scene.References[s].Contains(local)))
                .OrderBy(local => local.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0));

            if (slice.Statements.SelectMany(s => s.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    .FirstOrDefault(call => IsSceneRegistration(scene, call)) is { } nested)
                return new SceneSlice($"A linha {scene.LineOf(nested)} registra outra feature na cena no meio do código do sólido; o Build() de uma peça não pode fazer isso.");

            return slice;
        }
    }

    /// <summary>Fecho para trás a partir da raiz, sem atravessar as variáveis cortadas.</summary>
    private static (HashSet<StatementSyntax> Statements, HashSet<ISymbol> Symbols) Close(
        SceneStatements scene, ILocalSymbol root, HashSet<ISymbol> cut)
    {
        var statements = new HashSet<StatementSyntax>();
        var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<ISymbol>();
        queue.Enqueue(root);

        void Take(StatementSyntax statement)
        {
            if (!statements.Add(statement)) return;
            foreach (var next in scene.References[statement]) queue.Enqueue(next);
        }

        while (queue.Count > 0)
        {
            var symbol = queue.Dequeue();
            if (cut.Contains(symbol) || !symbols.Add(symbol)) continue;

            if (scene.Declarations.TryGetValue(symbol, out var declaration)) Take(declaration);
            foreach (var (subject, statement) in scene.Mutations)
            {
                if (SymbolEqualityComparer.Default.Equals(subject, symbol)) Take(statement);
            }
        }
        return (statements, symbols);
    }

    private static bool IsSceneRegistration(SceneStatements scene, InvocationExpressionSyntax call)
    {
        var info = scene.Model.GetSymbolInfo(call);
        var method = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        return method?.ContainingType?.ToDisplayString() == "AvaloniaCadEditor.Domain.Scripting.Scene";
    }
}
