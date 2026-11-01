using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaCadEditor.Application.Ai;

public enum EditabilitySeverity
{
    /// <summary>Quebra a regra dos sólidos rastreáveis: o sólido perde a ligação com a linha do código.</summary>
    Error,

    /// <summary>Estilo que atrapalha as ferramentas de edição manual, mas não as impede.</summary>
    Warning
}

/// <param name="RuleId">Id estável da regra (ver as constantes de <see cref="CadScriptEditabilityLint"/>).</param>
/// <param name="Line">Linha 1-based no arquivo.</param>
public sealed record EditabilityFinding(string RuleId, EditabilitySeverity Severity, int Line, string Message)
{
    public override string ToString() => $"linha {Line}: {Message}";
}

/// <summary>Contagens de um arquivo: o "quão editável" ficou o código.</summary>
/// <param name="Solids">Sólidos criados (features, booleanas, cópias nomeadas, sub-peças e instâncias).</param>
/// <param name="TrackableSolids">Dos sólidos, os com nome literal e fora de laço/lambda.</param>
/// <param name="Points">Coordenadas checadas: cada <c>new Point3D(...)</c> e, na cena, cada <c>Placement.At(...)</c>.</param>
/// <param name="EditablePoints">Das coordenadas, as que estão na forma editável (ver <see cref="CadScriptEditabilityLint.IsEditableNumber"/>).</param>
/// <param name="Loops">Laços (<c>for</c>, <c>foreach</c>, <c>while</c>, <c>do</c>) e chamadas LINQ.</param>
/// <param name="LocalFunctions">Funções locais e métodos auxiliares nos records.</param>
/// <param name="MaxPartParameters">Maior número de parâmetros de um record do arquivo (0 sem record).</param>
public sealed record EditabilityMetrics(int Solids, int TrackableSolids, int Points, int EditablePoints, int Loops, int LocalFunctions, int MaxPartParameters)
{
    public static EditabilityMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Fração das coordenadas na forma editável; null sem coordenadas.</summary>
    public double? EditablePointRatio => Points == 0 ? null : (double)EditablePoints / Points;

    public EditabilityMetrics Add(EditabilityMetrics other) => new(
        Solids + other.Solids, TrackableSolids + other.TrackableSolids, Points + other.Points,
        EditablePoints + other.EditablePoints, Loops + other.Loops, LocalFunctions + other.LocalFunctions,
        Math.Max(MaxPartParameters, other.MaxPartParameters));
}

public sealed record EditabilityReport(IReadOnlyList<EditabilityFinding> Findings, EditabilityMetrics Metrics)
{
    public static EditabilityReport Empty { get; } = new(Array.Empty<EditabilityFinding>(), EditabilityMetrics.Empty);

    public IEnumerable<EditabilityFinding> Errors => Findings.Where(f => f.Severity == EditabilitySeverity.Error);
    public IEnumerable<EditabilityFinding> Warnings => Findings.Where(f => f.Severity == EditabilitySeverity.Warning);
    public bool HasErrors => Errors.Any();
    public bool HasWarnings => Warnings.Any();

