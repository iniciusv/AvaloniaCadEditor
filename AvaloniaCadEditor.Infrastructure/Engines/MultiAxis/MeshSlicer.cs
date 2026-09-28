using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Infrastructure.Engines.MultiAxis;

/// <summary>
/// Fatia uma <see cref="TriangleMesh"/> ao longo de um <see cref="AxisFrame"/>, generalizando
/// a técnica de "extrusão da face inferior" (que só varria o eixo Z) para qualquer um dos três
/// eixos principais, e permitindo múltiplos degraus (camadas) ao longo do eixo escolhido.
/// </summary>
public static class MeshSlicer
{
    /// <summary>
    /// Encontra os níveis "de tampa" ao longo do eixo: coordenadas W onde há triângulos
    /// inteiramente planos (perpendiculares ao eixo), mais os extremos min/max da malha.
    /// Esses níveis definem os limites das camadas (slabs) a serem fatiadas.
    /// </summary>
    public static List<double> FindCapLevels(TriangleMesh mesh, AxisFrame axis, double tolerance)
    {
        var levels = new List<double>();
        double globalMin = double.MaxValue, globalMax = double.MinValue;

        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            double w0 = axis.ToLocal(PositionOf(mesh, mesh.Indices[i])).W;
            double w1 = axis.ToLocal(PositionOf(mesh, mesh.Indices[i + 1])).W;
            double w2 = axis.ToLocal(PositionOf(mesh, mesh.Indices[i + 2])).W;

            if (w0 < globalMin) globalMin = w0; if (w0 > globalMax) globalMax = w0;
            if (w1 < globalMin) globalMin = w1; if (w1 > globalMax) globalMax = w1;
            if (w2 < globalMin) globalMin = w2; if (w2 > globalMax) globalMax = w2;

            if (Math.Abs(w0 - w1) <= tolerance && Math.Abs(w1 - w2) <= tolerance && Math.Abs(w0 - w2) <= tolerance)
            {
                AddLevel(levels, (w0 + w1 + w2) / 3.0, tolerance);
            }
        }

        if (globalMin > globalMax) return levels;

        AddLevel(levels, globalMin, tolerance);
        AddLevel(levels, globalMax, tolerance);

