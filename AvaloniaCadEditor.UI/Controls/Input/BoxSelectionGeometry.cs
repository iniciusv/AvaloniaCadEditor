using System;
using System.Collections.Generic;
using Avalonia;

namespace AvaloniaCadEditor.UI.Controls.Input;

/// <summary>
/// Testes 2D (em pixels de tela) da seleção por retângulo, no estilo AutoCAD/SketchUp:
/// <list type="bullet">
/// <item><b>Janela</b> (arrastando da esquerda para a direita): o elemento só entra se estiver
/// inteiramente dentro do retângulo.</item>
/// <item><b>Crossing</b> (da direita para a esquerda): entra qualquer elemento que toque o retângulo.</item>
/// </list>
/// Pontos projetados como <c>null</c> estão atrás da câmera: nunca contam como "dentro" (então o
/// elemento não passa na janela) e os segmentos/triângulos que os usam são ignorados no crossing.
/// </summary>
public static class BoxSelectionGeometry
{
    /// <summary>Janela quando o arraste vai para a direita; crossing quando vai para a esquerda.</summary>
    public static bool IsWindowSelection(Point start, Point end) => end.X >= start.X;

    public static Rect Normalize(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    public static bool Contains(Rect r, Point p) =>
        p.X >= r.X && p.X <= r.Right && p.Y >= r.Y && p.Y <= r.Bottom;

    /// <summary>Polilinha (linha de esboço, círculo amostrado) contra o retângulo.</summary>
    public static bool PolylineMatches(Rect r, IReadOnlyList<Point?> points, bool closed, bool window)
    {
        if (points.Count == 0) return false;

        if (window)
        {
            foreach (var p in points)
                if (p is not { } q || !Contains(r, q)) return false;
            return true;
        }

        int segmentCount = closed ? points.Count : points.Count - 1;
        if (points.Count == 1)
            return points[0] is { } only && Contains(r, only);

        for (int i = 0; i < segmentCount; i++)
        {
            if (points[i] is { } a && points[(i + 1) % points.Count] is { } b && SegmentIntersectsRect(r, a, b))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Malha (vértices já projetados, índices de triângulos) contra o retângulo. Na janela todos os
    /// vértices usados pelos triângulos precisam estar dentro; no crossing basta um triângulo tocar.
    /// </summary>
    public static bool MeshMatches(Rect r, IReadOnlyList<Point?> vertices, IReadOnlyList<uint> indices, bool window)
    {
        if (indices.Count < 3) return false;

        if (window)
        {
            foreach (var index in indices)
                if (vertices[(int)index] is not { } q || !Contains(r, q)) return false;
            return true;
        }

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            if (vertices[(int)indices[i]] is { } a &&
                vertices[(int)indices[i + 1]] is { } b &&
                vertices[(int)indices[i + 2]] is { } c &&
                TriangleIntersectsRect(r, a, b, c))
                return true;
        }
        return false;
    }

    public static bool SegmentIntersectsRect(Rect r, Point a, Point b)
    {
        if (Contains(r, a) || Contains(r, b)) return true;

        // Rejeição rápida pela caixa envolvente do segmento.
        if (Math.Max(a.X, b.X) < r.X || Math.Min(a.X, b.X) > r.Right ||
            Math.Max(a.Y, b.Y) < r.Y || Math.Min(a.Y, b.Y) > r.Bottom)
            return false;

        return SegmentsIntersect(a, b, r.TopLeft, r.TopRight) ||
               SegmentsIntersect(a, b, r.TopRight, r.BottomRight) ||
               SegmentsIntersect(a, b, r.BottomRight, r.BottomLeft) ||
               SegmentsIntersect(a, b, r.BottomLeft, r.TopLeft);
    }

    public static bool TriangleIntersectsRect(Rect r, Point a, Point b, Point c)
    {
        if (Math.Max(a.X, Math.Max(b.X, c.X)) < r.X || Math.Min(a.X, Math.Min(b.X, c.X)) > r.Right ||
            Math.Max(a.Y, Math.Max(b.Y, c.Y)) < r.Y || Math.Min(a.Y, Math.Min(b.Y, c.Y)) > r.Bottom)
            return false;

        if (SegmentIntersectsRect(r, a, b) || SegmentIntersectsRect(r, b, c) || SegmentIntersectsRect(r, c, a))
            return true;

        // Sem arestas cruzando: ou o retângulo está todo dentro do triângulo, ou estão separados.
        return PointInTriangle(r.Center, a, b, c);
    }

    private static bool PointInTriangle(Point p, Point a, Point b, Point c)
    {
        double d1 = Cross(a, b, p);
        double d2 = Cross(b, c, p);
        double d3 = Cross(c, a, p);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static bool SegmentsIntersect(Point p1, Point p2, Point p3, Point p4)
    {
        double d1 = Cross(p3, p4, p1);
        double d2 = Cross(p3, p4, p2);
        double d3 = Cross(p1, p2, p3);
        double d4 = Cross(p1, p2, p4);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
            ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        return (d1 == 0 && OnSegment(p3, p4, p1)) ||
               (d2 == 0 && OnSegment(p3, p4, p2)) ||
               (d3 == 0 && OnSegment(p1, p2, p3)) ||
               (d4 == 0 && OnSegment(p1, p2, p4));
    }

    private static double Cross(Point o, Point a, Point b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    private static bool OnSegment(Point a, Point b, Point p) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
        p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
}