    /// <summary>
    /// Os achados como lista curta para a IA ou para o terminal: um por linha, cortada em
    /// <paramref name="max"/> itens com um "+N" no fim (uma peça antiga pode ter centenas de avisos).
    /// </summary>
    public static string Format(IEnumerable<EditabilityFinding> findings, int max = 12)
    {
        var list = findings.ToList();
        var sb = new StringBuilder();
        foreach (var finding in list.Take(max))
        {
            sb.Append("- ").AppendLine(finding.ToString());
        }
        if (list.Count > max)
        {
            sb.Append("- (+").Append(list.Count - max).AppendLine(" achados do mesmo tipo)");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Confere se um arquivo de cena ou de peça está escrito do jeito que as ferramentas de edição manual
/// conseguem reescrever (plano <c>docs/planos/2026-11-01-codigo-editavel-por-padrao.md</c>).
/// Puro e sintático: só a árvore do Roslyn, sem compilar nem executar.
/// <para>
/// <b>Erros</b> são a regra dos sólidos rastreáveis (<see cref="CadScriptRules.TrackableSolids"/>): sólido
/// criado em laço ou lambda, nome que não é literal e nome repetido. Sem eles, seleção, árvore, inspector
/// e "revelar no código" perdem o sólido. <b>Avisos</b> são o estilo que faz as ferramentas recusarem a
/// edição ou embrulharem o código em vez de mudar um número: coordenada calculada, parâmetros demais,
/// funções locais, laços de cálculo, atributos e opções <c>bool</c>.
/// </para>
/// </summary>
public static class CadScriptEditabilityLint
{
    public const string SolidInLoop = "solido-em-laco";
    public const string ComputedName = "nome-calculado";
    public const string DuplicateName = "nome-repetido";
    public const string NameDiffersFromVariable = "nome-diferente-da-variavel";
    public const string ComputedCoordinate = "coordenada-calculada";
    public const string TooManyParameters = "parametros-demais";
    public const string LocalFunction = "funcao-local";
    public const string LoopOrLinq = "laco-ou-linq";
    public const string ParameterAttribute = "atributo";
    public const string BoolOption = "opcao-bool";

    /// <summary>Parâmetros de um record além disso viram aviso: só as dimensões principais.</summary>
    public const int MaxParameters = 3;

    private static readonly HashSet<string> BooleanFactories = new(StringComparer.Ordinal)
    {
        "CreateUnion", "CreateDifference", "CreateIntersection"
    };

    private static readonly HashSet<string> LinqMethods = new(StringComparer.Ordinal)
    {
        "Select", "SelectMany", "Where", "Aggregate", "Zip", "Range", "Repeat"
    };

    /// <summary>Um sólido criado no código e onde está o nome dele (null = sem nome).</summary>
    private sealed record SolidCreation(ExpressionSyntax Node, ExpressionSyntax? NameExpression, string Kind);

    public static EditabilityReport Analyze(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return EditabilityReport.Empty;

        // Parse normal (não Script): no modo Script um "var x = ..." solto vira campo, e a cena deixaria de
        // ser reconhecida pelos statements top-level.
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var findings = new List<EditabilityFinding>();

        var solids = FindSolids(root).ToList();
        int trackable = CheckSolids(solids, findings);

        bool isScene = root.DescendantNodes().OfType<GlobalStatementSyntax>().Any();
        var (points, editablePoints) = CheckCoordinates(root, isScene, findings);
        int loops = CheckLoops(root, solids, findings);
        int localFunctions = CheckLocalFunctions(root, findings);
        int maxParameters = CheckRecords(root, findings);

        var ordered = findings
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.Line)
            .ToList();
        return new EditabilityReport(ordered,
            new EditabilityMetrics(solids.Count, trackable, points, editablePoints, loops, localFunctions, maxParameters));
    }

    /// <summary>
    /// A forma editável de um número: um literal (<c>12.5</c>, <c>-3</c>), um identificador
    /// (<c>Largura</c>) ou identificador ± literal (<c>Largura - 10</c>). É o formato em que mover soma o
    /// deslocamento no próprio número (<c>RoslynCoordinateMutator</c>) em vez de pendurar mais um termo.
    /// </summary>
    public static bool IsEditableNumber(ExpressionSyntax expression)
    {
        var e = Unwrap(expression);
        if (IsNumericLiteral(e)) return true;
        if (e is IdentifierNameSyntax) return true;
        return e is BinaryExpressionSyntax binary
               && (binary.IsKind(SyntaxKind.AddExpression) || binary.IsKind(SyntaxKind.SubtractExpression))
               && Unwrap(binary.Left) is IdentifierNameSyntax
               && IsNumericLiteral(Unwrap(binary.Right));
    }

    // ------------------------------------------------------------------ sólidos

    private static IEnumerable<SolidCreation> FindSolids(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ObjectCreationExpressionSyntax creation when SimpleTypeName(creation.Type) is { } type && type.EndsWith("Feature", StringComparison.Ordinal):
                    yield return new SolidCreation(creation, NameOfFeatureCreation(creation, type), type);
                    break;

                case InvocationExpressionSyntax invocation:
                    var solid = SolidFromInvocation(invocation);
                    if (solid != null) yield return solid;
                    break;
            }
        }
    }

