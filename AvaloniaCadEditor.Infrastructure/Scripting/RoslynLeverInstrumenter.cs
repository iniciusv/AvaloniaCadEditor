using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Levers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

/// <summary>
/// Reescreve a cena, só em memória, para que os números que mudam uma peça possam ser trocados sem
/// recompilar: cada literal vira <c>global::__CadLevers.L(id, literal)</c>, que devolve o valor trocado
/// quando há um e o próprio literal quando não há. Três fontes de números (ver <see cref="CodeLeverKind"/>):
/// <list type="bullet">
///   <item>os literais do corpo do record da peça;</item>
///   <item>os literais nos argumentos de <c>new Peca(...)</c> nos arquivos de entrada (a cena);</item>
///   <item>os valores padrão dos parâmetros do record. O padrão é constante e não pode virar chamada, então
///   cada <c>new Peca(...)</c> que não passa o parâmetro ganha <c>Parametro: L(id, padrão)</c>.</item>
/// </list>
/// Só entram literais que são <c>double</c> depois da conversão: contagens <c>int</c> e divisões inteiras
/// mudariam de tipo. Ficam de fora atributos, padrões de <c>switch</c> e padrões de parâmetros de métodos.
/// <para>
/// <b><c>const</c>.</b> Uma medida com nome (<c>const double raioFuro = 1.7;</c>) é o número que mais
/// vale a pena puxar, e um <c>const</c> não pode receber uma chamada. Na cópia instrumentada, o
/// <c>const double</c> do record deixa de ser <c>const</c> (o local vira variável; o campo privado vira
/// propriedade estática, para ser lido a cada execução) sempre que nenhum uso dele exige constante
/// (atributo, <c>case</c>, padrão de parâmetro, outro <c>const</c> que continua <c>const</c>). O fonte de
/// verdade não muda: gravar troca só o literal, e o <c>const</c> continua lá.
/// </para>
/// <para>
/// Cada número sai com o papel que tem na conta (<see cref="CodeLeverRole"/>) e, quando é a coordenada de
/// um ponto de uma lista, com o lugar dele nela (<see cref="LeverVertex"/>).
/// </para>
/// Puro: recebe a compilação e devolve outra, sem executar nem gravar.
/// </summary>
public static class RoslynLeverInstrumenter
{
    /// <summary>Classe gerada que guarda os valores trocados (ver <see cref="HelperSource"/>).</summary>
    public const string HelperClass = "__CadLevers";

    /// <summary>
    /// Os valores trocados ficam num campo estático da cena compilada para a sonda: o assembly é só dela, e
    /// ela executa uma vez por vez.
    /// </summary>
    public const string HelperSource =
        "public static class " + HelperClass + "\n" +
        "{\n" +
        "    public static readonly System.Collections.Generic.Dictionary<int, double> Values = new System.Collections.Generic.Dictionary<int, double>();\n" +
        "    public static double L(int id, double value) => Values.TryGetValue(id, out var v) ? v : value;\n" +
        "}\n";

    private const int MaxPathDepth = 3;

    /// <summary>
    /// Parâmetros <c>Vector3D</c> que são um deslocamento, e não uma direção: os componentes deles são
    /// medidas (<c>Placement.Translate(new Vector3D(10, 0, 0))</c>).
    /// </summary>
    private static readonly HashSet<string> DisplacementParameters =
        new(StringComparer.OrdinalIgnoreCase) { "translation", "offset", "vector", "delta", "deslocamento" };

    /// <summary>A compilação instrumentada e os números que ela sabe trocar.</summary>
    /// <param name="FreedConstants">Quantas declarações <c>const</c> deixaram de ser <c>const</c> na cópia. Se
    /// a compilação instrumentada não emitir e este número não for zero, vale tentar de novo sem isso.</param>
    public sealed record Result(CSharpCompilation Compilation, IReadOnlyList<CodeLever> Levers, int FreedConstants = 0);

    /// <param name="freeConstants">False deixa todo <c>const</c> como está (e os literais dele de fora).</param>
    /// <returns><c>null</c> se o record <paramref name="partTypeName"/> não está na cena.</returns>
    public static Result? Instrument(
        CSharpCompilation compilation,
        IReadOnlyList<SyntaxTree> userTrees,
        string partTypeName,
        CancellationToken cancellationToken = default,
        bool freeConstants = true)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(userTrees);
        if (string.IsNullOrEmpty(partTypeName)) return null;

