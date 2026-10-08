using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

/// <summary>
/// Rastreia a cadeia que produz um sólido do <c>Build()</c> de uma peça usando o modelo semântico
/// do Roslyn sobre a mesma compilação que executa a cena — como o <see cref="RoslynCodeNavigator"/>.
/// Ser semântico é o que faz diferença aqui: a cadeia de um furo termina nos parâmetros do record
/// (<c>DistanciaFuroPlaca1Y</c>), que não são variáveis locais, e atravessa funções locais
/// (<c>CantoBaseX</c>) — nada disso é visível para uma busca por nome.
/// </summary>
public sealed class RoslynPartSolidTracer : IPartSolidTracer
{
    public PartSolidInputs? Trace(IReadOnlyList<ScriptDocument> documents, string partTypeName, string solidName)
    {
        if (documents == null || string.IsNullOrEmpty(partTypeName) || string.IsNullOrEmpty(solidName)) return null;

        var compilation = RoslynSceneEvaluator.CreateCompilation(documents, out var trees);
        var featureType = compilation.GetTypeByMetadataName("AvaloniaCadEditor.Domain.Features.IFeature");

        foreach (var tree in trees)
        {
            var record = tree.GetRoot().DescendantNodes()
                .OfType<RecordDeclarationSyntax>()
                .FirstOrDefault(r => r.Identifier.ValueText == partTypeName);
            if (record == null) continue;

            var model = compilation.GetSemanticModel(tree);
            if (model.GetDeclaredSymbol(record) is not { } recordSymbol) return null;

            var seed = FindSolidSeed(record, solidName);
            if (seed == null) return null;

            var target = Walk(model, recordSymbol, trees, seed);

            // Com quem cada entrada é compartilhada: o mesmo passeio para cada sólido-folha da peça.
            var others = new Dictionary<string, HashSet<ISymbol>>(StringComparer.Ordinal);
            foreach (var (name, node) in LeafSolids(record, model, featureType))
            {
                if (name == solidName) continue;
                others[name] = Walk(model, recordSymbol, trees, node);
            }

            var inputs = target
                .Select(symbol => ToInput(symbol, recordSymbol, trees, others))
                .Where(input => input != null)
                .Select(input => input!)
                // Exclusivas primeiro, parâmetros do record antes dos intermediários, e depois por
                // nome: é a ordem em que o inspector lista e em que convém ler a cadeia.
                .OrderBy(input => input.IsExclusive ? 0 : 1)
                .ThenBy(input => input.Kind == SolidInputKind.PartParameter ? 0 : 1)
                .ThenBy(input => input.Name, StringComparer.Ordinal)
                .ToList();

            return new PartSolidInputs(partTypeName, solidName, ToLocation(tree, DeclarationSpan(seed)), inputs);
        }

        return null;
    }

    public IReadOnlyList<string>? TraceArgument(
        IReadOnlyList<ScriptDocument> documents, string partTypeName, string solidName, string constructorParameterName)
    {
        if (documents == null || string.IsNullOrEmpty(partTypeName) || string.IsNullOrEmpty(solidName) ||
            string.IsNullOrEmpty(constructorParameterName)) return null;

        var compilation = RoslynSceneEvaluator.CreateCompilation(documents, out var trees);

        foreach (var tree in trees)
        {
            var record = tree.GetRoot().DescendantNodes()
                .OfType<RecordDeclarationSyntax>()
                .FirstOrDefault(r => r.Identifier.ValueText == partTypeName);
            if (record == null) continue;

            var model = compilation.GetSemanticModel(tree);
            if (model.GetDeclaredSymbol(record) is not { } recordSymbol) return null;

            var creation = FindSolidSeed(record, solidName)?
                .DescendantNodesAndSelf()
                .OfType<BaseObjectCreationExpressionSyntax>()
                .FirstOrDefault();
            if (creation?.ArgumentList == null || SymbolOf(model, creation) is not IMethodSymbol constructor) return null;

            var argument = ArgumentFor(creation.ArgumentList, constructor, constructorParameterName);
            if (argument == null) return null;

            return Walk(model, recordSymbol, trees, argument.Expression)
                .Where(symbol => KindOf(symbol, recordSymbol, trees) == SolidInputKind.PartParameter)
                .Select(symbol => symbol.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }

        return null;
    }

    /// <summary>O argumento que preenche o parâmetro <paramref name="name"/>: nomeado (<c>radius: x</c>) ou pela posição.</summary>
    private static ArgumentSyntax? ArgumentFor(ArgumentListSyntax arguments, IMethodSymbol constructor, string name)
    {
        var named = arguments.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name);
        if (named != null) return named;

        int index = -1;
        for (int i = 0; i < constructor.Parameters.Length; i++)
        {
            if (constructor.Parameters[i].Name == name) { index = i; break; }
        }
        if (index < 0 || index >= arguments.Arguments.Count) return null;
        var positional = arguments.Arguments[index];
        return positional.NameColon == null ? positional : null;
    }

