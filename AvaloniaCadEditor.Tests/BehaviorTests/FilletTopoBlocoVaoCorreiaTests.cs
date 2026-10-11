using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Xunit.Abstractions;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Chanfro da face de cima (z = 17) do BlocoFicacaoBeltVaoCorreia com o vão de 6,5 mm (furo r 3,25
/// tangente às duas paredes do vão). O ProfileSegments muda quantos trechos tem cada arco e, com isso,
/// quais cortes o OCCT erra — o app usa 48.
/// </summary>
public class FilletTopoBlocoVaoCorreiaTests(ITestOutputHelper output)
{
    private static IFeature Bloco(double raioRebaixoPrisma)
    {
        var corpo = new BoxFeature(new Point3D(-20.615334, 10.5, 4), 20.5, 14.5, 13) { Name = "corpo" };
        var vao_correia = new BoxFeature(new Point3D(-22.61533, 15.5, 7), 17, 6.5, 10) { Name = "vao_correia", IsTransientTool = true };
        var furo_vertical = new CylinderFeature(new Point3D(-5.61533, 18.75, 7), Vector3D.UnitZ, 3.25, 10) { Name = "furo_vertical", IsTransientTool = true };
        var furo_vertical_3_2 = new CylinderFeature(new Point3D(-5.61533, 18.75, 3), Vector3D.UnitZ, 1.6, 15) { Name = "furo_vertical_3_2", IsTransientTool = true };

        var prism_f62c = new PrismFeature(new[]
        {
            new Point3D(26, 3.0, 0), new Point3D(26, 6.25, 0), new Point3D(30.3, 6.25, 0),
            new Point3D(30.3, 7.55, 0), new Point3D(41, 7.55, 0), new Point3D(41, 3.0, 0)
        }, height: 10, plane: new WorkPlane(new Point3D(20.38467, 25, 7), new Vector3D(0, 0, 1), new Vector3D(-1, 0, 0))) { Name = "prism_f62c" };
        var cylinder_821a = new CylinderFeature(new Point3D(-9.91533, 18.75, 7), Vector3D.UnitZ, 1.3, 10) { Name = "cylinder_821a" };
        var cylinder_54d2 = new CylinderFeature(new Point3D(-5.61533, 18.75, 7), Vector3D.UnitZ, raioRebaixoPrisma, 10) { Name = "cylinder_54d2" };
        var boolOp_7b90 = new BooleanFeature("boolOp_7b90", BooleanOperationType.Union, prism_f62c, new IFeature[] { cylinder_821a });
        var boolOp_16db = new BooleanFeature("boolOp_16db", BooleanOperationType.Subtract, boolOp_7b90, new IFeature[] { cylinder_54d2 });

        var metade = BooleanFeature.CreateDifference("metade", corpo, vao_correia, furo_vertical, furo_vertical_3_2);
        return BooleanFeature.CreateUnion("bloco", metade, boolOp_16db);
    }

    [Theory]
    [InlineData(3.0, 32)]
    [InlineData(3.25, 32)]
    [InlineData(3.0, 48)]
    public void ChanfroNaFaceDeCima_TodasAsQuinasDoTopoSaemChanfradas(double raioRebaixoPrisma, int profileSegments)
    {
        SemChanfroNoTopo(raioRebaixoPrisma, profileSegments).Should().BeEmpty("toda quina convexa da face z = 17 tem de sair chanfrada");
    }

    /// <summary>
    /// Aqui um corte no arco do furo sai CORROMPIDO (forma maior que antes, ou com menos volume do que
    /// o cortador tinha) e, aceito, todo corte seguinte dava forma vazia: faltavam ~35 trechos, inclusive
    /// a borda externa y = 25, longe do furo. Rejeitado, só sobram trechos isolados que o OCCT não
    /// consegue cortar com nenhuma sobra — e o contorno externo sai inteiro.
    /// </summary>
    [Fact]
    public void ChanfroNaFaceDeCima_CorteCorrompidoNoArcoNaoContaminaOsSeguintes()
    {
        var faltando = SemChanfroNoTopo(raioRebaixoPrisma: 3.25, profileSegments: 48);

        faltando.Should().NotContain(m => m.Y < 10.501 || m.Y > 24.999 || m.X > -0.116 || m.X < -20.614,
            "o contorno externo não tem nada de delicado; ficar sem chanfro ali é sinal de corte contaminado");
        faltando.Count.Should().BeLessThanOrEqualTo(2, "só trechos isolados podem falhar, não uma cascata");
    }

    private List<Point3D> SemChanfroNoTopo(double raioRebaixoPrisma, int profileSegments)
    {
        var bloco = Bloco(raioRebaixoPrisma);
        var chanfrado = new FilletFeature("chanfrado", bloco, 1.0)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = false,
            Faces = { new FaceReference(bloco, -1, new Vector3D(0, 0, 1), new Point3D(-0.115334, 25, 17), "CustomFace") }
        };

        var settings = new TessellationSettings { ProfileSegments = profileSegments };
        var semChanfro = new OpenCascadeGeometryKernel().BuildFeatureMesh(bloco, settings);
        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(chanfrado, settings);

        var faltando = QuinasConvexasDoTopo(semChanfro).Where(m => TocaASuperficie(mesh, m)).ToList();
        foreach (var m in faltando)
            output.WriteLine($"sem chanfro: ({m.X:0.###}, {m.Y:0.###}, {m.Z:0.###})");
        return faltando;
    }

    /// <summary>Ponto médio de cada quina convexa da malha cujas duas pontas estão em z = 17 e que tem o topo (+Z) de um lado.</summary>
    private static IEnumerable<Point3D> QuinasConvexasDoTopo(TriangleMesh mesh)
    {
        var (_, _, _, _, convexa) = EdgeDetector.ClassifyEdges(mesh.Positions, mesh.Indices);
        for (int i = 0; i < mesh.FeatureEdges.Length; i += 2)
        {
            if (!convexa[i / 2]) continue;
            var a = Ponto(mesh, mesh.FeatureEdges[i]);
            var b = Ponto(mesh, mesh.FeatureEdges[i + 1]);
            if (System.Math.Abs(a.Z - 17) > 1e-3 || System.Math.Abs(b.Z - 17) > 1e-3) continue;
            double nz1 = mesh.FeatureNormals1[(i / 2) * 3 + 2], nz2 = mesh.FeatureNormals2[(i / 2) * 3 + 2];
            bool topoDeUmLado = (nz1 > 0.99) != (nz2 > 0.99);
            if (!topoDeUmLado) continue;
            yield return new Point3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, 17);
        }
    }

    private static Point3D Ponto(TriangleMesh mesh, uint i) =>
        new(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2]);

    /// <summary>O ponto ainda está sobre algum triângulo da malha (a quina não foi cortada).</summary>
    private static bool TocaASuperficie(TriangleMesh mesh, Point3D p)
    {
        for (int t = 0; t < mesh.Indices.Length / 3; t++)
        {
            var a = Ponto(mesh, mesh.Indices[t * 3]);
            var b = Ponto(mesh, mesh.Indices[t * 3 + 1]);
            var c = Ponto(mesh, mesh.Indices[t * 3 + 2]);
            if (DistanciaAoTriangulo(p, a, b, c) < 1e-3) return true;
        }
        return false;
    }

    private static double DistanciaAoTriangulo(Point3D p, Point3D a, Point3D b, Point3D c)
    {
        // Ericson, Real-Time Collision Detection, ClosestPtPointTriangle.
        System.Numerics.Vector3 V(Point3D q) => new((float)q.X, (float)q.Y, (float)q.Z);
        var P = V(p); var A = V(a); var B = V(b); var C = V(c);
        var ab = B - A; var ac = C - A; var ap = P - A;
        float d1 = System.Numerics.Vector3.Dot(ab, ap), d2 = System.Numerics.Vector3.Dot(ac, ap);
        System.Numerics.Vector3 q;
        if (d1 <= 0 && d2 <= 0) q = A;
        else
        {
            var bp = P - B;
            float d3 = System.Numerics.Vector3.Dot(ab, bp), d4 = System.Numerics.Vector3.Dot(ac, bp);
            var cp = P - C;
            float d5 = System.Numerics.Vector3.Dot(ab, cp), d6 = System.Numerics.Vector3.Dot(ac, cp);
            float vc = d1 * d4 - d3 * d2, vb = d5 * d2 - d1 * d6, va = d3 * d6 - d5 * d4;
            if (d3 >= 0 && d4 <= d3) q = B;
            else if (d6 >= 0 && d5 <= d6) q = C;
            else if (vc <= 0 && d1 >= 0 && d3 <= 0) q = A + ab * (d1 / (d1 - d3));
            else if (vb <= 0 && d2 >= 0 && d6 <= 0) q = A + ac * (d2 / (d2 - d6));
            else if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) q = B + (C - B) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            else
            {
                float denom = 1f / (va + vb + vc);
                q = A + ab * (vb * denom) + ac * (vc * denom);
            }
        }
        return (P - q).Length();
    }
}