        levels.Sort();
        return levels;
    }

    /// <summary>
    /// Extensão total da malha (min, max) ao longo do eixo — usada para extrudar a silhueta
    /// pelo comprimento inteiro da peça.
    /// </summary>
    public static (double Min, double Max) GetAxisRange(TriangleMesh mesh, AxisFrame axis)
    {
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < mesh.Positions.Length; i += 3)
        {
            double w = axis.ToLocal(new Point3D(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2])).W;
            if (w < min) min = w;
            if (w > max) max = w;
        }
        return min <= max ? (min, max) : (0.0, 0.0);
    }

    /// <summary>
    /// Limites (U, V) de toda a malha nesse eixo — usados para construir o "envelope"
    /// (caixa delimitadora) de onde o sólido de cada camada é subtraído.
    /// </summary>
    public static (double MinU, double MaxU, double MinV, double MaxV) GetUVBounds(TriangleMesh mesh, AxisFrame axis)
    {
        double minU = double.MaxValue, maxU = double.MinValue;
        double minV = double.MaxValue, maxV = double.MinValue;

        for (int i = 0; i < mesh.Positions.Length; i += 3)
        {
            var (u, v, _) = axis.ToLocal(new Point3D(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2]));
            if (u < minU) minU = u; if (u > maxU) maxU = u;
            if (v < minV) minV = v; if (v > maxV) maxV = v;
        }

        return minU <= maxU ? (minU, maxU, minV, maxV) : (0.0, 0.0, 0.0, 0.0);
    }

    /// <summary>
    /// Fração da área da malha cuja normal é aproximadamente paralela (tampas) ou perpendicular
    /// (paredes laterais) ao eixo — um indicador de quão "2.5D" (varrível) a peça é nesse eixo.
    /// </summary>
    public static double Prismaticity(TriangleMesh mesh, AxisFrame axis)
    {
        const double parallelThreshold = 0.98;
        const double perpendicularThreshold = 0.06;

        double totalWeight = 0, matchedWeight = 0;

        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var p0 = PositionOf(mesh, mesh.Indices[i]);
            var p1 = PositionOf(mesh, mesh.Indices[i + 1]);
            var p2 = PositionOf(mesh, mesh.Indices[i + 2]);

            var cross = (p1 - p0).CrossProduct(p2 - p0);
            double weight = cross.Length; // proporcional à área (2x), serve para ponderar
            if (weight < 1e-12) continue;

            double dot = Math.Abs(cross.Normalize().DotProduct(axis.Axis));

            totalWeight += weight;
            if (dot >= parallelThreshold || dot <= perpendicularThreshold)
                matchedWeight += weight;
        }

        return totalWeight > 1e-9 ? matchedWeight / totalWeight : 0.0;
    }

    /// <summary>
    /// Fatia a malha no plano W = level (perpendicular ao eixo) via "marching triangles":
    /// para cada triângulo que cruza o plano, calcula o segmento 2D de interseção e encadeia
    /// os segmentos em loops fechados no plano (U, V).
    /// </summary>
    public static List<List<(double U, double V)>> Slice(TriangleMesh mesh, AxisFrame axis, double level)
    {
        var segments = new List<((double U, double V) A, (double U, double V) B)>();

        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var (u0, v0, w0) = axis.ToLocal(PositionOf(mesh, mesh.Indices[i]));
            var (u1, v1, w1) = axis.ToLocal(PositionOf(mesh, mesh.Indices[i + 1]));
            var (u2, v2, w2) = axis.ToLocal(PositionOf(mesh, mesh.Indices[i + 2]));

            // Desloca vértices exatamente sobre o plano por um épsilon determinístico,
            // para que a classificação de sinal das arestas nunca seja ambígua (evita
            // contagem duplicada/perdida nos casos degenerados de vértice-no-plano).
            const double eps = 1e-9;
            double d0 = w0 - level; if (d0 == 0.0) d0 = eps;
            double d1 = w1 - level; if (d1 == 0.0) d1 = eps;
            double d2 = w2 - level; if (d2 == 0.0) d2 = eps;

            bool sign0 = d0 > 0, sign1 = d1 > 0, sign2 = d2 > 0;
            if (sign0 == sign1 && sign1 == sign2) continue; // triângulo não cruza o plano

            var crossings = new List<(double U, double V)>(2);
            TryAddCrossing(crossings, d0, d1, u0, v0, u1, v1);
            TryAddCrossing(crossings, d1, d2, u1, v1, u2, v2);
            TryAddCrossing(crossings, d2, d0, u2, v2, u0, v0);

            if (crossings.Count == 2)
                segments.Add((crossings[0], crossings[1]));
        }

        var loops = ChainSegmentsIntoLoops(segments);
        for (int i = 0; i < loops.Count; i++)
        {
            loops[i] = RemoveCollinearPoints(loops[i]);
        }
        return loops;
    }

    /// <summary>
    /// Remove vértices que estão (quase) exatamente sobre o segmento entre seus vizinhos. A
    /// tesselação de uma face plana quase sempre inclui uma diagonal interna (2 triângulos por
    /// face), e essa diagonal cruza o plano de corte num ponto colinear com os dois extremos
    /// reais da aresta — sem isso, cada aresta "reta" viraria 2 segmentos com um vértice espúrio
    /// no meio.
    /// </summary>
    private static List<(double U, double V)> RemoveCollinearPoints(List<(double U, double V)> loop)
    {
        if (loop.Count <= 3) return loop;

        var result = new List<(double U, double V)>();
        int n = loop.Count;
        for (int i = 0; i < n; i++)
        {
            var prev = loop[(i - 1 + n) % n];
            var curr = loop[i];
            var next = loop[(i + 1) % n];

            double baseLength = Math.Sqrt((next.U - prev.U) * (next.U - prev.U) + (next.V - prev.V) * (next.V - prev.V));
            if (baseLength < 1e-9)
            {
                result.Add(curr);
                continue;
            }

            double cross = (curr.U - prev.U) * (next.V - prev.V) - (curr.V - prev.V) * (next.U - prev.U);
            double distanceFromLine = Math.Abs(cross) / baseLength;
            if (distanceFromLine > 1e-3)
            {
                result.Add(curr);
            }
        }

        return result.Count >= 3 ? result : loop;
    }

    /// <summary>
    /// Determina o aninhamento dos loops de uma fatia: contornos externos (profundidade par)
    /// e os furos diretamente contidos neles (profundidade ímpar). Ilhas sólidas dentro de
    /// furos (profundidade par &gt; 0) tornam-se regiões próprias, corretamente unidas depois.
    /// </summary>
    public static List<(List<(double U, double V)> Outer, List<List<(double U, double V)>> Holes)> BuildRegions(
        List<List<(double U, double V)>> loops)
    {
        var result = new List<(List<(double U, double V)> Outer, List<List<(double U, double V)>> Holes)>();
        int n = loops.Count;
        if (n == 0) return result;

        var areas = loops.Select(l => Math.Abs(SignedArea(l))).ToArray();
        var centroids = loops.Select(Centroid).ToArray();
        var order = Enumerable.Range(0, n).OrderByDescending(i => areas[i]).ToArray();

        var parent = new int[n];
        var depth = new int[n];
        for (int i = 0; i < n; i++) parent[i] = -1;

        for (int oi = 0; oi < n; oi++)
        {
            int i = order[oi];
            int bestParent = -1;
            double bestArea = double.MaxValue;

            for (int oj = 0; oj < oi; oj++)
            {
                int j = order[oj];
                if (areas[j] <= areas[i]) continue; // só um loop maior pode ser "pai"
                if (areas[j] < bestArea && PointInPolygon(loops[j], centroids[i]))
                {
                    bestParent = j;
                    bestArea = areas[j];
                }
            }

            parent[i] = bestParent;
            depth[i] = bestParent == -1 ? 0 : depth[bestParent] + 1;
        }

        for (int i = 0; i < n; i++)
        {
            if (depth[i] % 2 != 0) continue; // é furo; será anexado ao seu pai abaixo

            var holes = new List<List<(double U, double V)>>();
            for (int k = 0; k < n; k++)
            {
                if (parent[k] == i) holes.Add(loops[k]);
            }
            result.Add((loops[i], holes));
        }

        return result;
    }

    private static void TryAddCrossing(List<(double U, double V)> crossings, double dA, double dB,
        double uA, double vA, double uB, double vB)
    {
        if (dA * dB >= 0.0) return; // mesmo lado do plano, sem cruzamento nesta aresta
        double t = dA / (dA - dB);
        crossings.Add((uA + t * (uB - uA), vA + t * (vB - vA)));
    }

    private static List<List<(double U, double V)>> ChainSegmentsIntoLoops(
        List<((double U, double V) A, (double U, double V) B)> segments)
    {
        var loops = new List<List<(double U, double V)>>();
        var used = new bool[segments.Count];
        const double epsilonSq = 1e-4;

        for (int i = 0; i < segments.Count; i++)
        {
            if (used[i]) continue;

            var loop = new List<(double U, double V)> { segments[i].A };
            var current = segments[i].B;
            used[i] = true;
            bool closed = false;

            while (true)
            {
                loop.Add(current);
                bool foundNext = false;

                for (int j = 0; j < segments.Count; j++)
                {
                    if (used[j]) continue;

                    if (DistSq(segments[j].A, current) < epsilonSq)
                    {
                        current = segments[j].B;
                        used[j] = true;
                        foundNext = true;
                        break;
                    }
                    if (DistSq(segments[j].B, current) < epsilonSq)
                    {
                        current = segments[j].A;
                        used[j] = true;
                        foundNext = true;
                        break;
                    }
                }

                if (!foundNext) break;

                if (DistSq(loop[0], current) < epsilonSq)
                {
                    closed = true;
                    break;
                }
            }

            if (closed && loop.Count >= 3) loops.Add(loop);
        }

        return loops;
    }

    private static void AddLevel(List<double> levels, double value, double tolerance)
    {
        for (int i = 0; i < levels.Count; i++)
        {
            if (Math.Abs(levels[i] - value) <= tolerance) return; // nível equivalente já existe
        }
        levels.Add(value);
    }

    private static double SignedArea(List<(double U, double V)> loop)
    {
        double area = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            area += a.U * b.V - b.U * a.V;
        }
        return area * 0.5;
    }

    private static (double U, double V) Centroid(List<(double U, double V)> loop)
    {
        double u = 0, v = 0;
        foreach (var p in loop) { u += p.U; v += p.V; }
        return (u / loop.Count, v / loop.Count);
    }

    public static bool PointInPolygon(List<(double U, double V)> polygon, (double U, double V) point)
    {
        bool inside = false;
        int count = polygon.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var vi = polygon[i];
            var vj = polygon[j];
            if (((vi.V > point.V) != (vj.V > point.V)) &&
                (point.U < (vj.U - vi.U) * (point.V - vi.V) / (vj.V - vi.V) + vi.U))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// Como <see cref="PointInPolygon"/>, mas também considera "dentro" um ponto bem próximo de
    /// uma aresta (dentro de <paramref name="tolerance"/>) — evita falso-negativo por jitter
    /// numérico quando um vértice da malha está exatamente sobre o contorno do perfil base.
    /// </summary>
    public static bool ContainsPointWithTolerance(List<(double U, double V)> polygon, (double U, double V) point, double tolerance)
    {
        if (PointInPolygon(polygon, point)) return true;

        int count = polygon.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            if (DistanceToSegment(point, polygon[j], polygon[i]) <= tolerance) return true;
        }
        return false;
    }

    private static double DistanceToSegment((double U, double V) p, (double U, double V) a, (double U, double V) b)
    {
        double dx = b.U - a.U, dy = b.V - a.V;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-12)
        {
            double du = p.U - a.U, dv = p.V - a.V;
            return Math.Sqrt(du * du + dv * dv);
        }

        double t = ((p.U - a.U) * dx + (p.V - a.V) * dy) / lenSq;
        t = Math.Max(0.0, Math.Min(1.0, t));
        double projU = a.U + t * dx, projV = a.V + t * dy;
        double ddu = p.U - projU, ddv = p.V - projV;
        return Math.Sqrt(ddu * ddu + ddv * ddv);
    }

    private static double DistSq((double U, double V) a, (double U, double V) b)
    {
        double du = a.U - b.U, dv = a.V - b.V;
        return du * du + dv * dv;
    }

    private static Point3D PositionOf(TriangleMesh mesh, uint index)
    {
        int b = (int)index * 3;
        return new Point3D(mesh.Positions[b], mesh.Positions[b + 1], mesh.Positions[b + 2]);
    }
}
