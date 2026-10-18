using System.Numerics;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;

namespace AvaloniaCadEditor.Harness.Views;

/// <summary>
/// Base ortonormal de uma vista ortográfica: <see cref="U"/> para a direita da tela, <see cref="V"/>
/// para cima e <see cref="W"/> para quem olha. Sai da própria <see cref="CameraState"/> (a mesma conta
/// do <c>Matrix4x4.CreateLookAt</c>), então vale para todas as vistas padrão, inclusive a Iso, e bate
/// com o que a tela mostra. Nas vistas ortogonais coincide com <see cref="ViewBasis"/>.
/// </summary>
public sealed class ProjectionBasis
{
    private const double Snap = 1e-6;   // ruído de float do seno/cosseno vira zero

    private ProjectionBasis(StandardView view, Vector3D u, Vector3D v, Vector3D w)
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

    /// <param name="azimuthOffset">Rotação em Z do referencial (radianos), como em <see cref="CameraState.SetStandardView"/>.</param>
    public static ProjectionBasis For(StandardView view, double azimuthOffset = 0)
    {
        var camera = new CameraState { Target = Vector3.Zero, Distance = 1f };
        camera.SetStandardView(view, (float)azimuthOffset);

        var w = Clean(camera.Position - camera.Target);
        var v = Clean(camera.Up);
        var u = Clean(v.CrossProduct(w));
        return new ProjectionBasis(view, u, v, w);
    }

    public P2 Project(Point3D point)
    {
        var p = point - Point3D.Origin;
        return new P2(p.DotProduct(U), p.DotProduct(V));
    }

    /// <summary>Profundidade na direção de quem olha (maior = mais perto da câmera).</summary>
    public double Depth(Point3D point) => (point - Point3D.Origin).DotProduct(W);

    /// <summary>Volta um ponto da folha para o referencial, na profundidade dada (inverso de <see cref="Project"/> + <see cref="Depth"/>).</summary>
    public Point3D Unproject(P2 point, double depth) =>
        Point3D.Origin + U * point.U + V * point.V + W * depth;

    private static Vector3D Clean(Vector3 v) => Clean(new Vector3D(v.X, v.Y, v.Z));

    private static Vector3D Clean(Vector3D v)
    {
        var n = v.Normalize();
        return new Vector3D(Round(n.X), Round(n.Y), Round(n.Z)).Normalize();
    }

    private static double Round(double x) => Math.Abs(x) < Snap ? 0 : Math.Abs(Math.Abs(x) - 1) < Snap ? Math.Sign(x) : x;
}
