using System.Collections.Generic;

namespace AvaloniaCadEditor.Domain.ValueObjects;

/// <summary>
/// De onde veio uma referência do plano de trabalho: o contorno de uma face que está no plano, o
/// corte de um sólido que atravessa o plano, ou a projeção de uma face/sólido que o usuário escolheu.
/// </summary>
public enum PlaneReferenceKind
{
    Coplanar,
    Section,
    Projected
}

/// <summary>
/// Geometria de referência transitória de um plano de trabalho: uma polilinha em coordenadas locais do
/// plano (Z = 0), tirada da malha de um sólido visível. Não é entidade do esboço — não é salva nem
/// selecionável; só é desenhada e serve de snap enquanto se desenha no plano.
/// </summary>
/// <param name="Center">Preenchido quando o laço é circular (boca de furo, corte de cilindro).</param>
public sealed record PlaneReference(
    PlaneReferenceKind Kind,
    IReadOnlyList<Point3D> Points,
    bool Closed,
    Point3D? Center = null,
    double? Radius = null)
{
    public bool IsCircle => Center.HasValue && Radius.HasValue;

    /// <summary>Menor distância, no plano (X/Y locais), de <paramref name="local"/> até a polilinha.</summary>
    public double DistanceTo(Point3D local)
    {
        if (Points.Count == 0) return double.MaxValue;
        if (Points.Count == 1) return Distance(local, Points[0]);

        double best = double.MaxValue;
        int segments = Closed ? Points.Count : Points.Count - 1;
        for (int i = 0; i < segments; i++)
        {
            var a = Points[i];
            var b = Points[(i + 1) % Points.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;
            double t = lenSq < 1e-18 ? 0 : ((local.X - a.X) * dx + (local.Y - a.Y) * dy) / lenSq;
            t = System.Math.Clamp(t, 0.0, 1.0);
            best = System.Math.Min(best, Distance(local, new Point3D(a.X + t * dx, a.Y + t * dy, 0)));
        }
        return best;
    }

    private static double Distance(Point3D p, Point3D q) =>
        System.Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
}
