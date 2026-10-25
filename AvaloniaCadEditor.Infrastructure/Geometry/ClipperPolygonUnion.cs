using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using Clipper2Lib;

namespace AvaloniaCadEditor.Infrastructure.Geometry;

/// <summary>
/// <see cref="IPolygonUnion"/> com o Clipper2 (licença Boost). Cada polígono é virado para o sentido
/// anti-horário antes da união: com a regra NonZero, um triângulo invertido entre os outros abriria um furo
/// falso na sombra.
/// </summary>
public sealed class ClipperPolygonUnion : IPolygonUnion
{
    /// <summary>Casas decimais com que o Clipper arredonda as coordenadas (o mesmo 1e-4 da solda das malhas).</summary>
    private const int DecimalPrecision = 4;

    public IReadOnlyList<IReadOnlyList<Point3D>> Union(IEnumerable<IReadOnlyList<Point3D>> polygons)
    {
        var subject = new PathsD();
        foreach (var polygon in polygons)
        {
            if (polygon.Count < 3) continue;
            var path = new PathD(polygon.Select(p => new PointD(p.X, p.Y)));
            if (Clipper.Area(path) < 0) path.Reverse();
            subject.Add(path);
        }
        if (subject.Count == 0) return new List<IReadOnlyList<Point3D>>();

        return Clipper.Union(subject, new PathsD(), FillRule.NonZero, DecimalPrecision)
            .Select(path => (IReadOnlyList<Point3D>)path.Select(p => new Point3D(p.x, p.y, 0.0)).ToList())
            .ToList();
    }
}