    private static SolidCreation? SolidFromInvocation(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        switch (invocation.Expression)
        {
            // BooleanFeature.CreateDifference("nome", ...)
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: var method } member
                when BooleanFactories.Contains(method) && SimpleName(member.Expression) == "BooleanFeature":
                return new SolidCreation(invocation, Argument(args, "name", 0), "BooleanFeature");

            // x.Transformed(placement, name: "nome") — só com nome é um sólido novo; sem nome é mover.
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transformed" }:
                var copyName = Argument(args, "name", 1);
                return copyName == null ? null : new SolidCreation(invocation, copyName, "Transformed");

            // new SubPeca(...).Instance("nome", at: ...)
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Instance" }:
                return new SolidCreation(invocation, Argument(args, "name", 0), "Instance");

            // Add(peca, at, name, color) na cena; sem name: o nome automático vem da variável.
            case IdentifierNameSyntax { Identifier.ValueText: "Add" } when args.Count > 0:
                return new SolidCreation(invocation, Argument(args, "name", 2), "Add");

            default:
                return null;
        }
    }

    private static ExpressionSyntax? NameOfFeatureCreation(ObjectCreationExpressionSyntax creation, string type)
    {
        var fromInitializer = creation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(a => a.Left is IdentifierNameSyntax { Identifier.ValueText: "Name" })
            ?.Right;
        if (fromInitializer != null) return fromInitializer;

        var args = creation.ArgumentList?.Arguments ?? default;
        var named = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "name");
        if (named != null) return named.Expression;

        // FilletFeature(string name, IFeature baseFeature, double radius): o nome é o primeiro.
        if (type == "FilletFeature" && args.Count > 0 && args[0].NameColon == null) return args[0].Expression;

        // Construtor com o nome no fim (string? name = null) passado sem rótulo.
        return args.Where(a => a.NameColon == null).Select(a => a.Expression).FirstOrDefault(IsStringLike);
    }

    /// <summary>Confere nome literal, laço e repetição; devolve quantos sólidos são rastreáveis.</summary>
    private static int CheckSolids(IReadOnlyList<SolidCreation> solids, List<EditabilityFinding> findings)
    {
        int trackable = 0;
        var seen = new Dictionary<(SyntaxNode Scope, string Name), SyntaxNode>();

        foreach (var solid in solids)
        {
            int line = LineOf(solid.Node);
            string? literalName = solid.NameExpression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression)
                ? lit.Token.ValueText
                : null;
            string label = literalName ?? Declarator(solid.Node)?.Identifier.ValueText ?? solid.Kind;

            var repetition = EnclosingRepetition(solid.Node);
            bool inLoop = repetition != null;
            if (repetition != null)
            {
                findings.Add(new(SolidInLoop, EditabilitySeverity.Error, line,
                    $"{label} é criado dentro de {RepetitionLabel(repetition)}: cada sólido precisa da sua própria declaração " +
                    "com nome literal (furo_1, furo_2 ... um por linha); laços e LINQ só podem calcular valores."));
            }

            if (solid.NameExpression != null && literalName == null)
            {
                findings.Add(new(ComputedName, EditabilitySeverity.Error, line,
                    $"o nome de {label} é calculado ({Shorten(solid.NameExpression.ToString())}): use um texto literal igual ao nome da variável, " +
                    "ex.: Name = \"furo_1\"."));
            }

            if (literalName != null)
            {
                var key = (ScopeOf(solid.Node), literalName);
                if (seen.TryGetValue(key, out var first))
                {
                    // if/else (ou retorno antecipado) que cria o mesmo sólido de dois jeitos: só um existe por execução.
                    if (!MutuallyExclusive(first, solid.Node))
                    {
                        findings.Add(new(DuplicateName, EditabilitySeverity.Error, line,
                            $"o nome \"{literalName}\" já foi usado na linha {LineOf(first)}: cada sólido precisa de um nome único no arquivo."));
                    }
                }
                else
                {
                    seen[key] = solid.Node;
                }

                var declarator = Declarator(solid.Node);
                if (declarator != null && declarator.Identifier.ValueText != literalName)
                {
                    findings.Add(new(NameDiffersFromVariable, EditabilitySeverity.Warning, line,
                        $"a variável {declarator.Identifier.ValueText} cria o sólido \"{literalName}\": use o mesmo nome nos dois " +
                        $"(var {literalName} = ... {{ Name = \"{literalName}\" }})."));
                }
            }

            if (literalName != null && !inLoop) trackable++;
        }

        return trackable;
    }

    /// <summary>
    /// A variável que recebe o sólido: <c>var x = &lt;sólido&gt;</c>, atravessando um <c>.Transformed(...)</c>
    /// sem nome (mover) e parênteses. Null quando o sólido é argumento de outra coisa.
    /// </summary>
    private static VariableDeclaratorSyntax? Declarator(SyntaxNode node)
    {
        SyntaxNode current = node;
        while (true)
        {
            var parent = current.Parent;
            switch (parent)
            {
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }:
                    return declarator;
                case ParenthesizedExpressionSyntax:
                    current = parent;
                    continue;
                case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transformed" } member
                    when member.Expression == current && member.Parent is InvocationExpressionSyntax inv && Argument(inv.ArgumentList.Arguments, "name", 1) == null:
                    current = inv;
                    continue;
                default:
                    return null;
            }
        }
    }

    // ------------------------------------------------------------------ coordenadas

    private static (int Points, int Editable) CheckCoordinates(SyntaxNode root, bool isScene, List<EditabilityFinding> findings)
    {
        int points = 0, editable = 0;

        foreach (var node in root.DescendantNodes())
        {
            IReadOnlyList<ExpressionSyntax>? coordinates = node switch
            {
                ObjectCreationExpressionSyntax creation when SimpleTypeName(creation.Type) == "Point3D" =>
                    creation.ArgumentList?.Arguments.Select(a => a.Expression).ToList(),

                // Placement.At(x, y, z, rotação...) na cena: só as três coordenadas.
                InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "At" } member } invocation
                    when isScene && SimpleName(member.Expression) == "Placement" && invocation.ArgumentList.Arguments.Count >= 3 =>
                    invocation.ArgumentList.Arguments.Take(3).Where(a => a.NameColon == null).Select(a => a.Expression).ToList(),

                _ => null
            };
            if (coordinates == null || coordinates.Count == 0) continue;

            // p2.World(p => new Point3D(p.Largura, 0, 0)) é a forma ensinada de converter um ponto local: não conta.
            if (node.Ancestors().Any(a => a is AnonymousFunctionExpressionSyntax)) continue;

            points++;
            var computed = coordinates.Where(c => !IsEditableNumber(c)).ToList();
            if (computed.Count == 0)
            {
                editable++;
                continue;
            }

            findings.Add(new(ComputedCoordinate, EditabilitySeverity.Warning, LineOf(node),
                $"coordenada calculada ({Shorten(string.Join(", ", computed.Select(c => c.ToString())))}): escreva o número " +
                "(ou Parametro ± número) para que mover e o inspector consigam editá-la."));
        }

        return (points, editable);
    }

    // ------------------------------------------------------------------ laços, funções e records

    private static int CheckLoops(SyntaxNode root, IReadOnlyList<SolidCreation> solids, List<EditabilityFinding> findings)
    {
        int count = 0;
        var solidNodes = solids.Select(s => s.Node).ToList();

        foreach (var node in root.DescendantNodes())
        {
            bool isLoop = node is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax;
            bool isLinq = node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var method } }
                          && LinqMethods.Contains(method);
            if (!isLoop && !isLinq) continue;

            count++;
            // Laço que cria sólido já virou erro (solido-em-laco); o aviso é para o laço que só calcula.
            if (solidNodes.Any(s => node.Span.Contains(s.Span))) continue;

            findings.Add(new(LoopOrLinq, EditabilitySeverity.Warning, LineOf(node),
                (isLoop ? "laço" : $"LINQ ({((MemberAccessExpressionSyntax)((InvocationExpressionSyntax)node).Expression).Name})") +
                " sem pedido explícito: escreva os valores um a um, a menos que o pedido peça repetição ou código paramétrico."));
        }

        return count;
    }

    private static int CheckLocalFunctions(SyntaxNode root, List<EditabilityFinding> findings)
    {
        int count = 0;

        foreach (var function in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            count++;
            findings.Add(new(LocalFunction, EditabilitySeverity.Warning, LineOf(function),
                $"função local {function.Identifier.ValueText}: a seleção e o rastreio param nela; escreva os valores direto, " +
                "a menos que o pedido peça código paramétrico."));
        }

        // Métodos auxiliares num record de peça (além do Build e de outros overrides).
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Parent is not RecordDeclarationSyntax) continue;
            if (method.Modifiers.Any(SyntaxKind.OverrideKeyword)) continue;

            count++;
            findings.Add(new(LocalFunction, EditabilitySeverity.Warning, LineOf(method),
                $"método auxiliar {method.Identifier.ValueText}: escreva os valores direto no Build(), a menos que o pedido peça código paramétrico."));
        }

        return count;
    }

    /// <summary>Parâmetros, atributos e opções <c>bool</c> dos records; devolve o maior número de parâmetros.</summary>
    private static int CheckRecords(SyntaxNode root, List<EditabilityFinding> findings)
    {
        int max = 0;

        foreach (var record in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
        {
            var parameters = record.ParameterList?.Parameters ?? default;
            max = Math.Max(max, parameters.Count);

            if (parameters.Count > MaxParameters)
            {
                findings.Add(new(TooManyParameters, EditabilitySeverity.Warning, LineOf(record),
                    $"{record.Identifier.ValueText} tem {parameters.Count} parâmetros: deixe no máximo {MaxParameters} (as dimensões " +
                    "principais) e escreva as outras medidas como números no Build()."));
            }

            foreach (var parameter in parameters)
            {
                if (parameter.AttributeLists.Count > 0)
                {
                    findings.Add(new(ParameterAttribute, EditabilitySeverity.Warning, LineOf(parameter),
                        $"atributo no parâmetro {parameter.Identifier.ValueText}: só use [Range] e afins se o pedido pedir."));
                }

                if (parameter.Type is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.BoolKeyword))
                {
                    findings.Add(new(BoolOption, EditabilitySeverity.Warning, LineOf(parameter),
                        $"opção bool {parameter.Identifier.ValueText}: crie a variante com 'with' ou uma peça própria, " +
                        "a menos que o pedido peça a opção."));
                }
            }
        }

        return max;
    }

    // ------------------------------------------------------------------ utilitários

    /// <summary>O laço ou lambda mais próximo que repete a criação (dentro do mesmo método/função local).</summary>
    private static SyntaxNode? EnclosingRepetition(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax:
                case AnonymousFunctionExpressionSyntax:
                    return ancestor;
                case MemberDeclarationSyntax or LocalFunctionStatementSyntax or CompilationUnitSyntax:
                    return null;
            }
        }
        return null;
    }

    /// <summary>
    /// True se um dos dois está num ramo (then/else de um if, case de um switch, lado de um ?:) que não
    /// contém o outro: nesse caso no máximo um deles roda, como em <c>if (...) return X("a"); return Y("a");</c>.
    /// Pode deixar passar uma repetição de verdade, mas nunca acusa uma que não existe.
    /// </summary>
    private static bool MutuallyExclusive(SyntaxNode a, SyntaxNode b) =>
        Branches(a).Any(branch => !branch.Span.Contains(b.Span)) || Branches(b).Any(branch => !branch.Span.Contains(a.Span));

    private static IEnumerable<SyntaxNode> Branches(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            if (ancestor is MemberDeclarationSyntax and not GlobalStatementSyntax) yield break;

            bool isBranch = ancestor.Parent switch
            {
                IfStatementSyntax ifStatement => ifStatement.Statement == ancestor,
                ElseClauseSyntax => true,
                SwitchSectionSyntax => true,
                SwitchExpressionArmSyntax => true,
                ConditionalExpressionSyntax conditional => conditional.WhenTrue == ancestor || conditional.WhenFalse == ancestor,
                _ => false
            };
            if (isBranch) yield return ancestor;
        }
    }

    private static string RepetitionLabel(SyntaxNode repetition) => repetition switch
    {
        ForStatementSyntax => "um laço for",
        ForEachStatementSyntax => "um laço foreach",
        WhileStatementSyntax or DoStatementSyntax => "um laço while",
        _ => "uma lambda/LINQ"
    };

    /// <summary>Escopo onde os nomes precisam ser únicos: o record (peça) ou o arquivo (cena).</summary>
    private static SyntaxNode ScopeOf(SyntaxNode node) =>
        (SyntaxNode?)node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() ?? node.SyntaxTree.GetRoot();

    private static ExpressionSyntax? Argument(SeparatedSyntaxList<ArgumentSyntax> args, string name, int position)
    {
        var named = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name);
        if (named != null) return named.Expression;

        // Posicional só vale enquanto nenhum argumento antes dele foi passado por nome.
        if (position < args.Count && args.Take(position + 1).All(a => a.NameColon == null)) return args[position].Expression;
        return null;
    }

    private static bool IsStringLike(ExpressionSyntax e) =>
        e is InterpolatedStringExpressionSyntax
        || (e is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression))
        || (e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression) && (IsStringLike(b.Left) || IsStringLike(b.Right)));

    private static bool IsNumericLiteral(ExpressionSyntax e) =>
        (e is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.NumericLiteralExpression))
        || (e is PrefixUnaryExpressionSyntax p && (p.IsKind(SyntaxKind.UnaryMinusExpression) || p.IsKind(SyntaxKind.UnaryPlusExpression))
            && Unwrap(p.Operand) is LiteralExpressionSyntax inner && inner.IsKind(SyntaxKind.NumericLiteralExpression));

    private static ExpressionSyntax Unwrap(ExpressionSyntax e)
    {
        while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
        return e;
    }

    private static string? SimpleTypeName(TypeSyntax type) => type switch
    {
        SimpleNameSyntax s => s.Identifier.ValueText,
        QualifiedNameSyntax q => q.Right.Identifier.ValueText,
        _ => null
    };

    private static string? SimpleName(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        _ => null
    };

    private static int LineOf(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string Shorten(string text)
    {
        string single = string.Join(' ', text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        return single.Length <= 60 ? single : single[..57] + "...";
    }
}
