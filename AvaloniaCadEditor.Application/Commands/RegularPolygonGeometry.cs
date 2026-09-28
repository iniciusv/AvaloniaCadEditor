using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Cálculo puro dos vértices de um polígono regular inscrito num círculo, usado pela
/// ferramenta Polygon. Mantido separado do comando para ser testável isoladamente.
/// </summary>
public static class RegularPolygonGeometry
{
    public const int MinSides = 3;
    public const int MaxSides = 64;

    /// <summary>
    /// Retorna os vértices (em sentido anti-horário) de um polígono regular de 'sides' lados,
    /// centrado em 'center', com o primeiro vértice a 'radius' de distância no ângulo 'angle'
    /// (radianos, medido a partir do eixo X local).
    /// </summary>
    public static List<Point3D> GetVertices(Point3D center, double radius, int sides, double angle)
    {
        sides = Math.Clamp(sides, MinSides, MaxSides);
        var vertices = new List<Point3D>(sides);

        for (int i = 0; i < sides; i++)
        {
            double theta = angle + 2.0 * Math.PI * i / sides;
            vertices.Add(new Point3D(
                center.X + radius * Math.Cos(theta),
                center.Y + radius * Math.Sin(theta),
                center.Z));
        }

        return vertices;
    }
}
