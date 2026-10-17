using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

/// <summary>
/// "Face" de uma malha do jeito que o usuário a vê: a região de triângulos limitada pelas quinas que o
/// wireframe desenha. Atravessa só aresta suave, então a lateral facetada de um cilindro é uma face só e as
/// tampas ficam de fora. Um fillet tangente se funde com a face vizinha, como na tela.
/// </summary>
public static class MeshFaces
{
    /// <summary>Quão parecida tem de ser a normal para reencontrar a face depois de retesselar.</summary>
    public const double SeedNormalMinDot = 0.9;

    public const int NotFound = -1;

    public static IReadOnlySet<int> Region(MeshEdgeGraph graph, int seedTriangle)
    {
        var region = new HashSet<int>();
        if (seedTriangle < 0 || seedTriangle >= graph.TriangleCount) return region;

        var queue = new Queue<int>();
        region.Add(seedTriangle);
        queue.Enqueue(seedTriangle);
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            foreach (int e in graph.EdgesOf(t))
            {
                if (e == MeshEdgeGraph.NoEdge) continue;
                var edge = graph.Edges[e];
                if (!graph.IsSmooth(edge)) continue;
                foreach (int neighbor in edge.Triangles)
                {
                    if (region.Add(neighbor)) queue.Enqueue(neighbor);
                }
            }
        }
        return region;
    }

    /// <summary>
    /// O triângulo mais perto de <paramref name="point"/> cuja normal concorda com <paramref name="normal"/>.
    /// É como uma face escolhida antes é reencontrada quando o sólido muda e a malha é refeita: o índice do
    /// triângulo não sobrevive à retesselação, o ponto e a normal sim.
    /// </summary>
    public static int FindSeed(MeshEdgeGraph graph, Point3D point, Vector3D normal)
    {
        var wanted = normal.Normalize();
        int best = NotFound;
        double bestDistance = double.MaxValue;
        for (int t = 0; t < graph.TriangleCount; t++)
        {
            if (graph.Normal(t).DotProduct(wanted) < SeedNormalMinDot) continue;
            var (a, b, c) = graph.Triangle(t);
            double d = point.DistanceTo(ClosestPointOnTriangle(point, a, b, c));
            if (d < bestDistance)
            {
                bestDistance = d;
                best = t;
            }
        }
        return best;
    }

    /// <summary>Ponto do triângulo mais perto de <paramref name="p"/> (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    private static Point3D ClosestPointOnTriangle(Point3D p, Point3D a, Point3D b, Point3D c)
    {
        Vector3D ab = b - a, ac = c - a, ap = p - a;
        double d1 = ab.DotProduct(ap), d2 = ac.DotProduct(ap);
        if (d1 <= 0 && d2 <= 0) return a;

        Vector3D bp = p - b;
        double d3 = ab.DotProduct(bp), d4 = ac.DotProduct(bp);
        if (d3 >= 0 && d4 <= d3) return b;

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));

        Vector3D cp = p - c;
        double d5 = ab.DotProduct(cp), d6 = ac.DotProduct(cp);
        if (d6 >= 0 && d5 <= d6) return c;

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));

        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        double denom = va + vb + vc;
        if (Math.Abs(denom) < 1e-18) return a;
        double v = vb / denom, w = vc / denom;
        return a + ab * v + ac * w;
    }
}
