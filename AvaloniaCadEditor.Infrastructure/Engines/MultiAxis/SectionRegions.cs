using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using Clipper2Lib;

namespace AvaloniaCadEditor.Infrastructure.Engines.MultiAxis;

/// <summary>
/// Operações de região 2D (Clipper2) sobre as seções de uma <see cref="TriangleMesh"/>: a silhueta
/// da malha num eixo, a seção do prisma-envelope num plano de corte qualquer e a diferença entre
/// essa seção e a fatia real — o material que algum corte removeu, seja um furo fechado ou um
/// recorte que abre para a borda.
/// </summary>
public static class SectionRegions
{
    /// <summary>Casas decimais com que o Clipper arredonda as coordenadas (o mesmo 1e-4 da solda das malhas).</summary>
    private const int DecimalPrecision = 4;

    /// <summary>
    /// Contorno externo da sombra da malha projetada no plano (U, V) do eixo: a união de todos os
    /// triângulos projetados. Devolve <c>null</c> se a sombra tiver mais de uma ilha (a peça não é
    /// um prisma só nesse eixo).
    /// </summary>
    public static List<(double U, double V)>? SilhouetteOuter(TriangleMesh mesh, AxisFrame axis)
    {
        var subject = new PathsD();
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var path = new PathD(3);
            for (int k = 0; k < 3; k++)
            {
                int b = (int)mesh.Indices[i + k] * 3;
                var (u, v, _) = axis.ToLocal(new Point3D(mesh.Positions[b], mesh.Positions[b + 1], mesh.Positions[b + 2]));
                path.Add(new PointD(u, v));
            }
            double area = Clipper.Area(path);
            if (Math.Abs(area) < 1e-10) continue; // triângulo de perfil: não faz sombra
            if (area < 0) path.Reverse();
            subject.Add(path);
        }
        if (subject.Count == 0) return null;

        var outers = Clipper.Union(subject, new PathsD(), FillRule.NonZero, DecimalPrecision)
            .Where(p => Clipper.Area(p) > 0)
            .ToList();
        // A união guarda os vértices colineares (as pontas de cada triângulo ao longo de uma aresta reta):
        // sem tirá-los, o perfil da base sai com dezenas de pontos sobrando no código gerado.
        return outers.Count == 1 ? ToLoop(Clipper.TrimCollinear(outers[0], DecimalPrecision)) : null;
    }

    /// <summary>
    /// Regiões (contornos externos, no plano (U, V) de <paramref name="cutAxis"/>) que estão dentro
    /// do envelope mas fora do material na fatia W = <paramref name="level"/>. Fitas mais finas que
    /// 2 × <paramref name="tolerance"/> — ruído de arredondamento entre a borda do envelope e a da
    /// malha — são removidas por uma abertura morfológica.
    /// </summary>
    public static List<List<(double U, double V)>> RemovedRegions(
        TriangleMesh mesh, AxisFrame cutAxis, PrismEnvelope envelope, double level, double tolerance)
    {
        var envelopeSection = envelope.SectionAt(cutAxis, level);
        if (envelopeSection.Count == 0) return new List<List<(double U, double V)>>();

        var material = MaterialAt(mesh, cutAxis, level);
        var removed = Clipper.Difference(envelopeSection, material, FillRule.EvenOdd, DecimalPrecision);
        if (removed.Count == 0) return new List<List<(double U, double V)>>();

        double delta = Math.Max(tolerance, 0.01);
        var eroded = Clipper.InflatePaths(removed, -delta, JoinType.Miter, EndType.Polygon, 10.0, DecimalPrecision);
        var opened = Clipper.InflatePaths(eroded, delta, JoinType.Miter, EndType.Polygon, 10.0, DecimalPrecision);

        return opened
            .Where(p => Clipper.Area(p) > 0)
            .Select(p => ToLoop(Clipper.TrimCollinear(p, DecimalPrecision)))
            .Where(loop => loop.Count >= 3)
            .ToList();
    }

    /// <summary>
    /// Área de <paramref name="region"/> que cai sobre material na fatia W = <paramref name="level"/>
    /// do eixo. Zero (ou quase) quer dizer que um corte com essa seção atravessaria só ar ali.
    /// </summary>
    public static double MaterialAreaInside(TriangleMesh mesh, AxisFrame axis, List<(double U, double V)> region, double level)
    {
        var material = MaterialAt(mesh, axis, level);
        if (material.Count == 0) return 0;
        var clip = new PathsD { ToPath(region) };
        return Math.Abs(Clipper.Area(Clipper.Intersect(material, clip, FillRule.EvenOdd, DecimalPrecision)));
    }

    /// <summary>Área com sinal (positiva no sentido anti-horário).</summary>
    public static double Area(List<(double U, double V)> loop) => Clipper.Area(ToPath(loop));

    /// <summary>Centróide de área do polígono (não a média dos vértices, que um vértice a mais desloca).</summary>
    public static (double U, double V) Centroid(List<(double U, double V)> loop)
    {
        double a = 0, cu = 0, cv = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var p = loop[i];
            var q = loop[(i + 1) % loop.Count];
            double cross = p.U * q.V - q.U * p.V;
            a += cross;
            cu += (p.U + q.U) * cross;
            cv += (p.V + q.V) * cross;
        }
        if (Math.Abs(a) < 1e-12)
        {
            return (loop.Average(p => p.U), loop.Average(p => p.V));
        }
        return (cu / (3 * a), cv / (3 * a));
    }

    private static PathsD MaterialAt(TriangleMesh mesh, AxisFrame axis, double level)
    {
        var paths = new PathsD();
        foreach (var loop in MeshSlicer.Slice(mesh, axis, level))
        {
            paths.Add(ToPath(loop));
        }
        return paths;
    }

    internal static PathD ToPath(List<(double U, double V)> loop)
    {
        var path = new PathD(loop.Count);
        foreach (var p in loop) path.Add(new PointD(p.U, p.V));
        return path;
    }

    private static List<(double U, double V)> ToLoop(PathD path) => path.Select(p => (p.x, p.y)).ToList();
}

