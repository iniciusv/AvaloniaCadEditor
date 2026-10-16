using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.Features;

public class PlaneSectionTests
{
    /// <summary>
    /// Prisma reto sobre um polígono do plano XY, entre <paramref name="z0"/> e <paramref name="z1"/>, montado
    /// como o tesselador do OCCT entrega: vértices não soldados por face, tampas em leque e cada parede
    /// partida por uma diagonal. Arestas vivas: os contornos das tampas e, se <paramref name="sharpWalls"/>,
    /// as quinas verticais (num cilindro elas são lisas e não entram).
    /// </summary>
    internal static TriangleMesh Prism(IReadOnlyList<(double X, double Y)> polygon, double z0, double z1, bool sharpWalls = true)
    {
        var positions = new List<float>();
        var indices = new List<uint>();
        var featureEdges = new List<uint>();
        int n = polygon.Count;

        uint Add(double x, double y, double z)
        {
            positions.Add((float)x); positions.Add((float)y); positions.Add((float)z);
            return (uint)(positions.Count / 3 - 1);
        }

        foreach (var z in new[] { z0, z1 })
        {
            uint first = (uint)(positions.Count / 3);
            foreach (var (x, y) in polygon) Add(x, y, z);
            for (int i = 1; i + 1 < n; i++) { indices.Add(first); indices.Add(first + (uint)i); indices.Add(first + (uint)i + 1); }
            for (int i = 0; i < n; i++) { featureEdges.Add(first + (uint)i); featureEdges.Add(first + (uint)((i + 1) % n)); }
        }

        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            uint v0 = Add(a.X, a.Y, z0), v1 = Add(b.X, b.Y, z0), v2 = Add(b.X, b.Y, z1), v3 = Add(a.X, a.Y, z1);
            indices.AddRange(new[] { v0, v1, v2, v0, v2, v3 });
            if (sharpWalls) { featureEdges.Add(v0); featureEdges.Add(v3); }
        }

        var arr = positions.ToArray();
        return new TriangleMesh(arr, new float[arr.Length], indices.ToArray(), featureEdges.ToArray());
    }

    internal static TriangleMesh Box(double x0, double y0, double x1, double y1, double z0, double z1) =>
        Prism(new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }, z0, z1);

    internal static TriangleMesh Cylinder(double cx, double cy, double r, double z0, double z1, int sides = 32) =>
        Prism(Enumerable.Range(0, sides)
            .Select(i => (cx + r * Math.Cos(2 * Math.PI * i / sides), cy + r * Math.Sin(2 * Math.PI * i / sides)))
            .ToList(), z0, z1, sharpWalls: false);

    [Fact]
    public void FaceNoPlano_ViraUmLacoCoplanarDeQuatroCantos_ESemCorte()
    {
        var refs = PlaneSection.Compute(Box(0, 0, 10, 10, -5, 0), WorkPlane.XY);

        var coplanar = refs.Should().ContainSingle(r => r.Kind == PlaneReferenceKind.Coplanar).Subject;
        coplanar.Closed.Should().BeTrue();
        coplanar.Points.Should().HaveCount(4, "as diagonais do leque da tampa não são arestas vivas");
        refs.Should().NotContain(r => r.Kind == PlaneReferenceKind.Section);
    }

    [Fact]
    public void SolidoAtravessando_DaUmCorteRetangular_SemVerticeDaDiagonal()
    {
        var refs = PlaneSection.Compute(Box(0, 0, 10, 4, -5, 5), WorkPlane.XY);

        var section = refs.Should().ContainSingle().Subject;
        section.Kind.Should().Be(PlaneReferenceKind.Section);
        section.Closed.Should().BeTrue();
        section.Points.Should().HaveCount(4, "o cruzamento da diagonal de cada parede cai no meio da aresta e sai como colinear");
        section.Points.Should().Contain(p => Math.Abs(p.X - 10) < 1e-6 && Math.Abs(p.Y - 4) < 1e-6);
        section.IsCircle.Should().BeFalse();
    }

    [Fact]
    public void SolidoSoEncostando_NaoRepeteOContornoComoCorte()
    {
        var acima = PlaneSection.Compute(Box(0, 0, 10, 10, 0, 5), WorkPlane.XY);

        acima.Should().ContainSingle(r => r.Kind == PlaneReferenceKind.Coplanar);
        acima.Should().NotContain(r => r.Kind == PlaneReferenceKind.Section);
    }

    [Fact]
    public void CilindroAtravessando_DaCorteCircularComCentroERaio()
    {
        var refs = PlaneSection.Compute(Cylinder(2, 1, 3, -1, 1), WorkPlane.XY);

        var section = refs.Should().ContainSingle().Subject;
        section.IsCircle.Should().BeTrue();
        section.Center!.Value.X.Should().BeApproximately(2, 1e-4);
        section.Center!.Value.Y.Should().BeApproximately(1, 1e-4);
        section.Radius!.Value.Should().BeApproximately(3, 1e-2);
    }

    [Fact]
    public void BocaDeCilindroNoPlano_EhCirculoCoplanar()
    {
        var refs = PlaneSection.Compute(Cylinder(0, 0, 2, -3, 0), WorkPlane.XY);

        var mouth = refs.Should().ContainSingle().Subject;
        mouth.Kind.Should().Be(PlaneReferenceKind.Coplanar);
        mouth.IsCircle.Should().BeTrue();
        mouth.Radius!.Value.Should().BeApproximately(2, 1e-2);
    }

    [Fact]
    public void DoisSolidosComFacesNoPlano_DaoDoisLacosCoplanares()
    {
        var mesh = TriangleMesh.Combine(new[] { Box(0, 0, 10, 10, -5, 0), Box(20, 0, 30, 10, -2, 0) }, includeFeatureNormals: false);

        var refs = PlaneSection.Compute(mesh, WorkPlane.XY);

        refs.Where(r => r.Kind == PlaneReferenceKind.Coplanar).Should().HaveCount(2);
    }

    [Fact]
    public void PlanoInclinadoNaOrigem_CortaEmCoordenadasLocaisDoPlano()
    {
        // Plano YZ deslocado para x = 5: X local = Y global, Y local = Z global.
        var plane = new WorkPlane(new Point3D(5, 0, 0), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0));

        var refs = PlaneSection.Compute(Box(0, 0, 10, 4, -2, 3), plane);

        var section = refs.Should().ContainSingle().Subject;
        section.Points.Should().HaveCount(4);
        section.Points.Select(p => p.X).Should().OnlyContain(x => Math.Abs(x) < 1e-6 || Math.Abs(x - 4) < 1e-6);
        section.Points.Select(p => p.Y).Should().OnlyContain(y => Math.Abs(y + 2) < 1e-6 || Math.Abs(y - 3) < 1e-6);
    }
}
