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
/// mudariam de tipo. Ficam de fora <c>const</c>, atributos, padrões de <c>switch</c> e padrões de parâmetros
/// de métodos.
///
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

    /// <summary>A compilação instrumentada e os números que ela sabe trocar.</summary>
    public sealed record Result(CSharpCompilation Compilation, IReadOnlyList<CodeLever> Levers);

    /// <returns><c>null</c> se o record <paramref name="partTypeName"/> não está na cena.</returns>
    public static Result? Instrument(
        CSharpCompilation compilation,
        IReadOnlyList<SyntaxTree> userTrees,
        string partTypeName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(userTrees);
        if (string.IsNullOrEmpty(partTypeName)) return null;

        var found = FindRecord(compilation, userTrees, partTypeName, cancellationToken);
        if (found == null) return null;
        var (recordTree, record, recordSymbol) = found.Value;

        var builder = new Builder(compilation);

        // 1. Literais do corpo do record.
        var recordModel = compilation.GetSemanticModel(recordTree);
        foreach (var member in record.Members)
        {
            foreach (var expression in Candidates(member))
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
                    foreach (var expression in Candidates(creation.ArgumentList))
                    {
                        builder.AddLiteral(tree, model, expression, CodeLeverKind.Argument, fallbackOwner: owner);
                    }
                }

                builder.AddMissingDefaults(tree, model, creation, defaults);
            }
        }

        return new Result(builder.Build(userTrees), builder.Levers);
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

    /// <summary>Literais numéricos sob <paramref name="node"/>, já com o sinal (<c>-5</c> é um número só).</summary>
    private static IEnumerable<ExpressionSyntax> Candidates(SyntaxNode node) =>
        node.DescendantNodes(n => n is not AttributeListSyntax)
            .OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.NumericLiteralExpression))
            .Select(l => l.Parent is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression)
                ? (ExpressionSyntax)unary
                : l)
            .Where(e => !IsExcluded(e));

    private static bool IsNumericLiteral(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression) ||
        expression is PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax inner } unary &&
        unary.IsKind(SyntaxKind.UnaryMinusExpression) && inner.IsKind(SyntaxKind.NumericLiteralExpression);

    /// <summary>Lugares onde o C# exige constante: trocar o literal por uma chamada não compilaria.</summary>
    private static bool IsExcluded(ExpressionSyntax expression) =>
        expression.Ancestors().Any(a =>
            a is AttributeSyntax or ParameterSyntax or CaseSwitchLabelSyntax or PatternSyntax or EnumMemberDeclarationSyntax ||
            a is LocalDeclarationStatementSyntax { IsConst: true } ||
            a is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword));

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
            // Sobe por contas e parênteses até o argumento.
            while (current.Parent is BinaryExpressionSyntax or ParenthesizedExpressionSyntax or PrefixUnaryExpressionSyntax or CastExpressionSyntax)
            {
                current = current.Parent;
            }
            if (current.Parent is not ArgumentSyntax argument || argument.Parent is not ArgumentListSyntax list) break;
            if (list.Parent is not (BaseObjectCreationExpressionSyntax or InvocationExpressionSyntax)) break;

            var name = argument.NameColon?.Name.Identifier.ValueText;
            if (name == null && model.GetSymbolInfo(list.Parent).Symbol is IMethodSymbol method)
            {
                int index = list.Arguments.IndexOf(argument);
                if (index >= 0 && method.Parameters.Length > 0)
                {
                    name = method.Parameters[Math.Min(index, method.Parameters.Length - 1)].Name;
                }
            }
            if (name == null) break;

            parts.Insert(0, name);
            current = list.Parent;

            // A peça é o dono natural: o "part" do Add(new Bloco(50)) não diz nada.
            if (current is BaseObjectCreationExpressionSyntax creation && IsPart(model.GetTypeInfo(creation).Type)) break;
        }
        return string.Join(".", parts);
    }

    private static bool IsPart(ITypeSymbol? type)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (t.Name == "Part" && t.ContainingNamespace?.ToDisplayString() == "AvaloniaCadEditor.Domain.Features") return true;
        }
        return false;
    }

    /// <summary>Acumula as alavancas e as mudanças de texto de cada árvore.</summary>
    private sealed class Builder(CSharpCompilation compilation)
    {
        private readonly Dictionary<SyntaxTree, List<TextChange>> _changes = new();
        private readonly HashSet<(SyntaxTree, TextSpan)> _taken = new();
        private readonly List<CodeLever> _levers = new();

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
                convertible.ToDouble(CultureInfo.InvariantCulture), owner, path);
            _levers.Add(lever);
            return lever;
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
