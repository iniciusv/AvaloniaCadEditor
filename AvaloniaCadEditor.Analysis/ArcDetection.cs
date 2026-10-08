namespace AvaloniaCadEditor.Analysis;

/// <summary>Um arco circular detectado num perfil poligonal, no plano (u, v) do grupo.</summary>
public sealed record ArcInfo(double CenterU, double CenterV, double Radius);

/// <summary>
/// Detecta arcos em perfis poligonais: sequências de vértices consecutivos cujos circuncentros
/// concordam (a aproximação poligonal de um arco), reajustadas por mínimos quadrados.
/// </summary>
public static class ArcDetection
{
    /// <summary>Os arcos de um laço de pontos (u, v) — a coordenada Z dos pontos é ignorada.</summary>
    public static List<ArcInfo> DetectArcs(IReadOnlyList<(double U, double V)> loop, AnalysisOptions options)
    {
        var result = new List<ArcInfo>();
        int n = loop.Count;
        if (n < options.MinArcRunLength + 2) return result;

        var centers = new (double U, double V)?[n];
        var radii = new double[n];
        for (int i = 1; i < n - 1; i++)
        {
            var c = CircumCenter(loop[i - 1], loop[i], loop[i + 1]);
            if (c == null) continue;
            centers[i] = c;
            radii[i] = Distance(c.Value, loop[i]);
        }

        int runStart = centers[1] != null ? 1 : -1;
        for (int i = 2; i < n - 1; i++)
        {
            bool continuesRun = false;
            if (runStart >= 0 && centers[i] != null && centers[i - 1] != null)
            {
                double centerDist = Distance(centers[i]!.Value, centers[i - 1]!.Value);
                double refRadius = Math.Max(radii[i], 1e-6);
                double radiusDiff = Math.Abs(radii[i] - radii[i - 1]);
                continuesRun = centerDist / refRadius < options.ArcCenterTolerance
                               && radiusDiff / refRadius < options.ArcRadiusTolerance;
            }

            if (continuesRun) continue;

            if (runStart >= 0) FinalizeRun(runStart, i - 1, centers, loop, options, result);
            runStart = centers[i] != null ? i : -1;
        }
        if (runStart >= 0) FinalizeRun(runStart, n - 2, centers, loop, options, result);

        return result;
    }

    /// <summary>
    /// Transforma uma sequência confirmada de circuncentros num só (centro, raio). Em vez de tirar a
    /// média dos circuncentros — numericamente instável justo onde o arco fica quase tangente a uma
    /// aresta reta, porque as trincas ficam quase colineares —, reajusta um círculo por mínimos
    /// quadrados sobre todos os vértices cobertos. Isso é bem menos sensível ao arredondamento dos
    /// vértices, então dois arcos que deveriam ter o mesmo centro (as duas pontas de um rasgo) caem a
    /// um milésimo um do outro em vez de virarem um falso quase-alinhamento.
    /// </summary>
    private static void FinalizeRun(int start, int end, (double U, double V)?[] centers, IReadOnlyList<(double U, double V)> loop,
        AnalysisOptions options, List<ArcInfo> result)
    {
        int count = end - start + 1;
        if (count < options.MinArcRunLength) return;

        // centers[i] é o circuncentro de loop[i-1..i+1], então a sequência cobre loop[start-1 .. end+1].
        int firstVertex = Math.Max(0, start - 1);
        int lastVertex = Math.Min(loop.Count - 1, end + 1);
        var points = loop.Skip(firstVertex).Take(lastVertex - firstVertex + 1).ToList();

        var fit = FitCircleLeastSquares(points);
        if (fit != null)
        {
            result.Add(fit);
            return;
        }

        // Ajuste degenerado (não deveria acontecer com um arco de verdade): volta à média.
        double cu = 0, cv = 0, r = 0;
        for (int i = start; i <= end; i++)
        {
            cu += centers[i]!.Value.U;
            cv += centers[i]!.Value.V;
            r += Distance(centers[i]!.Value, loop[i]);
        }
        result.Add(new ArcInfo(cu / count, cv / count, r / count));
    }

    /// <summary>Ajuste algébrico (Kåsa) por mínimos quadrados: resolve x²+y²+Dx+Ey+F=0 sobre os pontos;
    /// centro = (-D/2, -E/2) e raio a partir de F.</summary>
    public static ArcInfo? FitCircleLeastSquares(IReadOnlyList<(double U, double V)> points)
    {
        if (points.Count < 3) return null;

        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0, sz = 0;
        int n = points.Count;
        foreach (var (x, y) in points)
        {
            double z = x * x + y * y;
            sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
            sxz += x * z; syz += y * z; sz += z;
        }

        if (!Solve3x3(sxx, sxy, sx, -sxz, sxy, syy, sy, -syz, sx, sy, n, -sz, out double d, out double e, out double f))
        {
            return null;
        }

        double radiusSquared = (d * d + e * e) / 4.0 - f;
        if (radiusSquared <= 0) return null;
        return new ArcInfo(-d / 2.0, -e / 2.0, Math.Sqrt(radiusSquared));
    }

    private static bool Solve3x3(
        double a00, double a01, double a02, double b0,
        double a10, double a11, double a12, double b1,
        double a20, double a21, double a22, double b2,
        out double x, out double y, out double z)
    {
        double det = a00 * (a11 * a22 - a12 * a21)
                   - a01 * (a10 * a22 - a12 * a20)
                   + a02 * (a10 * a21 - a11 * a20);

        if (Math.Abs(det) < 1e-9)
        {
            x = y = z = 0;
            return false;
        }

        x = (b0 * (a11 * a22 - a12 * a21) - a01 * (b1 * a22 - a12 * b2) + a02 * (b1 * a21 - a11 * b2)) / det;
        y = (a00 * (b1 * a22 - a12 * b2) - b0 * (a10 * a22 - a12 * a20) + a02 * (a10 * b2 - b1 * a20)) / det;
        z = (a00 * (a11 * b2 - b1 * a21) - a01 * (a10 * b2 - b1 * a20) + b0 * (a10 * a21 - a11 * a20)) / det;
        return true;
    }

    private static double Distance((double U, double V) a, (double U, double V) b) =>
        Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V));

    private static (double U, double V)? CircumCenter((double U, double V) a, (double U, double V) b, (double U, double V) c)
    {
        double ax = a.U, ay = a.V, bx = b.U, by = b.V, cx = c.U, cy = c.V;
        double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (Math.Abs(d) < 1e-9) return null; // colineares, sem círculo

        double ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
        double uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
        return (ux, uy);
    }
}