/// <summary>
/// O sólido-base hipotético: um perfil (no plano (U, V) de <see cref="Axis"/>) extrudado de
/// <see cref="Min"/> a <see cref="Max"/> ao longo do eixo. Sabe dar a própria seção em qualquer
/// plano perpendicular a um dos três eixos.
/// </summary>
public sealed class PrismEnvelope
{
    public AxisFrame Axis { get; }
    public List<(double U, double V)> Profile { get; }
    public double Min { get; }
    public double Max { get; }

    public PrismEnvelope(AxisFrame axis, List<(double U, double V)> profile, double min, double max)
    {
        Axis = axis;
        Profile = profile;
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Seção do prisma no plano W = <paramref name="level"/> de <paramref name="cutAxis"/>, em
    /// coordenadas (U, V) desse eixo. No próprio eixo do prisma é o perfil; num eixo perpendicular
    /// é um retângulo por trecho em que a reta de corte atravessa o perfil.
    /// </summary>
    public PathsD SectionAt(AxisFrame cutAxis, double level)
    {
        if (ReferenceEquals(cutAxis, Axis))
        {
            return level < Min || level > Max ? new PathsD() : new PathsD { SectionRegions.ToPath(Profile) };
        }

        int a = IndexOf(Axis), b = IndexOf(cutAxis), c = 3 - a - b;

        // O perfil em coordenadas de mundo: cada vértice tem uma componente no eixo de corte (b) e
        // outra no terceiro eixo (c). A reta b = level corta as arestas em pontos de c, que se
        // emparelham em trechos dentro do perfil (regra par-ímpar).
        var world = Profile.Select(p => Axis.ToWorld(p.U, p.V, 0)).ToList();
        var crossings = new List<double>();
        for (int i = 0, j = world.Count - 1; i < world.Count; j = i++)
        {
            double bi = Component(world[i], b), bj = Component(world[j], b);
            if ((bi > level) == (bj > level)) continue;
            double t = (level - bi) / (bj - bi);
            double ci = Component(world[i], c), cj = Component(world[j], c);
            crossings.Add(ci + t * (cj - ci));
        }
        crossings.Sort();

        var section = new PathsD();
        for (int k = 0; k + 1 < crossings.Count; k += 2)
        {
            var path = new PathD(4)
            {
                Local(cutAxis, a, Min, b, level, c, crossings[k]),
                Local(cutAxis, a, Max, b, level, c, crossings[k]),
                Local(cutAxis, a, Max, b, level, c, crossings[k + 1]),
                Local(cutAxis, a, Min, b, level, c, crossings[k + 1]),
            };
            if (Clipper.Area(path) < 0) path.Reverse();
            section.Add(path);
        }
        return section;
    }

    private static PointD Local(AxisFrame frame, int i1, double v1, int i2, double v2, int i3, double v3)
    {
        var comps = new double[3];
        comps[i1] = v1; comps[i2] = v2; comps[i3] = v3;
        var (u, v, _) = frame.ToLocal(new Point3D(comps[0], comps[1], comps[2]));
        return new PointD(u, v);
    }

    private static int IndexOf(AxisFrame frame) => frame.Axis.X != 0 ? 0 : frame.Axis.Y != 0 ? 1 : 2;

    private static double Component(Point3D p, int index) => index switch { 0 => p.X, 1 => p.Y, _ => p.Z };
}
