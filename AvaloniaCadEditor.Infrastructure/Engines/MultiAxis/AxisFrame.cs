using System;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Infrastructure.Engines.MultiAxis;

/// <summary>
/// Referencial local (U, V, W) usado para varrer a malha ao longo de um dos três eixos
/// principais. W é o eixo de varredura (o eixo de extrusão); (U, V) é o plano perpendicular
/// onde as seções 2D são reconstruídas. As três combinações são permutações cíclicas
/// destras de (X, Y, Z), então a orientação (sentido de giro do polígono) é sempre consistente:
///   Z: (U,V,W) = (X,Y,Z)
///   X: (U,V,W) = (Y,Z,X)
///   Y: (U,V,W) = (Z,X,Y)
/// </summary>
public sealed class AxisFrame
{
    public static readonly AxisFrame Z = new("Z", p => (p.X, p.Y, p.Z), (u, v, w) => new Point3D(u, v, w), new Vector3D(0, 0, 1));
    public static readonly AxisFrame X = new("X", p => (p.Y, p.Z, p.X), (u, v, w) => new Point3D(w, u, v), new Vector3D(1, 0, 0));
    public static readonly AxisFrame Y = new("Y", p => (p.Z, p.X, p.Y), (u, v, w) => new Point3D(v, w, u), new Vector3D(0, 1, 0));

    public static readonly AxisFrame[] All = { Z, X, Y };

    public string Name { get; }
    public Vector3D Axis { get; }

    private readonly Func<Point3D, (double U, double V, double W)> _toLocal;
    private readonly Func<double, double, double, Point3D> _toWorld;

    private AxisFrame(string name, Func<Point3D, (double, double, double)> toLocal, Func<double, double, double, Point3D> toWorld, Vector3D axis)
    {
        Name = name;
        _toLocal = toLocal;
        _toWorld = toWorld;
        Axis = axis;
    }

    public (double U, double V, double W) ToLocal(Point3D worldPoint) => _toLocal(worldPoint);

    public Point3D ToWorld(double u, double v, double w) => _toWorld(u, v, w);
}
