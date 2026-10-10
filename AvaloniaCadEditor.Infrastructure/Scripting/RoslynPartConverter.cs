using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AvaloniaCadEditor.Application.Ai.Context;
using AvaloniaCadEditor.Application.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

/// <summary>
/// Converte um sólido solto em peça com o modelo semântico do Roslyn, sobre a mesma compilação que
/// executa a cena (como o <see cref="RoslynPartSolidTracer"/>).
/// <para>
/// O recorte parte da variável registrada no <c>AddFeature</c> e anda para trás pelas instruções
/// top-level: declarações das variáveis que ela lê e instruções que só mexem nelas
/// (<c>furo.IsTransientTool = true;</c>). Uma variável que não é sólido e que a cena também usa
/// (ou que não dá para mover, ex.: declarada junto com outra) fica na cena e vira parâmetro do
/// record, passado na chamada. Um sólido intermediário que a cena também usa recusa a conversão:
/// tirá-lo da cena quebraria quem o usa, e duplicá-lo mudaria o modelo.
/// </para>
/// </summary>
public sealed class RoslynPartConverter : IPartConverter
{
    private const string Indent = "        ";

    /// <summary>Membros que um record de peça já tem: um parâmetro com esse nome os esconderia.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "Build", "DrawingLabel", "GetParameters", "Validate", "EqualityContract",
        "Equals", "GetHashCode", "ToString", "Deconstruct", "PrintMembers"
    };

    public PartConversion Convert(IReadOnlyList<ScriptDocument> documents, string filePath, int line, string typeName)
    {
        typeName = (typeName ?? string.Empty).Trim();
        if (!SyntaxFacts.IsValidIdentifier(typeName) || SyntaxFacts.GetKeywordKind(typeName) != SyntaxKind.None)
            return PartConversion.Refuse($"\"{typeName}\" não é um nome de tipo válido em C#.");

        string partFile = AiEditScopePolicy.PartsFolder + typeName + ".cs";
        if (documents.Any(d => SamePath(d.FilePath, partFile)))
            return PartConversion.Refuse($"Já existe o arquivo {partFile}.");

        var compilation = RoslynSceneEvaluator.CreateCompilation(documents, out var trees);
        if (compilation.GetSymbolsWithName(typeName, SymbolFilter.Type).Any())
            return PartConversion.Refuse($"Já existe um tipo chamado {typeName} na cena.");
        if (compilation.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } error)
            return PartConversion.Refuse($"Corrija os erros da cena antes de converter ({error.GetMessage(CultureInfo.CurrentCulture)}).");

        var tree = trees.FirstOrDefault(t => SamePath(t.FilePath, filePath));
        if (tree == null) return PartConversion.Refuse($"O arquivo {filePath} não faz parte da cena.");

        var scene = new SceneStatements(tree, compilation);
        if (scene.Statements.Count == 0)
            return PartConversion.Refuse("Só dá para converter sólidos registrados no arquivo de entrada da cena.");

        var registration = scene.Registrations.FirstOrDefault(r => scene.Covers(r.Statement, line));
        if (registration == null)
            return PartConversion.Refuse("Este sólido não foi registrado com AddFeature(variavel) — não há uma variável para converter.");
        if (scene.Registrations.Count(r => SymbolEqualityComparer.Default.Equals(r.Local, registration.Local)) > 1)
            return PartConversion.Refuse($"'{registration.Local.Name}' é registrado mais de uma vez com AddFeature.");
        if (!scene.Declarations.ContainsKey(registration.Local))
            return PartConversion.Refuse($"'{registration.Local.Name}' precisa ser declarado sozinho no topo da cena (var {registration.Local.Name} = ...;).");

        var slice = Slice.Compute(scene, registration);
        if (slice.Refusal != null) return PartConversion.Refuse(slice.Refusal);

        var parameters = BuildParameters(scene, slice, typeName);
        if (parameters.Refusal != null) return PartConversion.Refuse(parameters.Refusal);

        string source = tree.GetText().ToString();
        string newLine = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string partText = BuildPartFile(scene, slice, parameters.Items, registration, typeName, filePath, newLine);
        string sceneText = RewriteScene(scene, slice, parameters.Items, registration, typeName);

        return new PartConversion(new[]
        {
            new ScriptDocument(tree.FilePath, sceneText),
            new ScriptDocument(partFile, partText)
        }, null);
    }

    // ---------------------------------------------------------------- As instruções da cena

    /// <summary>Um <c>AddFeature(x)</c> ou <c>AddFeature(x.Transformed(P))</c> no topo da cena.</summary>
    private sealed record Registration(ExpressionStatementSyntax Statement, ILocalSymbol Local, ExpressionSyntax? Placement);

    /// <summary>
    /// As instruções top-level do arquivo e, para cada uma, as variáveis/funções locais da cena que
    /// ela lê — a base do recorte.
    /// </summary>
    private sealed class SceneStatements
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

        public List<Registration> Registrations { get; } = new();

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

        private Registration? AsRegistration(ExpressionStatementSyntax statement)
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
                return SceneSymbolOf(identifier) is ILocalSymbol local ? new Registration(statement, local, null) : null;
            }
            if (argument is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transformed", Expression: IdentifierNameSyntax inner },
                    ArgumentList.Arguments: { Count: 1 } transformedArguments
                } &&
                SceneSymbolOf(inner) is ILocalSymbol transformedLocal)
            {
                return new Registration(statement, transformedLocal, transformedArguments[0].Expression);
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

    // ---------------------------------------------------------------- O recorte

    private sealed class Slice
    {
        private Slice(string? refusal) => Refusal = refusal;

        public string? Refusal { get; }

        /// <summary>Instruções que vão para o Build(), na ordem do arquivo.</summary>
        public List<StatementSyntax> Statements { get; } = new();

        /// <summary>Instruções a apagar da cena: o recorte (menos funções locais que a cena ainda usa) e os AddFeature dos sólidos internos.</summary>
        public HashSet<StatementSyntax> Removed { get; } = new();

        /// <summary>Variáveis da cena que o Build() lê mas que ficam na cena: viram parâmetros.</summary>
        public List<ILocalSymbol> Inputs { get; } = new();

        public static Slice Compute(SceneStatements scene, Registration root)
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
                        return new Slice($"'{local.Name}' é um sólido declarado de um jeito que não dá para mover (declare uma variável por instrução).");

                    var user = outside.FirstOrDefault(s => scene.References[s].Contains(local));
                    if (user != null)
                    {
                        string why = registrationsOf[local].Any(r => r.Statement == user)
                            ? "é registrado deslocado (AddFeature(...Transformed(...)))"
                            : "também é usado";
                        return new Slice($"'{local.Name}' {why} na linha {scene.LineOf(user)}; a peça precisa ser dona dele. Tire essa dependência antes de converter.");
                    }
                }

                var slice = new Slice(null);
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
                    return new Slice($"A linha {scene.LineOf(nested)} registra outra feature na cena no meio do código do sólido; o Build() de uma peça não pode fazer isso.");

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

    // ---------------------------------------------------------------- Parâmetros

    /// <summary>Um parâmetro do record: a variável da cena que o alimenta, o nome, o tipo e o padrão (se constante).</summary>
    private sealed record Parameter(ILocalSymbol Local, string Name, string Type, string? Default);

    private static (IReadOnlyList<Parameter> Items, string? Refusal) BuildParameters(SceneStatements scene, Slice slice, string typeName)
    {
        var taken = new HashSet<string>(ReservedNames, StringComparer.Ordinal) { typeName };
        foreach (var statement in slice.Statements)
        {
            foreach (var declarator in statement.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>()) taken.Add(declarator.Identifier.ValueText);
            if (statement is LocalFunctionStatementSyntax function) taken.Add(function.Identifier.ValueText);
        }

        var items = new List<Parameter>();
        foreach (var local in slice.Inputs)
        {
            if (!local.Type.CanBeReferencedByName && local.Type is not IArrayTypeSymbol)
                return (items, $"'{local.Name}' tem um tipo sem nome ({local.Type.ToDisplayString()}); não dá para virar parâmetro da peça.");

            string name = UniqueName(PascalCase(local.Name), taken);
            taken.Add(name);
            string type = local.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            items.Add(new Parameter(local, name, type, ConstantDefault(scene, local)));
        }

        // Parâmetros com valor padrão precisam vir depois dos sem padrão.
        return (items.Where(p => p.Default == null).Concat(items.Where(p => p.Default != null)).ToList(), null);
    }

    /// <summary>O valor da variável como literal, quando o Roslyn sabe calculá-lo e o tipo aceita padrão sem sufixo.</summary>
    private static string? ConstantDefault(SceneStatements scene, ILocalSymbol local)
    {
        if (local.Type.SpecialType is not (SpecialType.System_Double or SpecialType.System_Int32 or SpecialType.System_Boolean or SpecialType.System_String))
            return null;
        if (!scene.Declarations.TryGetValue(local, out var statement) ||
            statement is not LocalDeclarationStatementSyntax { Declaration.Variables: [{ Initializer.Value: { } value }] })
            return null;

        var constant = scene.Model.GetConstantValue(value);
        if (!constant.HasValue || constant.Value == null) return null;
        return constant.Value switch
        {
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            int i when local.Type.SpecialType == SpecialType.System_Double => i.ToString(CultureInfo.InvariantCulture),
            _ => SymbolDisplay.FormatPrimitive(constant.Value, quoteStrings: true, useHexadecimalNumbers: false)
        };
    }

    /// <summary><c>altura_topo</c> e <c>alturaTopo</c> viram <c>AlturaTopo</c>.</summary>
    private static string PascalCase(string name)
    {
        var parts = name.TrimStart('@').Split('_', StringSplitOptions.RemoveEmptyEntries);
        string result = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return result.Length == 0 || !SyntaxFacts.IsValidIdentifier(result) ? "Valor" : result;
    }

    private static string UniqueName(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            if (!taken.Contains(name + i)) return name + i;
        }
    }

    // ---------------------------------------------------------------- Os dois arquivos

    private static string BuildPartFile(
        SceneStatements scene, Slice slice, IReadOnlyList<Parameter> parameters,
        Registration root, string typeName, string sceneFile, string newLine)
    {
        var renames = parameters.ToDictionary(p => (ISymbol)p.Local, p => p.Name, SymbolEqualityComparer.Default);
        var body = new StringBuilder();
        bool first = true;
        foreach (var statement in slice.Statements)
        {
            var (keep, move) = SplitLeadingTrivia(statement);
            if (!first && keep.Length > 0) body.Append(newLine);
            first = false;

            // Comentário no fim da linha (furo.IsTransientTool = true; // ferramenta) vai junto.
            string trailing = statement.GetTrailingTrivia().ToFullString().TrimEnd('\r', '\n');
            string text = move + Renamed(scene, statement, renames) + trailing;
            foreach (var line in Lines(Dedent(text)))
            {
                body.Append(line.Length == 0 ? string.Empty : Indent + line).Append(newLine);
            }
        }

        string parameterList = string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}" + (p.Default != null ? $" = {p.Default}" : string.Empty)));
        string rootName = root.Local.Name;

        return string.Join(newLine,
            "/// <summary>",
            $"/// Convertida do sólido solto <c>{rootName}</c> de {sceneFile}. O Build() continua nas coordenadas",
            "/// em que o sólido foi desenhado na cena.",
            "/// </summary>",
            $"public sealed record {typeName}({parameterList}) : Part",
            "{",
            "    protected override IFeature Build()",
            "    {",
            body.ToString() + $"{Indent}return {rootName};",
            "    }",
            "}",
            string.Empty);
    }

    private static string RewriteScene(
        SceneStatements scene, Slice slice, IReadOnlyList<Parameter> parameters, Registration root, string typeName)
    {
        var changes = new List<TextChange>();
        foreach (var statement in slice.Removed)
        {
            // Linhas em branco soltas acima da instrução saem com ela (senão acumulam onde o código
            // sumiu); as que separam um comentário de seção ficam.
            var (keep, _) = SplitLeadingTrivia(statement);
            int start = string.IsNullOrWhiteSpace(keep) ? statement.FullSpan.Start : statement.FullSpan.Start + keep.Length;
            changes.Add(new TextChange(TextSpan.FromBounds(start, statement.FullSpan.End), string.Empty));
        }

        string arguments = string.Join(", ", parameters.Select(p => $"{p.Name}: {p.Local.Name}"));
        string at = root.Placement != null ? $", at: {root.Placement}" : string.Empty;
        string name = root.Local.Name;
        changes.Add(new TextChange(root.Statement.Span, $"var {name} = Add(new {typeName}({arguments}){at}, name: \"{name}\");"));

        return scene.Tree.GetText().WithChanges(changes.OrderBy(c => c.Span.Start)).ToString();
    }

    /// <summary>
    /// Divide os comentários acima da instrução: o que vem até a última linha em branco fica na
    /// cena (costuma ser o título de uma seção); o que está colado na instrução vai com ela.
    /// </summary>
    private static (string Keep, string Move) SplitLeadingTrivia(StatementSyntax statement)
    {
        string leading = statement.GetLeadingTrivia().ToFullString();
        int keepLength = 0;
        int lineStart = 0;
        for (int i = 0; i < leading.Length; i++)
        {
            if (leading[i] != '\n') continue;
            if (string.IsNullOrWhiteSpace(leading[lineStart..i])) keepLength = i + 1;
            lineStart = i + 1;
        }
        return (leading[..keepLength], leading[keepLength..]);
    }

    /// <summary>O texto da instrução com as variáveis que viraram parâmetro trocadas pelo nome do parâmetro.</summary>
    private static string Renamed(SceneStatements scene, StatementSyntax statement, Dictionary<ISymbol, string> renames)
    {
        var changes = statement.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(id => (id, symbol: scene.SceneSymbolOf(id)))
            .Where(x => x.symbol != null && renames.ContainsKey(x.symbol))
            .Select(x => new TextChange(x.id.Span, renames[x.symbol!]))
            .ToList();

        var text = SourceText.From(statement.ToString());
        int offset = statement.Span.Start;
        return text.WithChanges(changes.Select(c => new TextChange(new TextSpan(c.Span.Start - offset, c.Span.Length), c.NewText!))).ToString();
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    /// <summary>Tira a indentação comum (a cena costuma estar na coluna 0, mas nada garante).</summary>
    private static string Dedent(string text)
    {
        var lines = Lines(text).ToList();
        int common = lines.Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart(' ', '\t').Length)
            .DefaultIfEmpty(0)
            .Min();
        return string.Join("\n", lines.Select(l => l.Trim().Length == 0 ? string.Empty : l[common..]));
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