        var found = FindRecord(compilation, userTrees, partTypeName, cancellationToken);
        if (found == null) return null;
        var (recordTree, record, recordSymbol) = found.Value;

        var builder = new Builder(compilation);
        var recordModel = compilation.GetSemanticModel(recordTree);

        // 0. Os const que podem deixar de ser const na cópia: os literais deles passam a ser candidatos.
        var freed = freeConstants
            ? FreeableConstants(record, recordModel, cancellationToken)
            : new HashSet<SyntaxNode>();
        foreach (var declaration in freed) builder.FreeConstant(recordTree, declaration);

        // 1. Literais do corpo do record.
        foreach (var member in record.Members)
        {
            foreach (var expression in Candidates(member, freed))
            {
                if (OutsideRecord(expression, record)) continue;
                builder.AddLiteral(recordTree, recordModel, expression, CodeLeverKind.Literal, fallbackOwner: MemberName(expression) ?? partTypeName);
            }
        }

        // 2. Valores padrão dos parâmetros.
        var defaults = new List<(string Name, int Id, string Text)>();
        foreach (var parameter in record.ParameterList?.Parameters ?? default)
        {
            if (parameter.Default?.Value is not { } value || !IsNumericLiteral(value)) continue;
            if (recordModel.GetDeclaredSymbol(parameter, cancellationToken) is not IParameterSymbol symbol ||
                symbol.Type.SpecialType != SpecialType.System_Double) continue;

            var lever = builder.AddLever(recordTree, recordModel, value, CodeLeverKind.Default, owner: parameter.Identifier.ValueText, path: "");
            if (lever != null) defaults.Add((parameter.Identifier.ValueText, lever.Id, lever.Text));
        }

