using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Drafting;

/// <summary>Ponto no plano de uma vista: U para a direita da tela, V para cima.</summary>
public readonly record struct P2(double U, double V)
{
    public static P2 operator +(P2 a, P2 b) => new(a.U + b.U, a.V + b.V);
    public static P2 operator -(P2 a, P2 b) => new(a.U - b.U, a.V - b.V);
    public static P2 operator *(P2 a, double s) => new(a.U * s, a.V * s);

    public double Length => Math.Sqrt(U * U + V * V);

    public double DistanceTo(P2 other) => (this - other).Length;
}

/// <summary>
/// Base ortonormal de uma vista padrão, em coordenadas do referencial da peça (<see cref="PartFrame"/>):
/// <see cref="U"/> aponta para a direita da tela, <see cref="V"/> para cima e <see cref="W"/> para
/// quem olha. É a mesma orientação que <see cref="CameraState.SetStandardView"/> produz, então o
/// desenho bate com o que a câmera mostra.
/// </summary>
public sealed class ViewBasis
{
    private ViewBasis(StandardView view, Vector3D u, Vector3D v, Vector3D w)
    {
        View = view;
        U = u;
        V = v;
        W = w;
    }

    public StandardView View { get; }
    public Vector3D U { get; }
    public Vector3D V { get; }
    public Vector3D W { get; }

    /// <summary>A base da vista, ou <c>null</c> para vistas que não são ortogonais aos eixos (Iso).</summary>
    public static ViewBasis? For(StandardView view) => view switch
    {
        StandardView.Front => new(view, Vector3D.UnitX, Vector3D.UnitZ, new Vector3D(0, -1, 0)),
        StandardView.Back => new(view, new Vector3D(-1, 0, 0), Vector3D.UnitZ, Vector3D.UnitY),
        StandardView.Right => new(view, Vector3D.UnitY, Vector3D.UnitZ, Vector3D.UnitX),
        StandardView.Left => new(view, new Vector3D(0, -1, 0), Vector3D.UnitZ, new Vector3D(-1, 0, 0)),
        StandardView.Top => new(view, Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ),
        StandardView.Bottom => new(view, Vector3D.UnitX, new Vector3D(0, -1, 0), new Vector3D(0, 0, -1)),
        _ => null
    };

    public P2 Project(Point3D framePoint)
    {
        var p = framePoint - Point3D.Origin;
        return new P2(p.DotProduct(U), p.DotProduct(V));
    }

    /// <summary>Profundidade do ponto na direção de quem olha (maior = mais perto da câmera).</summary>
    public double Depth(Point3D framePoint) => (framePoint - Point3D.Origin).DotProduct(W);

    /// <summary>Volta um ponto do plano da vista para o referencial, na profundidade dada.</summary>
    public Point3D Unproject(P2 point, double depth) =>
        Point3D.Origin + U * point.U + V * point.V + W * depth;
}
