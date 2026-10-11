using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AvaloniaCadEditor.Domain.ValueObjects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaCadEditor.Application.Scenes;

/// <summary>
/// Fillet/chanfro aplicado pela interface numa instância de peça: em vez de virar código da cena (onde a
/// instância nem é uma variável <c>IFeature</c>), o <c>FilletFeature</c> é declarado no fim do
/// <c>Build()</c> do record, sobre o sólido que ele retorna. A definição é compartilhada, então vale
/// para todas as instâncias. Como o <see cref="PartSourceEditor"/>, só faz edições de texto pontuais.
/// </summary>
public static class PartBuildFilletEditor
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest, kind: SourceCodeKind.Regular);
    private static readonly Regex IdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Envolve o retorno do <c>Build()</c> de <paramref name="partTypeName"/> num <c>FilletFeature</c>.
    /// <paramref name="localFaces"/> em coordenadas da <b>peça</b>, cada uma como plano (normal + um ponto da
    /// face): o fillet corta só as arestas sobre esses planos (<c>FaceReference</c> com <c>PointOnFace</c>);
    /// null ou vazio = todas as arestas.
    /// <para>
    /// <c>return v;</c> vira <c>var v_chanfrado = new FilletFeature(..., baseFeature: v, ...); return v_chanfrado;</c>.
    /// Qualquer outra expressão (<c>return BooleanFeature.CreateDifference("bloco", ...)</c>) é antes
    /// extraída para <c>var bloco = ...;</c>, para o sólido seguir com declaração própria e nome literal.
    /// </para>
    /// Recusa (InvalidOperationException) quando não acha o record, o <c>Build()</c> ou o <c>return</c> final.
    /// </summary>
    public static string AddFillet(string source, string partTypeName, FilletMode mode, double radius,
        IReadOnlyList<(Vector3D Normal, Point3D Point)>? localFaces)
    {
        var root = CSharpSyntaxTree.ParseText(source ?? string.Empty, ParseOptions).GetRoot();
        var type = root.DescendantNodes().OfType<RecordDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.ValueText == partTypeName)
            ?? throw new InvalidOperationException($"Não achei o record {partTypeName} neste arquivo.");

        var build = type.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Build" && m.ParameterList.Parameters.Count == 0)
            ?? throw new InvalidOperationException($"{partTypeName} não tem um método Build().");

        string newline = source!.Contains("\r\n") ? "\r\n" : "\n";
        var usedNames = UsedNames(type);
        var faces = localFaces ?? Array.Empty<(Vector3D, Point3D)>();

        if (build.Body is { } body)
        {
            // Só o return do próprio corpo: um return dentro de função local ou lambda não é o do Build().
            var ret = body.Statements.OfType<ReturnStatementSyntax>().LastOrDefault();
            if (ret?.Expression == null)
                throw new InvalidOperationException($"Não achei o 'return' final do Build() de {partTypeName}.");

            string indent = IndentOf(source, ret.SpanStart);
            string statements = FilletStatements(ret.Expression, usedNames, indent, newline, mode, radius, faces);
            return Replace(source, ret.Span, statements);
        }

        if (build.ExpressionBody is { } arrow)
        {
            // "Build() => E;" vira bloco: o fillet precisa de declarações antes do retorno.
            string methodIndent = IndentOf(source, build.SpanStart);
            string inner = methodIndent + "    ";
            string statements = FilletStatements(arrow.Expression, usedNames, inner, newline, mode, radius, faces);
            string block = newline + methodIndent + "{" + newline + inner + statements + newline + methodIndent + "}";
            var span = TextSpan.FromBounds(build.ParameterList.Span.End, build.SemicolonToken.Span.End);
            return Replace(source, span, block);
        }

        throw new InvalidOperationException($"O Build() de {partTypeName} não tem corpo.");
    }

    /// <summary>
    /// Faces selecionadas no mundo (normal + ponto) para o referencial da peça cuja instância está em
    /// <paramref name="placement"/> — a instância pode estar girada ou espelhada, e a face de baixo no
    /// mundo pode ser o topo da peça. Arredondado como o código grava (6 casas), igual ao Espelhar.
    /// </summary>
    public static IReadOnlyList<(Vector3D Normal, Point3D Point)> ToPartFrame(Placement placement, IEnumerable<(Vector3D Normal, Point3D Point)> worldFaces)
    {
        var toLocal = placement.Inverse();
        return worldFaces
            .Select(f => (toLocal.ApplyVector(f.Normal), toLocal.Apply(f.Point)))
            .Select(f => (new Vector3D(Round(f.Item1.X), Round(f.Item1.Y), Round(f.Item1.Z)),
                          new Point3D(Round(f.Item2.X), Round(f.Item2.Y), Round(f.Item2.Z))))
            .ToList();

        static double Round(double value) => Math.Round(value, 6);
    }

    /// <summary>
    /// As instruções que substituem o <c>return E;</c>. A primeira linha não leva indentação (fica no
    /// lugar do <c>return</c>); as seguintes usam <paramref name="indent"/>.
    /// </summary>
    private static string FilletStatements(ExpressionSyntax returned, HashSet<string> usedNames, string indent, string newline,
        FilletMode mode, double radius, IReadOnlyList<(Vector3D Normal, Point3D Point)> faces)
    {
        var sb = new StringBuilder();

        string baseName;
        if (returned is IdentifierNameSyntax identifier)
        {
            baseName = identifier.Identifier.ValueText;
        }
        else
        {
            baseName = Unique(LiteralName(returned) ?? "solido_base", usedNames);
            sb.Append($"var {baseName} = {returned};").Append(newline).Append(indent);
        }

        string filletName = Unique($"{baseName}_{(mode == FilletMode.Bevel ? "chanfrado" : "arredondado")}", usedNames);
        string modeCode = mode == FilletMode.Bevel ? "FilletMode.Bevel" : "FilletMode.Round";

        sb.Append($"var {filletName} = new FilletFeature(name: \"{filletName}\", baseFeature: {baseName}, radius: {Number(radius)})").Append(newline);
        sb.Append(indent).Append('{').Append(newline);
        sb.Append(indent).Append($"    Mode = {modeCode},").Append(newline);
        if (faces.Count == 0)
        {
            sb.Append(indent).Append("    ApplyToAllEdges = true").Append(newline);
        }
        else
        {
            var references = faces.Select(f =>
                $"new FaceReference({baseName}, -1, new Vector3D({Number(f.Normal.X)}, {Number(f.Normal.Y)}, {Number(f.Normal.Z)}), " +
                $"new Point3D({Number(f.Point.X)}, {Number(f.Point.Y)}, {Number(f.Point.Z)}), \"CustomFace\")");
            sb.Append(indent).Append("    ApplyToAllEdges = false,").Append(newline);
            sb.Append(indent).Append($"    Faces = {{ {string.Join(", ", references)} }}").Append(newline);
        }
        sb.Append(indent).Append("};").Append(newline);
        sb.Append(indent).Append($"return {filletName};");
        return sb.ToString();
    }

    /// <summary>
    /// O nome que o próprio sólido declara: o <c>Name = "x"</c> do inicializador ou o primeiro literal
    /// string dos argumentos (<c>CreateDifference("bloco", ...)</c>, <c>new BooleanFeature(name: "bloco", ...)</c>).
    /// </summary>
    private static string? LiteralName(ExpressionSyntax expression)
    {
        var arguments = expression switch
        {
            InvocationExpressionSyntax invocation => invocation.ArgumentList.Arguments,
            BaseObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments ?? default,
            _ => default
        };
        var fromInitializer = (expression as BaseObjectCreationExpressionSyntax)?.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(a => a.Left is IdentifierNameSyntax { Identifier.ValueText: "Name" })
            ?.Right;

        var literal = (fromInitializer != null ? new[] { fromInitializer } : arguments.Select(a => a.Expression))
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression));
        string? name = literal?.Token.ValueText;

        return name != null && IdentifierPattern.IsMatch(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
            ? name
            : null;
    }

    /// <summary>Identificadores já usados no record (variáveis locais, parâmetros e propriedades), para não colidir.</summary>
    private static HashSet<string> UsedNames(RecordDeclarationSyntax type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in type.DescendantNodes().OfType<VariableDeclaratorSyntax>()) names.Add(v.Identifier.ValueText);
        foreach (var p in type.DescendantNodes().OfType<ParameterSyntax>()) names.Add(p.Identifier.ValueText);
        foreach (var d in type.DescendantNodes().OfType<SingleVariableDesignationSyntax>()) names.Add(d.Identifier.ValueText);
        foreach (var p in type.Members.OfType<PropertyDeclarationSyntax>()) names.Add(p.Identifier.ValueText);
        return names;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        string candidate = name;
        for (int n = 2; used.Contains(candidate); n++) candidate = $"{name}_{n}";
        used.Add(candidate);
        return candidate;
    }

    /// <summary>Espaços/tabs do começo da linha que contém <paramref name="position"/>.</summary>
    private static string IndentOf(string source, int position)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        int end = lineStart;
        while (end < source.Length && (source[end] == ' ' || source[end] == '\t')) end++;
        return source.Substring(lineStart, end - lineStart);
    }

    private static string Number(double value)
    {
        double rounded = Math.Round(value, 6);
        if (rounded == 0) rounded = 0; // evita "-0"
        return rounded.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string Replace(string source, TextSpan span, string text) =>
        SourceText.From(source).WithChanges(new TextChange(span, text)).ToString();
}