    // ---------------------------------------------------------------- O passeio

    /// <summary>
    /// Símbolos relevantes alcançados a partir de <paramref name="seed"/>: para cada identificador,
    /// resolve o símbolo e, se for um local/função local/constante da cena, enfileira o que o
    /// define, até chegar nos parâmetros do record (que não expandem).
    /// </summary>
    private static HashSet<ISymbol> Walk(
        SemanticModel model, INamedTypeSymbol record, IReadOnlyList<SyntaxTree> trees, SyntaxNode seed)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<SyntaxNode>();
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            foreach (var identifier in queue.Dequeue().DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                var symbol = SymbolOf(model, identifier);
                if (symbol == null) continue;

                symbol = Normalize(symbol, record);
                if (KindOf(symbol, record, trees) == null) continue;
                if (!visited.Add(symbol)) continue;   // ciclos e repetições são inofensivos

                foreach (var next in Expansion(symbol)) queue.Enqueue(next);
            }
        }

        return visited;
    }

    /// <summary>
    /// Dentro do corpo de um método, um parâmetro do construtor primário é visto como a propriedade
    /// de mesmo nome; para quem edita a peça as duas são a mesma coisa, e é o parâmetro que carrega
    /// o valor padrão. Mesma equivalência que <see cref="RoslynCodeNavigator"/> usa para renomear.
    /// </summary>
    private static ISymbol Normalize(ISymbol symbol, INamedTypeSymbol record)
    {
        if (symbol is IPropertySymbol property &&
            SymbolEqualityComparer.Default.Equals(property.ContainingType, record))
        {
            var parameter = record.InstanceConstructors
                .Where(IsPrimaryConstructor)
                .SelectMany(ctor => ctor.Parameters)
                .FirstOrDefault(p => p.Name == property.Name);
            if (parameter != null) return parameter.OriginalDefinition;
        }
        return symbol.OriginalDefinition;
    }

    /// <summary>
    /// O que conta como entrada da cadeia. Fica de fora tudo que não é editável na peça: tipos
    /// (<c>Point3D</c>), membros da API (<c>Vector3D.UnitY</c>, <c>Name</c>, <c>IsTransientTool</c>)
    /// e símbolos de outras peças. Null = ignorar.
    /// </summary>
    private static SolidInputKind? KindOf(ISymbol symbol, INamedTypeSymbol record, IReadOnlyList<SyntaxTree> trees)
    {
        switch (symbol)
        {
            case IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } ctor }
                when SymbolEqualityComparer.Default.Equals(ctor.ContainingType, record) && IsPrimaryConstructor(ctor):
                return SolidInputKind.PartParameter;

            case ILocalSymbol when DeclaredInScene(symbol, trees):
                return SolidInputKind.Local;

            case IMethodSymbol { MethodKind: MethodKind.LocalFunction } when DeclaredInScene(symbol, trees):
                return SolidInputKind.LocalFunction;

            // Uma const/estática de um arquivo auxiliar da cena (ex.: static class Medidas). Um campo
            // do Domain (Vector3D.UnitY) não é declarado nas árvores da cena e cai fora.
            case IFieldSymbol when DeclaredInScene(symbol, trees):
                return SolidInputKind.Constant;

            default:
                return null;
        }
    }

    /// <summary>O que define o símbolo, para continuar o passeio. Parâmetros do record não expandem.</summary>
    private static IEnumerable<SyntaxNode> Expansion(ISymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax())
            {
                case VariableDeclaratorSyntax { Initializer.Value: { } value }:
                    yield return value;
                    break;
                case LocalFunctionStatementSyntax function:
                    if (function.Body != null) yield return function.Body;
                    if (function.ExpressionBody?.Expression != null) yield return function.ExpressionBody.Expression;
                    break;
            }
        }
    }

    private static bool DeclaredInScene(ISymbol symbol, IReadOnlyList<SyntaxTree> trees) =>
        symbol.DeclaringSyntaxReferences.Any(r => trees.Contains(r.SyntaxTree));

    private static bool IsPrimaryConstructor(IMethodSymbol ctor) =>
        ctor.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax);

    // ---------------------------------------------------------------- Sólidos da peça

    /// <summary>
    /// Onde o sólido nasce: a variável de mesmo nome (o padrão ensinado à IA) ou, na falta dela, a
    /// instrução que contém o literal com o nome — o mesmo par de casos de
    /// <c>PartSourceEditor.FindToolDeclaration</c>.
    /// </summary>
    private static SyntaxNode? FindSolidSeed(RecordDeclarationSyntax record, string solidName)
    {
        var declarator = record.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Identifier.ValueText == solidName);
        if (declarator != null) return declarator;

        var literal = record.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression) && l.Token.ValueText == solidName);
        return literal?.FirstAncestorOrSelf<StatementSyntax>();
    }

    /// <summary>
    /// Sólidos-folha da peça: os declarados com <c>new AlgumaFeature(...)</c> direto. Uniões e
    /// recortes (<c>BooleanFeature.CreateUnion(...)</c>) e apelidos (<c>IFeature corpo = corpo_bruto</c>)
    /// ficam fora de propósito: dizer que um parâmetro é "compartilhado com corpo_bruto" não ajuda a
    /// decidir o que mexer, já que o corpo é a montagem das folhas. Uma peça usada como ferramenta
    /// (<c>new FuroPorca(...).Instance("fixacao_1")</c>) também é folha: para a hospedeira, é um sólido.
    /// </summary>
    private static IEnumerable<(string Name, SyntaxNode Node)> LeafSolids(
        RecordDeclarationSyntax record, SemanticModel model, INamedTypeSymbol? featureType)
    {
        foreach (var declarator in record.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            bool leaf = declarator.Initializer?.Value switch
            {
                BaseObjectCreationExpressionSyntax creation => CreatesFeature(creation, model, featureType),
                InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Instance", Expression: BaseObjectCreationExpressionSyntax }
                } instance => CreatesFeature(instance, model, featureType),
                _ => false
            };
            if (leaf) yield return (declarator.Identifier.ValueText, declarator);
        }
    }

    private static bool CreatesFeature(
        ExpressionSyntax creation, SemanticModel model, INamedTypeSymbol? featureType)
    {
        var type = model.GetTypeInfo(creation).Type;
        if (type == null) return false;
        if (featureType != null) return type.AllInterfaces.Contains(featureType, SymbolEqualityComparer.Default);

        // Sem o símbolo de IFeature (compilação sem a referência do Domain), vale o nome do tipo.
        return type.Name.EndsWith("Feature", StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- Montagem do resultado

    private static SolidInput? ToInput(
        ISymbol symbol, INamedTypeSymbol record, IReadOnlyList<SyntaxTree> trees,
        Dictionary<string, HashSet<ISymbol>> others)
    {
        var kind = KindOf(symbol, record, trees);
        if (kind == null) return null;

        var declarations = symbol.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .Where(syntax => trees.Contains(syntax.SyntaxTree))
            .ToList();

        var spans = declarations
            .Select(syntax => ToLocation(syntax.SyntaxTree, DeclarationSpan(syntax)))
            .Distinct()
            .ToList();

        var sharedWith = others
            .Where(other => other.Value.Contains(symbol))
            .Select(other => other.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new SolidInput(symbol.Name, kind.Value, ValueCode(declarations), spans, sharedWith);
    }

    /// <summary>Valor padrão do parâmetro, ou a expressão que calcula o local/a função.</summary>
    private static string? ValueCode(IEnumerable<SyntaxNode> declarations)
    {
        foreach (var syntax in declarations)
        {
            switch (syntax)
            {
                case ParameterSyntax { Default.Value: { } defaultValue }: return defaultValue.ToString();
                case VariableDeclaratorSyntax { Initializer.Value: { } value }: return value.ToString();
                case LocalFunctionStatementSyntax { ExpressionBody.Expression: { } body }: return body.ToString();
            }
        }
        return null;
    }

    /// <summary>
    /// Trecho a destacar: a instrução inteira, não só o nome — ler
    /// <c>var raioFuro = DiametroFuroM3 / 2;</c> é o que explica a cadeia. Para um parâmetro do
    /// record, o próprio parâmetro na lista do topo.
    /// </summary>
    private static TextSpan DeclarationSpan(SyntaxNode syntax) => syntax switch
    {
        VariableDeclaratorSyntax declarator =>
            (declarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>()
             ?? (SyntaxNode?)declarator.FirstAncestorOrSelf<FieldDeclarationSyntax>())?.Span ?? declarator.Span,
        _ => syntax.Span
    };

    private static ISymbol? SymbolOf(SemanticModel model, SyntaxNode node)
    {
        var info = model.GetSymbolInfo(node);
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    private static CodeLocation ToLocation(SyntaxTree tree, TextSpan span)
    {
        var text = tree.GetText();
        var position = text.Lines.GetLinePosition(span.Start);
        string lineText = text.Lines[position.Line].ToString().Trim();
        return new CodeLocation(tree.FilePath, span.Start, span.Length, position.Line + 1, position.Character + 1, lineText);
    }
}
