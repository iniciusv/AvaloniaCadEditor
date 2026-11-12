using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AvaloniaCadEditor.Application.EditContext;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Library;

/// <summary>
/// "Furar a peça aberta" com um vazio da biblioteca (<c>Part.IsCutter</c>): o clique numa face da peça dá o
/// ponto (o primeiro parâmetro <c>Point3D</c> do vazio, ex.: <c>Face</c>) e a direção para dentro do material
/// (o primeiro <c>Vector3D</c>, ex.: <c>Direcao</c>), já nas coordenadas do <c>Build()</c> da peça.
/// <para>
/// Devolve um trecho de cena que o <see cref="PartSolidInserter"/> grava na peça como ferramenta da diferença:
/// <c>var furo_rebaixado_1 = new FuroRebaixado(Face: ..., Direcao: ...).Instance("furo_rebaixado_1");</c> mais
/// um <c>AddFeature(x.Transformed(posição da peça))</c>. Como os parâmetros já são locais, a conversão mundo →
/// local é a identidade e a linha entra no <c>Build()</c> como está, sem <c>at:</c>. Puro.
/// </para>
/// </summary>
public static class PartLibraryDrill
{
    /// <summary>Casas do ponto clicado, em mm: o clique não é mais preciso que isso.</summary>
    private const double PointStep = 0.01;

    /// <summary>O parâmetro que recebe o ponto do clique: o primeiro <c>Point3D</c> do record.</summary>
    public static PartTypeParameter? PointParameter(PartLibraryItem item) =>
        item.Parameters.FirstOrDefault(p => BaseType(p.TypeName) == "Point3D");

    /// <summary>O parâmetro que recebe a direção para dentro do material: o primeiro <c>Vector3D</c> do record.</summary>
    public static PartTypeParameter? DirectionParameter(PartLibraryItem item) =>
        item.Parameters.FirstOrDefault(p => BaseType(p.TypeName) == "Vector3D");

    /// <summary>Por que este item não fura, ou null.</summary>
    public static string? Refusal(PartLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsCutter) return $"{item.Name} não é um vazio (não declara IsCutter).";
        if (PointParameter(item) == null || DirectionParameter(item) == null)
            return $"{item.Name} precisa de um parâmetro Point3D (onde) e um Vector3D (para onde) para receber o clique.";
        return null;
    }

    /// <summary>
    /// O ponto e a normal do clique (no mundo, a normal saindo da face) nas coordenadas da peça: o ponto em
    /// centésimos de mm e a direção invertida, para dentro do material, sem ruído numérico.
    /// </summary>
    public static (Point3D Point, Vector3D Direction) ToLocal(PartEditContext context, Point3D worldPoint, Vector3D worldNormal)
    {
        ArgumentNullException.ThrowIfNull(context);
        var inverse = context.Placement.Inverse();
        var point = inverse.Apply(worldPoint);
        var direction = inverse.ApplyVector(new Vector3D(-worldNormal.X, -worldNormal.Y, -worldNormal.Z)).Normalize();
        return (
            new Point3D(Step(point.X), Step(point.Y), Step(point.Z)),
            new Vector3D(Clean(direction.X), Clean(direction.Y), Clean(direction.Z)));
    }

    /// <summary>
    /// O trecho de cena do vazio no ponto clicado. <paramref name="values"/> são os parâmetros escolhidos no
    /// detalhe; os de ponto e direção são trocados pelos do clique.
    /// </summary>
    /// <param name="existingNames">Identificadores já usados nos arquivos da cena: o nome da instância não repete nenhum.</param>
    public static string Snippet(
        PartLibraryItem item, IReadOnlyDictionary<string, string> values, PartEditContext context,
        Point3D worldPoint, Vector3D worldNormal, IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (Refusal(item) is { } refusal) throw new InvalidOperationException(refusal);

        var (point, direction) = ToLocal(context, worldPoint, worldNormal);
        var arguments = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            [PointParameter(item)!.Name] = PlacementSource.Point(point),
            [DirectionParameter(item)!.Name] = PlacementSource.Vector(direction)
        };

        // Nunca o modelo pronto pelo nome: ele não traria o ponto e a direção.
        string expression = PartLibraryInsertion.PartExpression(item, arguments);
        string name = InstanceName(item.Name, existingNames);
        return $"var {name} = {expression}.Instance(\"{name}\");\n" +
               $"AddFeature({name}.Transformed({PlacementSource.Code(context.Placement)}));";
    }

    /// <summary>Os identificadores de um conjunto de arquivos, para <see cref="Snippet"/> não repetir nenhum.</summary>
    public static IReadOnlyCollection<string> NamesIn(IEnumerable<ScriptDocument> documents) =>
        documents.SelectMany(d => Regex.Matches(d.Text, @"\b[A-Za-z_]\w*\b").Select(m => m.Value))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary><c>FuroRebaixado</c> → <c>furo_rebaixado_1</c> (ou o próximo número livre).</summary>
    public static string InstanceName(string typeName, IEnumerable<string> existingNames)
    {
        var taken = existingNames as IReadOnlySet<string> ?? existingNames.ToHashSet(StringComparer.Ordinal);
        var snake = new StringBuilder();
        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(typeName[i - 1])) snake.Append('_');
            snake.Append(char.ToLowerInvariant(c));
        }
        for (int n = 1; ; n++)
        {
            string candidate = $"{snake}_{n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static string BaseType(string typeName) => typeName.Trim().TrimEnd('?');

    private static double Step(double value)
    {
        double rounded = Math.Round(value / PointStep) * PointStep;
        rounded = Math.Round(rounded, 2);
        return rounded == 0 ? 0 : rounded;
    }

    private static double Clean(double value)
    {
        double rounded = Math.Round(value, 9);
        return rounded == 0 ? 0 : rounded;
    }
}
