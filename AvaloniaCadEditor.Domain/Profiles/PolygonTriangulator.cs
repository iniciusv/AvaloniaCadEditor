using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

/// <summary>
/// Triangulação de um polígono simples por corte de orelhas, em coordenadas (X, Y). Serve para polígonos
/// côncavos, como o quadrado com uma mordida de círculo: o leque a partir do centro pintaria fora deles.
/// </summary>
public static class PolygonTriangulator
{
    private const double Epsilon = 1e-12;

    /// <summary>
    /// Índices dos triângulos (três por triângulo) sobre <paramref name="vertices"/>, sempre anti-horários
    /// no plano XY. Vazio se houver menos de três vértices; incompleto se o polígono se cruzar.
    /// </summary>
    public static IReadOnlyList<int> Triangulate(IReadOnlyList<Point3D> vertices)
    {
        var result = new List<int>();
        int n = vertices.Count;
        if (n < 3) return result;

        var remaining = new List<int>(n);
        for (int i = 0; i < n; i++) remaining.Add(i);
        if (SignedArea(vertices) < 0) remaining.Reverse();

        while (remaining.Count > 3)
        {
            if (!ClipOneEar(vertices, remaining, result) && !DropFlatVertex(vertices, remaining))
                return result; // polígono que se cruza: devolve o que deu
        }

        if (Cross(vertices[remaining[0]], vertices[remaining[1]], vertices[remaining[2]]) > Epsilon)
        {
            result.Add(remaining[0]);
            result.Add(remaining[1]);
            result.Add(remaining[2]);
        }
        return result;
    }

    private static bool ClipOneEar(IReadOnlyList<Point3D> v, List<int> remaining, List<int> result)
    {
        int count = remaining.Count;
        for (int i = 0; i < count; i++)
        {
            int a = remaining[(i - 1 + count) % count];
            int b = remaining[i];
            int c = remaining[(i + 1) % count];

            if (Cross(v[a], v[b], v[c]) <= Epsilon) continue; // côncavo ou chato

            bool blocked = false;
            foreach (int k in remaining)
            {
                if (k == a || k == b || k == c) continue;
                var p = v[k];
                if (SamePoint(p, v[a]) || SamePoint(p, v[b]) || SamePoint(p, v[c])) continue;
                if (InsideOrOnTriangle(p, v[a], v[b], v[c])) { blocked = true; break; }
            }
            if (blocked) continue;

            result.Add(a);
            result.Add(b);
            result.Add(c);
            remaining.RemoveAt(i);
            return true;
        }
        return false;
    }

    /// <summary>Um vértice alinhado com os vizinhos não forma orelha e pode travar o corte: sai sem triângulo.</summary>
    private static bool DropFlatVertex(IReadOnlyList<Point3D> v, List<int> remaining)
    {
        int count = remaining.Count;
        for (int i = 0; i < count; i++)
        {
            var a = v[remaining[(i - 1 + count) % count]];
            var b = v[remaining[i]];
            var c = v[remaining[(i + 1) % count]];
            if (Math.Abs(Cross(a, b, c)) <= Epsilon)
            {
                remaining.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    private static double SignedArea(IReadOnlyList<Point3D> v)
    {
        double sum = 0;
        for (int i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }

    private static double Cross(Point3D a, Point3D b, Point3D c) =>
        (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);

    private static bool SamePoint(Point3D p, Point3D q) =>
        Math.Abs(p.X - q.X) <= 1e-9 && Math.Abs(p.Y - q.Y) <= 1e-9;

    private static bool InsideOrOnTriangle(Point3D p, Point3D a, Point3D b, Point3D c) =>
        Cross(a, b, p) >= -Epsilon && Cross(b, c, p) >= -Epsilon && Cross(c, a, p) >= -Epsilon;
}