        // 3. Criações da peça: argumentos (só na cena) e padrões que faltam (em todo lugar).
        foreach (var tree in userTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            bool isEntry = IsEntry(tree, cancellationToken);
            foreach (var creation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if (creation.ArgumentList == null) continue;
                if (!SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(creation, cancellationToken).Type, recordSymbol)) continue;

                if (isEntry)
                {
                    string owner = DeclaredName(creation) ?? partTypeName;
                    foreach (var expression in Candidates(creation.ArgumentList, freed))
                    {
                        builder.AddLiteral(tree, model, expression, CodeLeverKind.Argument, fallbackOwner: owner);
                    }
                }

                builder.AddMissingDefaults(tree, model, creation, defaults);
            }
        }

        return new Result(builder.Build(userTrees), builder.Levers, freed.Count);
    }

    private static (SyntaxTree Tree, RecordDeclarationSyntax Record, INamedTypeSymbol Symbol)? FindRecord(
        CSharpCompilation compilation, IReadOnlyList<SyntaxTree> trees, string name, CancellationToken cancellationToken)
    {
        foreach (var tree in trees)
        {
            var record = tree.GetRoot(cancellationToken).DescendantNodes()
                .OfType<RecordDeclarationSyntax>()
                .FirstOrDefault(r => r.Identifier.ValueText == name);
            if (record == null) continue;

            var symbol = compilation.GetSemanticModel(tree).GetDeclaredSymbol(record, cancellationToken);
            return symbol == null ? null : (tree, record, symbol);
        }
        return null;
    }

    private static bool IsEntry(SyntaxTree tree, CancellationToken cancellationToken) =>
        ((CompilationUnitSyntax)tree.GetRoot(cancellationToken)).Members.OfType<GlobalStatementSyntax>().Any();

    // ------------------------------------------------------------------ const

    /// <summary>
    /// As declarações <c>const double</c> do record (locais e campos privados) que podem deixar de ser
    /// <c>const</c> na cópia instrumentada. Fica <c>const</c> a que é usada onde o C# exige constante, e,
    /// por tabela, toda a que entra na conta de outra que ficou <c>const</c>.
    /// </summary>
    private static HashSet<SyntaxNode> FreeableConstants(RecordDeclarationSyntax record, SemanticModel model, CancellationToken cancellationToken)
    {
        // Toda declaração const do record, com os símbolos que declara.
        var declarations = new Dictionary<SyntaxNode, List<ISymbol>>();
        var owner = new Dictionary<ISymbol, SyntaxNode>(SymbolEqualityComparer.Default);
        foreach (var node in record.DescendantNodes(n => n == record || n is not TypeDeclarationSyntax))
        {
            var variables = node switch
            {
                LocalDeclarationStatementSyntax { IsConst: true } local => local.Declaration.Variables,
                FieldDeclarationSyntax field when field.Modifiers.Any(SyntaxKind.ConstKeyword) => field.Declaration.Variables,
                _ => default
            };
            if (variables.Count == 0) continue;

            var symbols = new List<ISymbol>();
            foreach (var variable in variables)
            {
                if (model.GetDeclaredSymbol(variable, cancellationToken) is { } symbol)
                {
                    symbols.Add(symbol);
                    owner[symbol] = node;
                }
            }
            declarations[node] = symbols;
        }
        if (declarations.Count == 0) return new HashSet<SyntaxNode>();

        // Candidatas: só double; o campo, só privado e sozinho na declaração (vira propriedade).
        var kept = new HashSet<SyntaxNode>(declarations.Keys.Where(d => !CanBeFreed(d, declarations[d])));

        // Quem usa cada const, e onde.
        var uses = new List<(SyntaxNode Declaration, SyntaxNode Use)>();
        var names = new HashSet<string>(owner.Keys.Select(s => s.Name), StringComparer.Ordinal);
        foreach (var identifier in record.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!names.Contains(identifier.Identifier.ValueText)) continue;
            if (model.GetSymbolInfo(identifier, cancellationToken).Symbol is { } symbol && owner.TryGetValue(symbol, out var declaration))
            {
                uses.Add((declaration, identifier));
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (declaration, use) in uses)
            {
                if (kept.Contains(declaration)) continue;
                if (NeedsConstant(use, declarations, kept))
                {
                    kept.Add(declaration);
                    changed = true;
                }
            }
        }

        return new HashSet<SyntaxNode>(declarations.Keys.Where(d => !kept.Contains(d)));
    }

    private static bool CanBeFreed(SyntaxNode declaration, IReadOnlyList<ISymbol> symbols)
    {
        if (symbols.Count == 0) return false;
        bool allDouble = symbols.All(s => s switch
        {
            ILocalSymbol local => local.Type.SpecialType == SpecialType.System_Double,
            IFieldSymbol field => field.Type.SpecialType == SpecialType.System_Double,
            _ => false
        });
        if (!allDouble) return false;

        return declaration switch
        {
            LocalDeclarationStatementSyntax => true,
            FieldDeclarationSyntax field =>
                field.Declaration.Variables.Count == 1 &&
                field.Declaration.Variables[0].Initializer != null &&
                symbols[0].DeclaredAccessibility == Accessibility.Private,
            _ => false
        };
    }

    /// <summary>O uso está num lugar onde o C# exige constante, contando os <c>const</c> que continuam <c>const</c>.</summary>
    private static bool NeedsConstant(SyntaxNode use, IReadOnlyDictionary<SyntaxNode, List<ISymbol>> declarations, HashSet<SyntaxNode> kept)
    {
        foreach (var ancestor in use.Ancestors())
        {
            if (ancestor is AttributeSyntax or ParameterSyntax or CaseSwitchLabelSyntax or PatternSyntax or EnumMemberDeclarationSyntax or GotoStatementSyntax)
            {
                return true;
            }
            if (declarations.ContainsKey(ancestor)) return kept.Contains(ancestor);
            if (ancestor is TypeDeclarationSyntax) return false;
        }
        return false;
    }

    // ------------------------------------------------------------------ candidatos

    /// <summary>Literais numéricos sob <paramref name="node"/>, já com o sinal (<c>-5</c> é um número só).</summary>
    private static IEnumerable<ExpressionSyntax> Candidates(SyntaxNode node, HashSet<SyntaxNode> freed) =>
        node.DescendantNodes(n => n is not AttributeListSyntax)
            .OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.NumericLiteralExpression))
            .Select(l => l.Parent is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression)
                ? (ExpressionSyntax)unary
                : l)
            .Where(e => !IsExcluded(e, freed));

    private static bool IsNumericLiteral(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression) ||
        expression is PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax inner } unary &&
        unary.IsKind(SyntaxKind.UnaryMinusExpression) && inner.IsKind(SyntaxKind.NumericLiteralExpression);

    /// <summary>Lugares onde o C# exige constante: trocar o literal por uma chamada não compilaria. O
    /// <c>const</c> que deixou de ser <c>const</c> na cópia (<paramref name="freed"/>) não conta.</summary>
    private static bool IsExcluded(ExpressionSyntax expression, HashSet<SyntaxNode> freed) =>
        expression.Ancestors().Any(a =>
            a is AttributeSyntax or ParameterSyntax or CaseSwitchLabelSyntax or PatternSyntax or EnumMemberDeclarationSyntax ||
            a is LocalDeclarationStatementSyntax { IsConst: true } && !freed.Contains(a) ||
            a is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword) && !freed.Contains(a));

    /// <summary>Literal de um tipo aninhado dentro do record: não é da peça.</summary>
    private static bool OutsideRecord(SyntaxNode node, RecordDeclarationSyntax record) =>
        node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() != record;

    /// <summary>A variável que recebe a expressão (<c>var furo_a = ...</c>, <c>furo = ...</c>).</summary>
    private static string? DeclaredName(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case VariableDeclaratorSyntax declarator:
                    return declarator.Identifier.ValueText;
                case AssignmentExpressionSyntax { Left: IdentifierNameSyntax target }:
                    return target.Identifier.ValueText;
                case StatementSyntax or MemberDeclarationSyntax:
                    return null;
            }
        }
        return null;
    }

    private static string? MemberName(SyntaxNode node) =>
        node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault() switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            _ => null
        };

    // ------------------------------------------------------------------ o que o número é

    /// <summary>Sobe por contas, parênteses, sinais e conversões: a expressão inteira em que o número está.</summary>
    private static SyntaxNode Operand(SyntaxNode node)
    {
        while (node.Parent is BinaryExpressionSyntax or ParenthesizedExpressionSyntax or PrefixUnaryExpressionSyntax or CastExpressionSyntax)
        {
            node = node.Parent;
        }
        return node;
    }

    /// <summary>O nome do parâmetro que um argumento preenche: o escrito (<c>at:</c>) ou o da posição.</summary>
    private static string? ParameterName(SemanticModel model, ArgumentSyntax argument)
    {
        if (argument.NameColon?.Name.Identifier.ValueText is { } written) return written;
        if (argument.Parent is not ArgumentListSyntax list || list.Parent == null) return null;
        if (model.GetSymbolInfo(list.Parent).Symbol is not IMethodSymbol method || method.Parameters.Length == 0) return null;

        int index = list.Arguments.IndexOf(argument);
        return index < 0 ? null : method.Parameters[Math.Min(index, method.Parameters.Length - 1)].Name;
    }

    /// <summary>
    /// O que o número preenche, do mais externo ao mais interno: em
    /// <c>new CylinderFeature(new Point3D(10, 5, 0), ...)</c>, o 10 é <c>center.x</c>.
    /// </summary>
    private static string PathOf(SemanticModel model, ExpressionSyntax expression)
    {
        var parts = new List<string>();
        SyntaxNode current = expression;
        while (parts.Count < MaxPathDepth)
        {
            current = Operand(current);
            if (current.Parent is not ArgumentSyntax argument || argument.Parent is not ArgumentListSyntax list) break;
            if (list.Parent is not (BaseObjectCreationExpressionSyntax or InvocationExpressionSyntax)) break;

            var name = ParameterName(model, argument);
            if (name == null) break;

            parts.Insert(0, name);
            current = list.Parent;

            // A peça é o dono natural: o "part" do Add(new Bloco(50)) não diz nada.
            if (current is BaseObjectCreationExpressionSyntax creation && IsPart(model.GetTypeInfo(creation).Type)) break;
        }
        return string.Join(".", parts);
    }

    /// <summary>
    /// O papel do número na conta. Divisor: em qualquer nível à direita de um <c>/</c> ou <c>%</c>.
    /// Direção: componente de um <c>new Vector3D(...)</c> passado a um parâmetro que não é um deslocamento.
    /// O multiplicador não se decide aqui: <c>16 * sx</c> (medida vezes sinal) e <c>2 * raio</c> (fator vezes
    /// medida) têm a mesma sintaxe, e quem os separa é a resposta da peça (<c>LeverCaveat.Scaled</c>).
    /// </summary>
    private static CodeLeverRole RoleOf(SemanticModel model, ExpressionSyntax expression)
    {
        SyntaxNode current = expression;
        while (current.Parent is BinaryExpressionSyntax or ParenthesizedExpressionSyntax or PrefixUnaryExpressionSyntax or CastExpressionSyntax)
        {
            if (current.Parent is BinaryExpressionSyntax binary && binary.Right == current &&
                (binary.IsKind(SyntaxKind.DivideExpression) || binary.IsKind(SyntaxKind.ModuloExpression)))
            {
                return CodeLeverRole.Factor;
            }
            current = current.Parent;
        }

        if (current.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: BaseObjectCreationExpressionSyntax vector } } &&
            IsNamed(model.GetTypeInfo(vector).Type, "Vector3D", "AvaloniaCadEditor.Domain.ValueObjects") &&
            vector.Parent is ArgumentSyntax target &&
            ParameterName(model, target) is { } parameter &&
            !DisplacementParameters.Contains(parameter))
        {
            return CodeLeverRole.Direction;
        }

        return CodeLeverRole.Measure;
    }

    private static bool IsPart(ITypeSymbol? type)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (IsNamed(t, "Part", "AvaloniaCadEditor.Domain.Features")) return true;
        }
        return false;
    }

    private static bool IsNamed(ITypeSymbol? type, string name, string @namespace) =>
        type != null && type.Name == name && type.ContainingNamespace?.ToDisplayString() == @namespace;

    /// <summary>Acumula as alavancas e as mudanças de texto de cada árvore.</summary>
    private sealed class Builder(CSharpCompilation compilation)
    {
        private readonly Dictionary<SyntaxTree, List<TextChange>> _changes = new();
        private readonly HashSet<(SyntaxTree, TextSpan)> _taken = new();
        private readonly List<CodeLever> _levers = new();
        private readonly Dictionary<SyntaxNode, int> _lists = new();

        public IReadOnlyList<CodeLever> Levers => _levers;

        /// <summary>Um literal que vira <c>L(id, literal)</c> no próprio lugar.</summary>
        public void AddLiteral(SyntaxTree tree, SemanticModel model, ExpressionSyntax expression, CodeLeverKind kind, string fallbackOwner)
        {
            if (_taken.Contains((tree, expression.Span))) return;
            var lever = AddLever(tree, model, expression, kind, DeclaredName(expression) ?? fallbackOwner, PathOf(model, expression));
            if (lever == null) return;

            _taken.Add((tree, expression.Span));
            Change(tree, new TextChange(expression.Span, Call(lever.Id, lever.Text)));
        }

        /// <summary>A alavanca de um literal <c>double</c>, sem tocar no texto; <c>null</c> se não for <c>double</c>.</summary>
        public CodeLever? AddLever(SyntaxTree tree, SemanticModel model, ExpressionSyntax expression, CodeLeverKind kind, string owner, string path)
        {
            if (model.GetTypeInfo(expression).ConvertedType?.SpecialType != SpecialType.System_Double) return null;
            var constant = model.GetConstantValue(expression);
            if (!constant.HasValue || constant.Value is not IConvertible convertible) return null;

            var lever = new CodeLever(
                _levers.Count, kind, ToLocation(tree, expression.Span), tree.GetText().ToString(expression.Span),
                convertible.ToDouble(CultureInfo.InvariantCulture), owner, path,
                RoleOf(model, expression), VertexOf(model, expression));
            _levers.Add(lever);
            return lever;
        }

        /// <summary>
        /// O lugar do número numa lista de pontos: <c>new[] { new Point3D(16, -24), new Point3D(16, 24) }</c>.
        /// Só o argumento direto do ponto conta (com a conta em volta, como <c>16 * sx</c>).
        /// </summary>
        private LeverVertex? VertexOf(SemanticModel model, ExpressionSyntax expression)
        {
            var coordinate = Operand(expression);
            if (coordinate.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: BaseObjectCreationExpressionSyntax point } }) return null;
            if (!IsNamed(model.GetTypeInfo(point).Type, "Point3D", "AvaloniaCadEditor.Domain.ValueObjects")) return null;

            (SyntaxNode? List, int Index, int Count) place = point.Parent switch
            {
                InitializerExpressionSyntax initializer => (initializer, initializer.Expressions.IndexOf(point), initializer.Expressions.Count),
                ExpressionElementSyntax { Parent: CollectionExpressionSyntax collection } element =>
                    (collection, collection.Elements.IndexOf(element), collection.Elements.Count),
                _ => (null, -1, 0)
            };
            if (place.List is not { } list || place.Index < 0 || place.Count < 2) return null;
            int index = place.Index, count = place.Count;

            if (!_lists.TryGetValue(list, out int id)) _lists[list] = id = _lists.Count;
            string text = string.Concat(coordinate.ToString().Where(c => !char.IsWhiteSpace(c)));
            return new LeverVertex(id, index, count, text);
        }

        /// <summary>Acrescenta à criação os parâmetros com padrão que ela não passa, lendo de <c>L(id, padrão)</c>.</summary>
        public void AddMissingDefaults(SyntaxTree tree, SemanticModel model, BaseObjectCreationExpressionSyntax creation,
            IReadOnlyList<(string Name, int Id, string Text)> defaults)
        {
            if (defaults.Count == 0 || model.GetSymbolInfo(creation).Symbol is not IMethodSymbol constructor) return;

            var arguments = creation.ArgumentList!.Arguments;
            var supplied = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arguments.Count; i++)
            {
                var name = arguments[i].NameColon?.Name.Identifier.ValueText
                    ?? (i < constructor.Parameters.Length ? constructor.Parameters[i].Name : null);
                if (name != null) supplied.Add(name);
            }

            var missing = defaults
                .Where(d => !supplied.Contains(d.Name) && constructor.Parameters.Any(p => p.Name == d.Name))
                .Select(d => $"{d.Name}: {Call(d.Id, d.Text)}")
                .ToList();
            if (missing.Count == 0) return;

            string text = (arguments.Count > 0 ? ", " : "") + string.Join(", ", missing);
            Change(tree, new TextChange(new TextSpan(creation.ArgumentList.CloseParenToken.SpanStart, 0), text));
        }

        /// <summary>
        /// Tira o <c>const</c> de uma declaração, só na cópia. O local vira variável comum. O campo vira
        /// propriedade estática (<c>const double X = 1;</c> → <c>static double X => 1;</c>): um campo
        /// estático seria calculado uma vez só, na primeira execução da sonda, e ignoraria as trocas seguintes.
        /// </summary>
        public void FreeConstant(SyntaxTree tree, SyntaxNode declaration)
        {
            switch (declaration)
            {
                case LocalDeclarationStatementSyntax local:
                    Change(tree, new TextChange(local.Modifiers.First(m => m.IsKind(SyntaxKind.ConstKeyword)).Span, ""));
                    break;

                case FieldDeclarationSyntax field:
                    Change(tree, new TextChange(field.Modifiers.First(m => m.IsKind(SyntaxKind.ConstKeyword)).Span, "static"));
                    Change(tree, new TextChange(field.Declaration.Variables[0].Initializer!.EqualsToken.Span, "=>"));
                    break;
            }
        }

        public CSharpCompilation Build(IReadOnlyList<SyntaxTree> userTrees)
        {
            var result = compilation;
            foreach (var (tree, changes) in _changes)
            {
                var text = tree.GetText().WithChanges(changes.OrderBy(c => c.Span.Start).ThenBy(c => c.Span.Length));
                result = result.ReplaceSyntaxTree(tree, tree.WithChangedText(text));
            }

            var options = userTrees.Count > 0 ? (CSharpParseOptions)userTrees[0].Options : CSharpParseOptions.Default;
            return result.AddSyntaxTrees(CSharpSyntaxTree.ParseText(HelperSource, options, path: HelperClass + ".g.cs"));
        }

        private void Change(SyntaxTree tree, TextChange change)
        {
            if (!_changes.TryGetValue(tree, out var list)) _changes[tree] = list = new List<TextChange>();
            list.Add(change);
        }

        /// <summary>Entre parênteses: dentro de <c>$"{x:0.#}"</c> o <c>::</c> de <c>global::</c> sem eles seria lido
        /// como o início do formato.</summary>
        private static string Call(int id, string literal) => $"(global::{HelperClass}.L({id}, {literal}))";
    }

    private static CodeLocation ToLocation(SyntaxTree tree, TextSpan span)
    {
        var text = tree.GetText();
        var position = text.Lines.GetLinePosition(span.Start);
        string lineText = text.Lines[position.Line].ToString().Trim();
        return new CodeLocation(tree.FilePath, span.Start, span.Length, position.Line + 1, position.Character + 1, lineText);
    }
}
