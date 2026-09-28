using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Infrastructure.Engines.MultiAxis;

/// <summary>
/// Ajuste de curvas 2D usado para decidir se um loop de borda deduzido da malha
/// deve virar uma feature paramétrica (círculo) ou permanecer poligonal.
/// </summary>
public static class LoopFitting
{
    /// <summary>
    /// Ajuste de círculo por mínimos quadrados (método de Kåsa).
    /// https://en.wikipedia.org/wiki/Circle_fitting
    /// </summary>
    public static (double CenterU, double CenterV, double Radius, double Error) FitCircle(IReadOnlyList<(double U, double V)> points)
    {
        int n = points.Count;
        if (n < 3) return (0, 0, 0, double.MaxValue);

        double sx = 0.0, sy = 0.0;
        double sxx = 0.0, syy = 0.0, sxy = 0.0;
        double sxxx = 0.0, syyy = 0.0, sxxy = 0.0, sxyy = 0.0;

        foreach (var p in points)
        {
            double x = p.U;
            double y = p.V;
            sx += x; sy += y;
            sxx += x * x; syy += y * y; sxy += x * y;
            sxxx += x * x * x; syyy += y * y * y;
            sxxy += x * x * y; sxyy += x * y * y;
        }

        double c1 = n * sxx - sx * sx;
        double c2 = n * sxy - sx * sy;
        double c3 = n * syy - sy * sy;
        double d1 = 0.5 * (n * sxxx + n * sxyy - sxx * sx - syy * sx);
        double d2 = 0.5 * (n * syyy + n * sxxy - syy * sy - sxx * sy);

        double det = c1 * c3 - c2 * c2;
        if (Math.Abs(det) < 1e-10) return (0, 0, 0, double.MaxValue); // Colineares (Não é círculo)

        double a = (d1 * c3 - d2 * c2) / det;
        double b = (c1 * d2 - c2 * d1) / det;
        double rSq = (sxx - 2 * a * sx + n * a * a + syy - 2 * b * sy + n * b * b) / n;
        double radius = Math.Sqrt(rSq);

        double errorSqSum = 0;
        foreach (var p in points)
        {
            double d = Math.Sqrt((p.U - a) * (p.U - a) + (p.V - b) * (p.V - b));
            errorSqSum += (d - radius) * (d - radius);
        }
        double avgError = Math.Sqrt(errorSqSum / n);

        return (a, b, radius, avgError);
    }

    /// <summary>
    /// Simplifica um loop removendo vértices consecutivos quase coincidentes,
    /// preservando o formato geral. Tolerância em unidades ao quadrado.
    /// </summary>
    public static List<Point3D> SimplifyLoop(List<Point3D> originalLoop, double toleranceSq = 0.05)
    {
        if (originalLoop.Count == 0) return originalLoop;

        var simplified = new List<Point3D> { originalLoop[0] };
        for (int i = 1; i < originalLoop.Count; i++)
        {
            var last = simplified[^1];
            if (DistSq(originalLoop[i], last) > toleranceSq)
                simplified.Add(originalLoop[i]);
        }

        // Evita loop degenerado (primeiro e último quase coincidentes após simplificação).
        if (simplified.Count > 3 && DistSq(simplified[0], simplified[^1]) <= toleranceSq)
            simplified.RemoveAt(simplified.Count - 1);

        return simplified;
    }

    private static double DistSq(Point3D a, Point3D b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
