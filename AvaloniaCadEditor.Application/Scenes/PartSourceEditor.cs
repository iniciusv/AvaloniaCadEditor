using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaCadEditor.Application.Scenes;

/// <summary>Como um parâmetro aparece na chamada <c>Add(...)</c> que criou a instância.</summary>
/// <param name="Name">Nome do parâmetro no construtor primário.</param>
/// <param name="Expression">Texto do argumento no código, ou null quando o valor padrão é usado.</param>
/// <param name="IsEditable">True se o app pode trocar o valor sem perder lógica escrita à mão.</param>
public sealed record PartArgument(string Name, string? Expression, bool IsEditable);

/// <summary>Um parâmetro declarado no record da peça.</summary>
public sealed record PartTypeParameter(string Name, string TypeName, string? DefaultValue, string? RangeMinimum);

/// <summary>Um tipo de peça (record que herda de Part) declarado nos arquivos da cena.</summary>
public sealed record PartTypeInfo(string Name, string FilePath, int Line, IReadOnlyList<PartTypeParameter> Parameters);

/// <summary>
/// Edições no código da cena feitas a partir da interface (inspector, inserir peça, ir para a
/// definição). Trabalha só com a árvore sintática do Roslyn e aplica edições de texto pontuais,
/// então a formatação e os comentários do usuário ficam intactos.
/// </summary>
public static class PartSourceEditor
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest, kind: SourceCodeKind.Regular);

    // ---------------------------------------------------------------- Parâmetros de uma instância

    /// <summary>
    /// Lê, na chamada <c>Add(...)</c> da linha <paramref name="line"/>, como cada parâmetro foi escrito.
    /// Retorna null se não houver uma chamada <c>Add</c> nessa linha.
    /// </summary>
    public static IReadOnlyList<PartArgument>? ReadArguments(string source, int line, IReadOnlyList<string> parameterNames)
    {
        var part = FindPartExpression(Parse(source), line);
        if (part == null) return null;

        return parameterNames
            .Select((name, index) =>
            {
                var expression = FindArgumentExpression(part, name, index);
                return new PartArgument(name, expression?.ToString(), expression == null || IsLiteral(expression));
            })
            .ToList();
    }

    /// <summary>
    /// Troca o valor de um parâmetro na chamada <c>Add(...)</c> da linha <paramref name="line"/>.
    /// <list type="bullet">
    /// <item><c>new T(...)</c>: substitui o argumento ou acrescenta <c>Nome: valor</c>;</item>
    /// <item><c>x with { ... }</c>: substitui ou acrescenta <c>Nome = valor</c>;</item>
    /// <item>qualquer outra expressão (ex.: <c>base1.Part</c>): vira <c>expr with { Nome = valor }</c>.</item>
    /// </list>
    /// Recusa (InvalidOperationException) quando o valor atual é uma expressão, não um literal:
    /// sobrescrever <c>Medidas.Espessura * 2</c> com um número apagaria a lógica do usuário.
    /// </summary>
    public static string SetArgument(string source, int line, string parameterName, int parameterIndex, string valueCode)
    {
        var root = Parse(source);
        var part = FindPartExpression(root, line)
            ?? throw new InvalidOperationException($"Não achei a chamada Add(...) na linha {line}.");

        var existing = FindArgumentExpression(part, parameterName, parameterIndex);
        if (existing != null)
        {
            if (!IsLiteral(existing))
            {
                throw new InvalidOperationException(
                    $"{parameterName} é calculado no código ({existing}); edite essa expressão no editor.");
            }
            return Replace(source, existing.Span, valueCode);
        }

        switch (part)
        {
            case BaseObjectCreationExpressionSyntax creation:
            {
                var args = creation.ArgumentList;
                if (args == null)
                {
                    // new T { ... } sem parênteses: acrescenta a lista de argumentos.
                    var typeEnd = creation is ObjectCreationExpressionSyntax oc ? oc.Type.Span.End : creation.NewKeyword.Span.End;
                    return Insert(source, typeEnd, $"({parameterName}: {valueCode})");
                }
                string separator = args.Arguments.Count > 0 ? ", " : string.Empty;
                return Insert(source, args.CloseParenToken.SpanStart, $"{separator}{parameterName}: {valueCode}");
            }
            case WithExpressionSyntax with:
            {
                var init = with.Initializer;
                if (init.Expressions.Count == 0)
                {
                    return Replace(source, TextSpan.FromBounds(init.OpenBraceToken.Span.End, init.CloseBraceToken.SpanStart),
                        $" {parameterName} = {valueCode} ");
                }
                return Insert(source, init.Expressions.Last().Span.End, $", {parameterName} = {valueCode}");
            }
            default:
            {
                string target = NeedsParentheses(part) ? $"({part})" : part.ToString();
                return Replace(source, part.Span, $"{target} with {{ {parameterName} = {valueCode} }}");
            }
        }
    }

    /// <summary>Código C# para um valor digitado no inspector (sempre com ponto decimal).</summary>
    public static string FormatValue(object value) => value switch
    {
        bool b => b ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
        decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
        string s => SymbolDisplay.FormatLiteral(s, quote: true),
        IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Tipo {value.GetType().Name} não pode ser editado pelo inspector.")
    };

    /// <summary>Tipos que o inspector sabe converter de texto.</summary>
    public static bool IsEditableType(Type type) =>
        type == typeof(double) || type == typeof(float) || type == typeof(decimal) ||
        type == typeof(int) || type == typeof(long) || type == typeof(bool) || type == typeof(string);

    /// <summary>Converte o texto digitado (aceita vírgula ou ponto) para o tipo do parâmetro.</summary>
    public static bool TryParseValue(Type type, string text, out object? value, out string? error)
    {
        value = null;
        error = null;
        text = (text ?? string.Empty).Trim();

        if (type == typeof(string))
        {
            value = text;
            return true;
        }
        if (type == typeof(bool))
        {
            if (bool.TryParse(text, out var b)) { value = b; return true; }
            if (text is "sim" or "1") { value = true; return true; }
            if (text is "não" or "nao" or "0") { value = false; return true; }
            error = "Use true ou false.";
            return false;
        }

        string normalized = text.Replace(',', '.');
        var style = NumberStyles.Float;
        var culture = CultureInfo.InvariantCulture;
        bool ok = false;
        if (type == typeof(double)) { ok = double.TryParse(normalized, style, culture, out var d) && double.IsFinite(d); value = d; }
        else if (type == typeof(float)) { ok = float.TryParse(normalized, style, culture, out var f) && float.IsFinite(f); value = f; }
        else if (type == typeof(decimal)) { ok = decimal.TryParse(normalized, style, culture, out var m); value = m; }
        else if (type == typeof(int)) { ok = int.TryParse(normalized, NumberStyles.Integer, culture, out var i); value = i; }
        else if (type == typeof(long)) { ok = long.TryParse(normalized, NumberStyles.Integer, culture, out var l); value = l; }
        else
        {
            error = $"Tipo {type.Name} não pode ser editado pelo inspector.";
            return false;
        }

        if (!ok)
        {
            value = null;
            error = type == typeof(int) || type == typeof(long) ? "Digite um número inteiro." : "Digite um número.";
        }
        return ok;
    }

    // ---------------------------------------------------------------- Mover uma instância

    /// <summary>
    /// Move a instância criada pela chamada <c>Add(...)</c> da linha dada, somando o deslocamento
    /// ao argumento <c>at:</c> sem perder a lógica escrita no código:
    /// <list type="bullet">
    /// <item>sem <c>at:</c>: acrescenta <c>at: Placement.At(dx, dy, dz)</c>;</item>
    /// <item><c>Placement.At(x, y, z, ...)</c>: soma em cada coordenada (<c>150</c> vira <c>170</c>,
    /// <c>i * 150</c> vira <c>i * 150 + 20</c>);</item>
    /// <item>qualquer outro posicionamento (ex.: <c>Placement.At(base1.World(...))</c>): acrescenta
    /// <c>.Then(Placement.Translate(new Vector3D(dx, dy, dz)))</c>, ou soma num Then desses que já exista.</item>
    /// </list>
    /// </summary>
    public static string TranslateInstance(string source, int line, double dx, double dy, double dz)
    {
        var root = Parse(source);
        var call = FindAddCall(root, line)
            ?? throw new InvalidOperationException($"Não achei a chamada Add(...) na linha {line}.");

        var args = call.ArgumentList.Arguments;
        var at = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "at")
            ?? (args.Count > 1 && args[1].NameColon == null ? args[1] : null);

        if (at == null)
        {
            string placement = FormattableString.Invariant($"Placement.At({Number(dx)}, {Number(dy)}, {Number(dz)})");
            return Insert(source, call.ArgumentList.CloseParenToken.SpanStart, $", at: {placement}");
        }

        var edits = new List<TextChange>();
        if (TryOffsetCoordinates(at.Expression, "At", new[] { "x", "y", "z" }, new[] { dx, dy, dz }, edits))
        {
            return Apply(source, edits);
        }

        // E.Then(Placement.Translate(new Vector3D(a, b, c))): soma no deslocamento que já existe.
        if (at.Expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Then" } } then &&
            then.ArgumentList.Arguments.Count == 1 &&
            then.ArgumentList.Arguments[0].Expression is InvocationExpressionSyntax translate &&
            MethodName(translate.Expression) == "Translate" &&
            translate.ArgumentList.Arguments.Count == 1 &&
            translate.ArgumentList.Arguments[0].Expression is BaseObjectCreationExpressionSyntax vector &&
            vector.ArgumentList is { Arguments.Count: 3 } &&
            vector.ArgumentList.Arguments.All(a => a.NameColon == null))
        {
            var coords = vector.ArgumentList.Arguments;
            double[] deltas = { dx, dy, dz };
            for (int i = 0; i < 3; i++) OffsetExpression(coords[i].Expression, deltas[i], edits);
            return Apply(source, edits);
        }

        string target = NeedsParentheses(at.Expression) ? $"({at.Expression})" : at.Expression.ToString();
        string offset = FormattableString.Invariant($"Placement.Translate(new Vector3D({Number(dx)}, {Number(dy)}, {Number(dz)}))");
        return Replace(source, at.Expression.Span, $"{target}.Then({offset})");
    }

    /// <summary>Trecho da instrução que registrou uma feature (para destacar no editor).</summary>
    public static TextSpan? FindStatementSpan(string source, int line)
    {
        if (line < 1) return null;
        var root = Parse(source);
        var call = FindAddCall(root, line);
        if (call != null)
        {
            return call.FirstAncestorOrSelf<StatementSyntax>()?.Span ?? call.Span;
        }

        // Outras chamadas (AddFeature, ajudantes): a menor instrução que começa nessa linha.
        return root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(st => st is not BlockSyntax && LineOf(st.GetFirstToken()) == line)
            .OrderBy(st => st.Span.Length)
            .Select(st => (TextSpan?)st.Span)
            .FirstOrDefault();
    }

    /// <summary>
    /// Se <paramref name="expression"/> é <c>Placement.At(x, y, z, ...)</c> com coordenadas
    /// numéricas, registra as edições que somam os deslocamentos e devolve true.
    /// </summary>
    private static bool TryOffsetCoordinates(ExpressionSyntax expression, string method, string[] names, double[] deltas, List<TextChange> edits)
    {
        if (expression is not InvocationExpressionSyntax invocation || MethodName(invocation.Expression) != method) return false;
        var args = invocation.ArgumentList.Arguments;

        // Forma At(Point3D, ...): um único argumento posicional que não é um número.
        int positional = args.TakeWhile(a => a.NameColon == null).Count();
        bool named = args.Any(a => a.NameColon != null && names.Contains(a.NameColon.Name.Identifier.ValueText));
        if (positional < 2 && !named) return false;

        for (int i = 0; i < names.Length; i++)
        {
            var arg = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == names[i])
                ?? (i < positional ? args[i] : null);
            if (arg != null)
            {
                OffsetExpression(arg.Expression, deltas[i], edits);
            }
            else if (Math.Abs(deltas[i]) > 1e-9)
            {
                // Coordenada omitida (ex.: z no padrão 0): acrescenta como argumento nomeado.
                edits.Add(new TextChange(new TextSpan(invocation.ArgumentList.CloseParenToken.SpanStart, 0),
                    $", {names[i]}: {Number(deltas[i])}"));
            }
        }
        return true;
    }

    /// <summary>Soma <paramref name="delta"/> a uma expressão numérica, mantendo-a legível.</summary>
    private static void OffsetExpression(ExpressionSyntax expression, double delta, List<TextChange> edits)
    {
        if (Math.Abs(delta) < 1e-9) return;

        if (TryNumber(expression, out double value))
        {
            edits.Add(new TextChange(expression.Span, Number(value + delta)));
            return;
        }

        // e + L / e - L: ajusta o literal em vez de acumular "+ 10 + 5".
        if (expression is BinaryExpressionSyntax binary &&
            (binary.IsKind(SyntaxKind.AddExpression) || binary.IsKind(SyntaxKind.SubtractExpression)) &&
            TryNumber(binary.Right, out double right))
        {
            double current = binary.IsKind(SyntaxKind.AddExpression) ? right : -right;
            double next = current + delta;
            var span = TextSpan.FromBounds(binary.Left.Span.End, binary.Right.Span.End);
            edits.Add(new TextChange(span, SignedSuffix(next)));
            return;
        }

        string text = NeedsParenthesesForSum(expression) ? $"({expression})" : expression.ToString();
        edits.Add(new TextChange(expression.Span, text + SignedSuffix(delta)));
    }

    private static bool TryNumber(ExpressionSyntax expression, out double value)
    {
        value = 0;
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NumericLiteralExpression):
                value = Convert.ToDouble(literal.Token.Value, CultureInfo.InvariantCulture);
                return true;
            case PrefixUnaryExpressionSyntax unary when unary.Operand is LiteralExpressionSyntax inner &&
                                                        inner.IsKind(SyntaxKind.NumericLiteralExpression):
                double v = Convert.ToDouble(inner.Token.Value, CultureInfo.InvariantCulture);
                value = unary.IsKind(SyntaxKind.UnaryMinusExpression) ? -v : v;
                return true;
            default:
                return false;
        }
    }

    /// <summary>" + 20", " - 5" ou "" (quando o deslocamento zera).</summary>
    private static string SignedSuffix(double value)
    {
        if (Math.Abs(value) < 1e-9) return string.Empty;
        return value > 0 ? " + " + Number(value) : " - " + Number(-value);
    }

    /// <summary>Número sem ruído de ponto flutuante (150.00000000001 vira 150).</summary>
    private static string Number(double value)
    {
        double rounded = Math.Round(value, 6);
        if (rounded == 0) rounded = 0; // evita "-0"
        return rounded.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static bool NeedsParenthesesForSum(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or
        ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or LiteralExpressionSyntax or
        PrefixUnaryExpressionSyntax => false,
        BinaryExpressionSyntax b => !(b.IsKind(SyntaxKind.MultiplyExpression) || b.IsKind(SyntaxKind.DivideExpression) ||
                                      b.IsKind(SyntaxKind.ModuloExpression) || b.IsKind(SyntaxKind.AddExpression) ||
                                      b.IsKind(SyntaxKind.SubtractExpression)),
        _ => true
    };

    private static string Apply(string source, List<TextChange> edits) =>
        edits.Count == 0 ? source : SourceText.From(source).WithChanges(edits.OrderBy(e => e.Span.Start)).ToString();

    // ---------------------------------------------------------------- Tipos de peça

    /// <summary>Records não abstratos que herdam de <c>Part</c>, em ordem de nome.</summary>
    public static IReadOnlyList<PartTypeInfo> FindPartTypes(IEnumerable<ScriptDocument> documents)
    {
        var result = new List<PartTypeInfo>();
        foreach (var doc in documents)
        {
            var root = Parse(doc.Text);
            // Só records: Part é um record abstrato, e uma classe não pode herdar de record.
            foreach (var type in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
            {
                if (type.Modifiers.Any(SyntaxKind.AbstractKeyword)) continue;
                if (type.BaseList?.Types.Any(t => SimpleName(t.Type) == "Part") != true) continue;

                var parameters = type.ParameterList?.Parameters
                    .Select(p => new PartTypeParameter(
                        p.Identifier.ValueText,
                        p.Type?.ToString() ?? "object",
                        p.Default?.Value.ToString(),
                        RangeMinimum(p)))
                    .ToList() ?? new List<PartTypeParameter>();

                int line = type.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                result.Add(new PartTypeInfo(type.Identifier.ValueText, doc.FilePath, line, parameters));
            }
        }
        return result.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Chamada que insere uma peça nova: parâmetros obrigatórios com o mínimo do [Range]
    /// (ou um valor neutro), os opcionais ficam com o padrão do record.
    /// </summary>
    public static string BuildAddStatement(PartTypeInfo type, double x = 0, double y = 0, double z = 0)
    {
        var required = type.Parameters
            .Where(p => p.DefaultValue == null)
            .Select(p => $"{p.Name}: {p.RangeMinimum ?? NeutralValue(p.TypeName)}");
        string at = FormattableString.Invariant($"Placement.At({x:0.###}, {y:0.###}, {z:0.###})");
        return $"Add(new {type.Name}({string.Join(", ", required)}), at: {at});";
    }

    /// <summary>
    /// Insere uma instrução depois da última instrução de nível superior. Não pode ir para o fim do
    /// arquivo: em C#, instruções de nível superior precisam vir antes das declarações de tipo.
    /// </summary>
    public static string InsertTopLevelStatement(string source, string statement)
    {
        var root = (CompilationUnitSyntax)Parse(source);
        string newline = source.Contains("\r\n") ? "\r\n" : "\n";

        var lastStatement = root.Members.OfType<GlobalStatementSyntax>().LastOrDefault();
        if (lastStatement != null)
        {
            int end = lastStatement.Span.End;
            return Insert(source, end, newline + statement);
        }

        var firstMember = root.Members.FirstOrDefault();
        if (firstMember != null)
        {
            int start = firstMember.GetLeadingTrivia().Any() ? firstMember.FullSpan.Start : firstMember.SpanStart;
            return Insert(source, start, statement + newline + newline);
        }

        string trimmed = source.TrimEnd();
        return trimmed.Length == 0 ? statement + newline : trimmed + newline + statement + newline;
    }

    // ---------------------------------------------------------------- Internos

    private static SyntaxNode Parse(string source) =>
        CSharpSyntaxTree.ParseText(source ?? string.Empty, ParseOptions).GetRoot();

    /// <summary>Chamada Add/Scene.Add cujo nome está na linha dada (ou que atravessa essa linha).</summary>
    private static InvocationExpressionSyntax? FindAddCall(SyntaxNode root, int line)
    {
        var calls = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(i => MethodName(i.Expression) == "Add" && i.ArgumentList.Arguments.Count > 0)
            .ToList();

        // CallerLineNumber aponta para a linha do nome do método; para chamadas quebradas em
        // várias linhas, aceita também qualquer linha dentro da chamada.
        return calls.FirstOrDefault(i => LineOf(NameToken(i.Expression)) == line)
            ?? calls.FirstOrDefault(i => LineOf(i.GetFirstToken()) <= line && LineOf(i.GetLastToken()) >= line);
    }

    /// <summary>Primeiro argumento (a peça) da chamada Add/Scene.Add cujo nome está na linha dada.</summary>
    private static ExpressionSyntax? FindPartExpression(SyntaxNode root, int line)
    {
        var call = FindAddCall(root, line);
        if (call == null) return null;

        var args = call.ArgumentList.Arguments;
        var partArg = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "part")
            ?? args.FirstOrDefault(a => a.NameColon == null);
        return partArg?.Expression;
    }

    /// <summary>Onde o parâmetro está escrito dentro da expressão da peça (null = usa o padrão).</summary>
    private static ExpressionSyntax? FindArgumentExpression(ExpressionSyntax part, string name, int index)
    {
        switch (part)
        {
            case BaseObjectCreationExpressionSyntax creation:
            {
                var fromInitializer = InitializerValue(creation.Initializer, name);
                if (fromInitializer != null) return fromInitializer;
                var args = creation.ArgumentList?.Arguments;
                if (args == null) return null;

                var named = args.Value.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name);
                if (named != null) return named.Expression;

                // Argumentos posicionais vêm antes dos nomeados; o índice do parâmetro é a posição.
                if (index >= 0 && index < args.Value.Count && args.Value[index].NameColon == null)
                {
                    return args.Value[index].Expression;
                }
                return null;
            }
            case WithExpressionSyntax with:
                // Só o que está no with conta: um valor herdado da peça original (ex.: base1.Part)
                // é editado acrescentando uma atribuição, sem mexer na peça original.
                return InitializerValue(with.Initializer, name);
            case ParenthesizedExpressionSyntax parenthesized:
                return FindArgumentExpression(parenthesized.Expression, name, index);
            default:
                return null;
        }
    }

    private static ExpressionSyntax? InitializerValue(InitializerExpressionSyntax? initializer, string name) =>
        initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(a => a.Left is IdentifierNameSyntax id && id.Identifier.ValueText == name)
            ?.Right;

    private static bool IsLiteral(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax => true,
        PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax } unary =>
            unary.IsKind(SyntaxKind.UnaryMinusExpression) || unary.IsKind(SyntaxKind.UnaryPlusExpression),
        ParenthesizedExpressionSyntax p => IsLiteral(p.Expression),
        _ => false
    };

    private static bool NeedsParentheses(ExpressionSyntax expression) => expression is not (
        IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or
        ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or BaseObjectCreationExpressionSyntax);

    private static string? MethodName(ExpressionSyntax expression) => NameToken(expression).ValueText;

    private static SyntaxToken NameToken(ExpressionSyntax expression) => expression switch
    {
        SimpleNameSyntax simple => simple.Identifier,
        MemberAccessExpressionSyntax member => member.Name.Identifier,
        _ => default
    };

    private static string? SimpleName(TypeSyntax type) => type switch
    {
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        _ => null
    };

    private static int LineOf(SyntaxToken token) =>
        token == default ? -1 : token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string? RangeMinimum(ParameterSyntax parameter)
    {
        var range = parameter.AttributeLists
            .SelectMany(l => l.Attributes)
            .FirstOrDefault(a => SimpleName(a.Name) is "Range" or "RangeAttribute");
        var first = range?.ArgumentList?.Arguments.FirstOrDefault();
        return first != null && IsLiteral(first.Expression) ? first.Expression.ToString() : null;
    }

    private static string NeutralValue(string typeName) => typeName switch
    {
        "double" or "float" or "decimal" or "int" or "long" => "10",
        "bool" => "false",
        "string" => "\"\"",
        _ => "default"
    };

    private static string Replace(string source, TextSpan span, string text) =>
        SourceText.From(source).WithChanges(new TextChange(span, text)).ToString();

    private static string Insert(string source, int position, string text) =>
        Replace(source, new TextSpan(position, 0), text);
}
