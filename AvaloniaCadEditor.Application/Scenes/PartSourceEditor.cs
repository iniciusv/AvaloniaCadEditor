using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;
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
        if (TryMergeTranslate(at.Expression, dx, dy, dz, edits))
        {
            return Apply(source, edits);
        }

        string target = NeedsParentheses(at.Expression) ? $"({at.Expression})" : at.Expression.ToString();
        return Replace(source, at.Expression.Span, $"{target}.Then({TranslationCall(dx, dy, dz)})");
    }

    // ---------------------------------------------------------------- Cor de uma instância

    /// <summary>
    /// Define o <c>color:</c> da chamada <c>Add(...)</c> da linha dada: troca o que já existe ou
    /// acrescenta no fim. <paramref name="color"/> null remove o argumento (volta à cor padrão).
    /// </summary>
    public static string SetInstanceColor(string source, int line, string? color)
    {
        var root = Parse(source);
        var call = FindAddCall(root, line)
            ?? throw new InvalidOperationException($"Não achei a chamada Add(...) na linha {line}.");

        var args = call.ArgumentList.Arguments;
        int index = -1;
        for (int i = 0; i < args.Count; i++)
        {
            // color é o 4º parâmetro de Add(part, at, name, color).
            if (args[i].NameColon?.Name.Identifier.ValueText == "color" || (i == 3 && args[i].NameColon == null))
            {
                index = i;
                break;
            }
        }

        string? literal = color == null ? null : $"\"{color}\"";
        if (index < 0)
        {
            return literal == null ? source : Insert(source, call.ArgumentList.CloseParenToken.SpanStart, $", color: {literal}");
        }
        if (literal != null)
        {
            return Replace(source, args[index].Expression.Span, literal);
        }

        // Remove o argumento junto com a vírgula que o precede.
        var separator = args.GetSeparator(index - 1);
        return Replace(source, TextSpan.FromBounds(separator.SpanStart, args[index].Span.End), string.Empty);
    }

    // ---------------------------------------------------------------- Copiar uma instância

    /// <summary>
    /// Duplica a chamada <c>Add(...)</c> da linha dada numa instrução nova logo depois da instrução
    /// original (sem <c>var x =</c> e sem <c>name:</c>, para não repetir variável nem nome) e devolve
    /// o código e a linha da cópia — use-a com <see cref="TranslateInstance"/> / <see cref="RotateInstance"/>.
    /// </summary>
    public static (string Source, int Line) DuplicateInstance(string source, int line)
    {
        var root = Parse(source);
        var call = FindAddCall(root, line)
            ?? throw new InvalidOperationException($"Não achei a chamada Add(...) na linha {line}.");
        var statement = call.FirstAncestorOrSelf<StatementSyntax>()
            ?? throw new InvalidOperationException($"A chamada Add(...) da linha {line} não está numa instrução.");

        var args = call.ArgumentList.Arguments;
        var nameArg = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "name");
        var copy = nameArg == null ? call : call.WithArgumentList(call.ArgumentList.WithArguments(args.Remove(nameArg)));

        string newline = LineBreakOf(source, statement);
        string indent = IndentOf(source, statement);

        string updated = Insert(source, statement.Span.End, $"{newline}{indent}{copy.WithoutTrivia()};");
        return (updated, LineOf(statement.GetLastToken()) + 1);
    }

    // ---------------------------------------------------------------- Girar uma instância

    /// <summary>
    /// Gira a instância criada pela chamada <c>Add(...)</c> da linha dada em torno de
    /// <paramref name="pivot"/> e <paramref name="axis"/>, somando ao argumento <c>at:</c>:
    /// <list type="bullet">
    /// <item>sem <c>at:</c>: acrescenta <c>at: Placement.RotationAbout(pivot, axis, graus)</c>;</item>
    /// <item><c>Placement.RotationAbout(mesmo pivô, mesmo eixo, d)</c>, direto ou no fim de um
    /// <c>.Then(...)</c>: soma no ângulo existente em vez de encadear mais um Then;</item>
    /// <item>qualquer outro posicionamento: acrescenta
    /// <c>.Then(Placement.RotationAbout(pivot, axis, graus))</c>.</item>
    /// </list>
    /// </summary>
    public static string RotateInstance(string source, int line, Point3D pivot, Vector3D axis, double degrees)
    {
        var root = Parse(source);
        var call = FindAddCall(root, line)
            ?? throw new InvalidOperationException($"Não achei a chamada Add(...) na linha {line}.");

        var args = call.ArgumentList.Arguments;
        var at = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "at")
            ?? (args.Count > 1 && args[1].NameColon == null ? args[1] : null);

        if (at == null)
        {
            return Insert(source, call.ArgumentList.CloseParenToken.SpanStart, $", at: {RotationCall(pivot, axis, degrees)}");
        }

        if (TryMergeRotation(at.Expression, pivot, axis, degrees, out string? merged))
        {
            return Replace(source, at.Expression.Span, merged!);
        }

        string target = NeedsParentheses(at.Expression) ? $"({at.Expression})" : at.Expression.ToString();
        return Replace(source, at.Expression.Span, $"{target}.Then({RotationCall(pivot, axis, degrees)})");
    }

    /// <summary>
    /// Se <paramref name="expression"/> já é (ou termina num <c>.Then</c> de)
    /// <c>Placement.RotationAbout(pivot, axis, d)</c> com o mesmo pivô e eixo, devolve o texto com
    /// o ângulo somado.
    /// </summary>
    private static bool TryMergeRotation(ExpressionSyntax expression, Point3D pivot, Vector3D axis, double degrees, out string? merged)
    {
        merged = null;

        if (expression is InvocationExpressionSyntax direct &&
            MethodName(direct.Expression) == "RotationAbout" &&
            TryMatchRotation(direct, pivot, axis, out double existingDirect))
        {
            merged = RotationCall(pivot, axis, existingDirect + degrees);
            return true;
        }

        if (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Then" } } then &&
            then.ArgumentList.Arguments.Count == 1 &&
            then.ArgumentList.Arguments[0].Expression is InvocationExpressionSyntax rotation &&
            MethodName(rotation.Expression) == "RotationAbout" &&
            TryMatchRotation(rotation, pivot, axis, out double existingThen))
        {
            var baseExpr = ((MemberAccessExpressionSyntax)then.Expression).Expression;
            merged = $"{baseExpr}.Then({RotationCall(pivot, axis, existingThen + degrees)})";
            return true;
        }

        return false;
    }

    private static bool TryMatchRotation(InvocationExpressionSyntax rotationCall, Point3D pivot, Vector3D axis, out double existingDegrees)
    {
        existingDegrees = 0;
        var rotArgs = rotationCall.ArgumentList.Arguments;
        if (rotArgs.Count != 3) return false;
        if (!TryPoint3D(rotArgs[0].Expression, out var existingPivot)) return false;
        if (!TryVector3D(rotArgs[1].Expression, out var existingAxis)) return false;
        if (!TryNumber(rotArgs[2].Expression, out existingDegrees)) return false;
        return existingPivot.Equals(pivot) && existingAxis.Equals(axis);
    }

    public static string RotationCall(Point3D pivot, Vector3D axis, double degrees) =>
        FormattableString.Invariant($"Placement.RotationAbout({PointText(pivot)}, {AxisText(axis)}, {Number(degrees)})");

    private static string PointText(Point3D p) =>
        FormattableString.Invariant($"new Point3D({Number(p.X)}, {Number(p.Y)}, {Number(p.Z)})");

    private static string AxisText(Vector3D axis)
    {
        if (axis.Equals(Vector3D.UnitX)) return "Vector3D.UnitX";
        if (axis.Equals(Vector3D.UnitY)) return "Vector3D.UnitY";
        if (axis.Equals(Vector3D.UnitZ)) return "Vector3D.UnitZ";
        return FormattableString.Invariant($"new Vector3D({Number(axis.X)}, {Number(axis.Y)}, {Number(axis.Z)})");
    }

    private static bool TryPoint3D(ExpressionSyntax expression, out Point3D point)
    {
        point = default;
        if (expression is not BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 3 } creation) return false;
        var a = creation.ArgumentList!.Arguments;
        if (!TryNumber(a[0].Expression, out double x) || !TryNumber(a[1].Expression, out double y) || !TryNumber(a[2].Expression, out double z))
            return false;
        point = new Point3D(x, y, z);
        return true;
    }

    private static bool TryVector3D(ExpressionSyntax expression, out Vector3D vector)
    {
        vector = default;
        switch (expression)
        {
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "UnitX" }: vector = Vector3D.UnitX; return true;
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "UnitY" }: vector = Vector3D.UnitY; return true;
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "UnitZ" }: vector = Vector3D.UnitZ; return true;
            case BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 3 } creation:
                var a = creation.ArgumentList!.Arguments;
                if (TryNumber(a[0].Expression, out double x) && TryNumber(a[1].Expression, out double y) && TryNumber(a[2].Expression, out double z))
                {
                    vector = new Vector3D(x, y, z);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- Mover/girar/copiar uma feature solta

    /// <summary>
    /// Move a feature solta registrada por <c>AddFeature(x)</c> na linha dada, sem reescrever as
    /// coordenadas originais de <c>x</c> (diferente de uma instância, que tem um <c>at:</c> dedicado):
    /// <list type="bullet">
    /// <item>ainda não é <c>x.Transformed(...)</c>: vira <c>x.Transformed(Placement.Translate(new Vector3D(dx, dy, dz)))</c>;</item>
    /// <item>já é, com <c>Placement.Translate(...)</c> direto ou no fim de um <c>.Then</c>: soma no deslocamento existente;</item>
    /// <item>qualquer outro placement: acrescenta <c>.Then(Placement.Translate(...))</c> dentro do Transformed.</item>
    /// </list>
    /// Se <c>x</c> também é usado em outro lugar do script (ex.: como ferramenta de uma
    /// <c>BooleanFeature</c>), a transformação vai para a <b>declaração</b> de <c>x</c> em vez do
    /// argumento do <c>AddFeature</c> — senão o corte ficaria parado enquanto um sólido novo (e
    /// visível) nascia no lugar de destino. Ver <see cref="EditDeclarationInsteadOfAddFeature"/>.
    /// </summary>
    public static string TranslateFeature(string source, int line, double dx, double dy, double dz) =>
        EditTransformedFeature(source, line, TranslationCall(dx, dy, dz),
            (currentSource, placement) => { var edits = new List<TextChange>(); return TryMergeTranslate(placement, dx, dy, dz, edits) ? Apply(currentSource, edits) : null; });

    /// <summary>
    /// Gira a feature solta registrada por <c>AddFeature(x)</c> na linha dada em torno de
    /// <paramref name="pivot"/>/<paramref name="axis"/>, seguindo o mesmo padrão de <see cref="TranslateFeature"/>
    /// (e de <see cref="RotateInstance"/>: mesmo pivô/eixo soma o ângulo) — inclusive o desvio para a
    /// declaração quando <c>x</c> é usado em outro lugar do script.
    /// </summary>
    public static string RotateFeature(string source, int line, Point3D pivot, Vector3D axis, double degrees) =>
        EditTransformedFeature(source, line, RotationCall(pivot, axis, degrees),
            (currentSource, placement) => TryMergeRotation(placement, pivot, axis, degrees, out string? merged) ? Replace(currentSource, placement.Span, merged!) : null);

    /// <summary>
    /// Aplica um novo placement à feature solta de <c>AddFeature(x)</c> na linha dada. Se <c>x</c> é só
    /// um identificador (ou já <c>x.Transformed(P)</c>) e é referenciado em outro lugar do mesmo
    /// arquivo — o caso de uma ferramenta/base de uma <c>BooleanFeature</c> — a edição vai para a
    /// <b>declaração</b> de <c>x</c>, para que quem depende de <c>x</c> (a booleana) veja o sólido
    /// já na posição nova, em vez de continuar apontando para o original parado. Senão, segue o
    /// caminho de sempre: funde com um <c>x.Transformed(...)</c> já existente no próprio
    /// <c>AddFeature</c> (via <paramref name="tryMerge"/>), encadeia um <c>.Then</c>, ou cria um novo.
    /// </summary>
    private static string EditTransformedFeature(string source, int line, string newPlacementCode, Func<string, ExpressionSyntax, string?> tryMerge)
    {
        var root = Parse(source);
        var call = FindAddFeatureCall(root, line)
            ?? throw new InvalidOperationException($"Não achei a chamada AddFeature(...) na linha {line}.");
        var expr = call.ArgumentList.Arguments[0].Expression;

        var (identifier, existingPlacementInCall) = DecomposeAddFeatureArgument(expr);
        if (identifier != null && IsReferencedElsewhere(root, identifier.Identifier.ValueText, call))
        {
            return EditDeclarationInsteadOfAddFeature(source, root, identifier.Identifier.ValueText, existingPlacementInCall, call, newPlacementCode, tryMerge);
        }

        return ApplyPlacement(source, expr, newPlacementCode, tryMerge);
    }

    /// <summary>
    /// Se <paramref name="expr"/> (o argumento de <c>AddFeature</c>) é um identificador simples
    /// <c>x</c>, ou <c>x.Transformed(P)</c>, devolve <c>x</c> e (no segundo caso) <c>P</c>. Senão,
    /// devolve <c>(null, null)</c> — não há como saber a variável de origem para olhar o resto do script.
    /// </summary>
    private static (IdentifierNameSyntax? Identifier, ExpressionSyntax? ExistingPlacement) DecomposeAddFeatureArgument(ExpressionSyntax expr)
    {
        if (expr is IdentifierNameSyntax id) return (id, null);

        if (expr is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transformed" } transformedMember } invocation &&
            transformedMember.Expression is IdentifierNameSyntax innerId &&
            invocation.ArgumentList.Arguments.Count >= 1)
        {
            return (innerId, invocation.ArgumentList.Arguments[0].Expression);
        }

        return (null, null);
    }

    /// <summary>True se <paramref name="variableName"/> aparece em algum outro lugar do arquivo, fora
    /// da própria chamada <c>AddFeature</c> (ex.: como base/ferramenta de uma BooleanFeature).</summary>
    private static bool IsReferencedElsewhere(SyntaxNode root, string variableName, InvocationExpressionSyntax addFeatureCall) =>
        root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(id => id.Identifier.ValueText == variableName && !addFeatureCall.Span.Contains(id.Span));

    /// <summary>
    /// True se <paramref name="variableName"/> é usado como <c>x.Algo(...)</c> em outro lugar do
    /// arquivo (fora do próprio <c>AddFeature</c>) — mover a declaração para <c>x.Transformed(...)</c>
    /// mudaria o tipo estático de <c>x</c> e poderia quebrar essa chamada, então não arriscamos.
    /// </summary>
    private static bool HasRiskyMemberAccessElsewhere(SyntaxNode root, string variableName, InvocationExpressionSyntax addFeatureCall) =>
        root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(m => m.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == variableName &&
                      !addFeatureCall.Span.Contains(m.Span));

    /// <summary>
    /// Move o placement para a declaração <c>var x = ...;</c> em vez do <c>AddFeature(x)</c>, porque
    /// outra parte do script (ex.: uma BooleanFeature) também usa <c>x</c> e precisa ver a posição nova.
    /// Se o <c>AddFeature</c> já estava como <c>x.Transformed(P)</c> — uma cena que ficou inconsistente
    /// por ter sido movida assim antes desta correção existir — primeiro repara: volta a ser
    /// <c>AddFeature(x)</c> e leva <c>P</c> para a declaração, e só então aplica o placement novo.
    /// </summary>
    private static string EditDeclarationInsteadOfAddFeature(
        string source, SyntaxNode root, string variableName, ExpressionSyntax? existingPlacementInCall,
        InvocationExpressionSyntax addFeatureCall, string newPlacementCode, Func<string, ExpressionSyntax, string?> tryMerge)
    {
        if (HasRiskyMemberAccessElsewhere(root, variableName, addFeatureCall))
        {
            throw new InvalidOperationException(
                $"'{variableName}' é usado com '.algumMétodo(...)' em outro lugar do script; mover assim quebraria essa chamada. " +
                $"Edite '{variableName}' direto no código, ou tire essa dependência antes de mover.");
        }

        string currentSource = source;

        if (existingPlacementInCall != null)
        {
            // Repara: AddFeature(x.Transformed(P)) volta a ser AddFeature(x)...
            currentSource = Replace(currentSource, addFeatureCall.ArgumentList.Arguments[0].Expression.Span, variableName);

            // ...e P vai para a declaração (sempre por composição estrutural — .Then/.Transformed —
            // nunca por fusão numérica: não sabemos se P é do mesmo tipo do placement novo).
            var declaratorForFold = FindFeatureDeclarator(Parse(currentSource), variableName)
                ?? throw new InvalidOperationException(DeclarationNotFoundMessage(variableName));
            currentSource = ApplyPlacement(currentSource, declaratorForFold.Initializer!.Value, existingPlacementInCall.ToString(), static (_, _) => null);
        }

        var declarator = FindFeatureDeclarator(Parse(currentSource), variableName)
            ?? throw new InvalidOperationException(DeclarationNotFoundMessage(variableName));
        return ApplyPlacement(currentSource, declarator.Initializer!.Value, newPlacementCode, tryMerge);
    }

    private static VariableDeclaratorSyntax? FindFeatureDeclarator(SyntaxNode root, string variableName) =>
        root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(d => d.Identifier.ValueText == variableName && d.Initializer?.Value != null);

    private static string DeclarationNotFoundMessage(string variableName) =>
        $"'{variableName}' é usado em outro lugar do script (ex.: numa operação booleana), mas não achei " +
        $"'var {variableName} = ...;' nesse arquivo para mover o sólido de verdade.";

    /// <summary>
    /// Aplica <paramref name="newPlacementCode"/> sobre <paramref name="target"/> — o argumento do
    /// <c>AddFeature</c>, ou o inicializador da declaração de <c>x</c>, dependendo de quem chamou:
    /// funde com um <c>.Transformed(P)</c> que já esteja no fim de <paramref name="target"/> (via
    /// <paramref name="tryMerge"/>; sem sucesso, encadeia um <c>.Then</c> dentro dele), ou embrulha do zero.
    /// </summary>
    private static string ApplyPlacement(string currentSource, ExpressionSyntax target, string newPlacementCode, Func<string, ExpressionSyntax, string?> tryMerge)
    {
        if (target is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transformed" } } transformed &&
            transformed.ArgumentList.Arguments.Count >= 1)
        {
            var placementArg = transformed.ArgumentList.Arguments[0].Expression;
            var merged = tryMerge(currentSource, placementArg);
            if (merged != null) return merged;

            string placementTarget = NeedsParentheses(placementArg) ? $"({placementArg})" : placementArg.ToString();
            return Replace(currentSource, placementArg.Span, $"{placementTarget}.Then({newPlacementCode})");
        }

        string targetText = NeedsParentheses(target) ? $"({target})" : target.ToString();
        return Replace(currentSource, target.Span, $"{targetText}.Transformed({newPlacementCode})");
    }

    /// <summary>
    /// Copia a feature solta registrada por <c>AddFeature(x)</c> na linha dada: acrescenta
    /// <c>var nome = x.Transformed(placementCode, name: "nome"); AddFeature(nome);</c> logo depois
    /// da instrução original (<paramref name="placementCode"/> normalmente vem de
    /// <see cref="RotationCall"/> ou de um deslocamento equivalente a <see cref="TranslateFeature"/>),
    /// e devolve o código e a linha do novo <c>AddFeature</c>.
    /// </summary>
    public static (string Source, int Line) CopyFeature(string source, int line, string placementCode)
    {
        var root = Parse(source);
        var call = FindAddFeatureCall(root, line)
            ?? throw new InvalidOperationException($"Não achei a chamada AddFeature(...) na linha {line}.");
        var statement = call.FirstAncestorOrSelf<StatementSyntax>()
            ?? throw new InvalidOperationException($"A chamada AddFeature(...) da linha {line} não está numa instrução.");
        var expr = call.ArgumentList.Arguments[0].Expression;

        // "Base" do nome: a origem, mesmo que já esteja copiada (x.Transformed(...)) - evita
        // encadear "_copia1_copia2_copia3" em cópias sucessivas.
        var baseExpr = expr is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transformed" } transformedMember }
            ? transformedMember.Expression
            : expr;
        string baseName = baseExpr is IdentifierNameSyntax id ? id.Identifier.ValueText : "copia";

        var existingNames = root.DescendantTokens().Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);
        string copyName = $"{baseName}_copia1";
        for (int n = 2; existingNames.Contains(copyName); n++)
        {
            copyName = $"{baseName}_copia{n}";
        }

        string newline = LineBreakOf(source, statement);
        string indent = IndentOf(source, statement);
        string insertion =
            $"{newline}{indent}var {copyName} = {expr.WithoutTrivia()}.Transformed({placementCode}, name: \"{copyName}\");" +
            $"{newline}{indent}AddFeature({copyName});";

        string updated = Insert(source, statement.Span.End, insertion);
        return (updated, LineOf(statement.GetLastToken()) + 2);
    }

    /// <summary>Chamada AddFeature(x) cujo nome está na linha dada (ou que atravessa essa linha).</summary>
    private static InvocationExpressionSyntax? FindAddFeatureCall(SyntaxNode root, int line)
    {
        var calls = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(i => MethodName(i.Expression) == "AddFeature" && i.ArgumentList.Arguments.Count > 0)
            .ToList();

        return calls.FirstOrDefault(i => LineOf(NameToken(i.Expression)) == line)
            ?? calls.FirstOrDefault(i => LineOf(i.GetFirstToken()) <= line && LineOf(i.GetLastToken()) >= line);
    }

    /// <summary>
    /// Se <paramref name="expression"/> é <c>Placement.Translate(new Vector3D(x, y, z))</c>, direto
    /// ou no fim de um <c>.Then(...)</c>, registra as edições que somam o deslocamento e devolve true.
    /// </summary>
    private static bool TryMergeTranslate(ExpressionSyntax expression, double dx, double dy, double dz, List<TextChange> edits)
    {
        InvocationExpressionSyntax? translate = null;

        if (expression is InvocationExpressionSyntax bare && MethodName(bare.Expression) == "Translate")
        {
            translate = bare;
        }
        else if (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Then" } } then &&
                 then.ArgumentList.Arguments.Count == 1 &&
                 then.ArgumentList.Arguments[0].Expression is InvocationExpressionSyntax inner &&
                 MethodName(inner.Expression) == "Translate")
        {
            translate = inner;
        }

        if (translate == null ||
            translate.ArgumentList.Arguments.Count != 1 ||
            translate.ArgumentList.Arguments[0].Expression is not BaseObjectCreationExpressionSyntax vector ||
            vector.ArgumentList is not { Arguments.Count: 3 } ||
            !vector.ArgumentList.Arguments.All(a => a.NameColon == null))
        {
            return false;
        }

        var coords = vector.ArgumentList.Arguments;
        double[] deltas = { dx, dy, dz };
        for (int i = 0; i < 3; i++) OffsetExpression(coords[i].Expression, deltas[i], edits);
        return true;
    }

    public static string TranslationCall(double dx, double dy, double dz) =>
        FormattableString.Invariant($"Placement.Translate(new Vector3D({Number(dx)}, {Number(dy)}, {Number(dz)}))");

    /// <summary>Quebra de linha usada por <paramref name="statement"/> (arquivos colados podem misturar \n e \r\n).</summary>
    private static string LineBreakOf(string source, StatementSyntax statement)
    {
        var text = SourceText.From(source);
        var endLine = text.Lines.GetLineFromPosition(statement.Span.End);
        return endLine.EndIncludingLineBreak > endLine.End
            ? text.ToString(TextSpan.FromBounds(endLine.End, endLine.EndIncludingLineBreak))
            : (source.Contains("\r\n") ? "\r\n" : "\n");
    }

    /// <summary>Indentação (espaços/tabs no início da linha) de <paramref name="statement"/>.</summary>
    private static string IndentOf(string source, StatementSyntax statement)
    {
        var text = SourceText.From(source);
        var statementLine = text.Lines.GetLineFromPosition(statement.SpanStart);
        return new string(text.ToString(statementLine.Span).TakeWhile(char.IsWhiteSpace).ToArray());
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
