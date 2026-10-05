using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Domain.ValueObjects;

/// <summary>
/// A FORMA da seção varrida por um <c>SweepFeature</c>, e só a forma: um contorno fechado em 2D.
/// <para>
/// Quem diz onde a seção fica e para onde ela aponta é o caminho, não ela. Por isso este tipo
/// existe em vez de um <see cref="IProfile"/> solto: num caminho de vários trechos a seção é
/// transportada de estação em estação, e um perfil — que carrega plano, centro e normal próprios —
/// prometeria um posicionamento que seria ignorado.
/// </para>
/// <para>
/// Por dentro é um <see cref="IProfile"/> mesmo, usado só como gerador de contorno: os vértices
/// dele são projetados de volta no plano dele, o que desfaz o posicionamento e deixa apenas (u, v).
/// Isso reaproveita os perfis que já existem — inclusive a discretização do círculo por
/// ProfileSegments, que não pode ser decidida aqui.
/// </para>
/// </summary>
public sealed class SweepSection
{
    /// <summary>
    /// O perfil que gera o contorno. Só a FORMA dele é usada; o plano serve para projetar os
    /// vértices de volta a 2D, e some no resultado.
    /// </summary>
    public IProfile Outline { get; }

    private SweepSection(IProfile outline)
    {
        Outline = outline ?? throw new ArgumentNullException(nameof(outline));
    }

    /// <summary>Seção circular — o caso dominante (tubo, barra).</summary>
    public static SweepSection Circle(double radius) =>
        new(new CircleProfile(Point3D.Origin, radius, plane: WorkPlane.XY));

    /// <summary>Seção retangular centrada no caminho (tubo quadrado, barra chata, eletrocalha).</summary>
    public static SweepSection Rectangle(double width, double height) =>
        new(new RectangleProfile(Point3D.Origin, width, height, plane: WorkPlane.XY));

    /// <summary>Seção de contorno qualquer, dada por pontos (u, v).</summary>
    public static SweepSection Polygon(IEnumerable<(double u, double v)> points)
    {
        var verts = new List<Point3D>();
        foreach (var (u, v) in points) verts.Add(new Point3D(u, v, 0));
        return new SweepSection(new PolygonProfile(verts, plane: WorkPlane.XY));
    }

    /// <summary>
    /// Usa um perfil existente como forma. O plano dele é descartado (os vértices voltam para 2D
    /// por ele), então um perfil no meio do mundo e uma cópia dele na origem dão a MESMA seção.
    /// </summary>
    public static SweepSection FromProfile(IProfile profile) => new(profile);

    /// <summary>
    /// O contorno em (u, v). <paramref name="segments"/> é a discretização de curvas
    /// (ProfileSegments); perfis poligonais ignoram.
    /// </summary>
    public IReadOnlyList<(double U, double V)> GetPoints(int segments)
    {
        var world = Outline.GetVertices(segments);
        var plane = Outline.Plane;
        var points = new List<(double, double)>(world.Count);
        foreach (var w in world)
        {
            var local = plane.ToLocalSpace(w);
            points.Add((local.X, local.Y));
        }
        return points;
    }
}
