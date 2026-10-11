using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Chanfro por face no caminho genérico (por malha) do <c>OcctFilletBuilder</c> sobre a geometria do
/// BlocoFicacaoBeltVaoCorreia. Ela tem os dois casos que faziam o sólido inteiro SUMIR:
/// <list type="bullet">
/// <item>emenda entre faces coplanares do topo (a união do corpo com o extra na mesma altura): aresta
/// com n1 = n2, cortador degenerado e "edge creation failed" derrubando a construção;</item>
/// <item>chanfro (2 mm) maior que o raio da boca do furo de 3,2 mm no fundo do rebaixo: os cortadores
/// do anel se cruzam no eixo e o OCCT devolve uma forma não nula, porém vazia, que contaminava todos
/// os cortes seguintes.</item>
/// </list>
/// </summary>
public class FilletDegenerateCutTests
{
    private static IFeature BlocoComFurosEExtra()
    {
        var corpo = new BoxFeature(new Point3D(-20.615334, 10.5, 4), 20.5, 14.5, 13) { Name = "corpo" };
        var vao_correia = new BoxFeature(new Point3D(-22.61533, 15.75, 9), 17, 6, 8) { Name = "vao_correia", IsTransientTool = true };
        var furo_vertical = new CylinderFeature(new Point3D(-5.61533, 18.75, 9), Vector3D.UnitZ, 3, 8) { Name = "furo_vertical", IsTransientTool = true };
        var furo_vertical_3_2 = new CylinderFeature(new Point3D(-5.61533, 18.75, 3), Vector3D.UnitZ, 1.6, 15) { Name = "furo_vertical_3_2", IsTransientTool = true };

        var prism_f62c = new PrismFeature(new[]
        {
            new Point3D(26, 3.25, 0), new Point3D(26, 6.25, 0), new Point3D(30.3, 6.25, 0),
            new Point3D(30.3, 7.55, 0), new Point3D(41, 7.55, 0), new Point3D(41, 3.25, 0)
        }, height: 8, plane: new WorkPlane(new Point3D(20.38467, 25, 9), new Vector3D(0, 0, 1), new Vector3D(-1, 0, 0))) { Name = "prism_f62c" };
        var cylinder_821a = new CylinderFeature(new Point3D(-9.91533, 18.75, 9), Vector3D.UnitZ, 1.3, 8) { Name = "cylinder_821a" };
        var cylinder_54d2 = new CylinderFeature(new Point3D(-5.61533, 18.75, 9), Vector3D.UnitZ, 3, 8) { Name = "cylinder_54d2" };
        var boolOp_7b90 = new BooleanFeature("boolOp_7b90", BooleanOperationType.Union, prism_f62c, new IFeature[] { cylinder_821a });
        var boolOp_16db = new BooleanFeature("boolOp_16db", BooleanOperationType.Subtract, boolOp_7b90, new IFeature[] { cylinder_54d2 });

        var metade = BooleanFeature.CreateDifference("metade", corpo, vao_correia, furo_vertical, furo_vertical_3_2);
        return BooleanFeature.CreateUnion("bloco", metade, boolOp_16db);
    }

    [Fact]
    public void ChanfroNaFaceDeCima_ComEmendaCoplanarEFuroMenorQueORaio_NaoApagaOSolido()
    {
        var bloco = BlocoComFurosEExtra();
        var chanfrado = new FilletFeature("chanfrado", bloco, 2.0)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = false,
            Faces = { new FaceReference(bloco, -1, new Vector3D(0, 0, 1), "CustomFace") }
        };

        var kernel = new OpenCascadeGeometryKernel();
        var semChanfro = kernel.BuildFeatureMesh(bloco);
        var mesh = kernel.BuildFeatureMesh(chanfrado);

        mesh.TriangleCount.Should().BeGreaterThan(0, "uma aresta problemática fica sem chanfro, mas o sólido não pode sumir");
        mesh.TriangleCount.Should().BeGreaterThan(semChanfro.TriangleCount, "as demais arestas do topo continuam chanfradas");

        var zs = Enumerable.Range(0, mesh.VertexCount).Select(i => (double)mesh.Positions[i * 3 + 2]).ToList();
        zs.Min().Should().BeApproximately(4, 0.01, "o chanfro só mexe nas quinas do topo; o corpo continua inteiro");
        zs.Max().Should().BeApproximately(17, 0.01);
    }

    /// <summary>
    /// Com um ponto da face, a face é o PLANO z = 17: o topo é chanfrado, mas o fundo do rebaixo e do vão
    /// (z = 9, mesma normal +Z) ficam intactos — antes, só a normal casava as duas alturas.
    /// </summary>
    [Fact]
    public void ChanfroNaFaceDeCima_ComPontoDaFace_NaoTocaFacesParalelasDeOutraAltura()
    {
        var bloco = BlocoComFurosEExtra();
        var chanfrado = new FilletFeature("chanfrado", bloco, 1.0)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = false,
            Faces = { new FaceReference(bloco, -1, new Vector3D(0, 0, 1), new Point3D(-10.365334, 17.75, 17), "CustomFace") }
        };

        var kernel = new OpenCascadeGeometryKernel();
        var semChanfro = kernel.BuildFeatureMesh(bloco);
        var mesh = kernel.BuildFeatureMesh(chanfrado);

        mesh.TriangleCount.Should().BeGreaterThan(semChanfro.TriangleCount, "o topo continua chanfrado");
        InclinedTriangleCount(mesh, zMin: 8, zMax: 10).Should().Be(0,
            "nada perto de z = 9 (fundo do rebaixo e do vão) pode ganhar face de chanfro");
        InclinedTriangleCount(mesh, zMin: 16, zMax: 17.01).Should().BeGreaterThan(0, "o chanfro do topo é uma face inclinada");
    }

    /// <summary>Controle: caixa com degrau. Topo (z = 30) e piso do degrau (z = 20) têm a mesma normal.</summary>
    [Fact]
    public void Degrau_ComPontoNoTopo_SoOTopoEChanfrado_SemPontoOsDois()
    {
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 40, length: 20, height: 30);
        var notch = new BoxFeature(new Point3D(-1, -1, 20), width: 42, length: 11, height: 11);
        var comDegrau = BooleanFeature.CreateDifference("caixa_com_degrau", box, notch);

        FilletFeature Chanfro(FaceReference face) => new("chanfrado", comDegrau, 1.0)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = false,
            Faces = { face }
        };

        var kernel = new OpenCascadeGeometryKernel();
        var soTopo = kernel.BuildFeatureMesh(Chanfro(new FaceReference(comDegrau, -1, new Vector3D(0, 0, 1), new Point3D(20, 15, 30), "CustomFace")));
        var porNormal = new OpenCascadeGeometryKernel().BuildFeatureMesh(Chanfro(new FaceReference(comDegrau, -1, new Vector3D(0, 0, 1), "CustomFace")));

        InclinedTriangleCount(soTopo, zMin: 28.5, zMax: 30.01).Should().BeGreaterThan(0, "o topo é a face escolhida");
        InclinedTriangleCount(soTopo, zMin: 18.5, zMax: 20.01).Should().Be(0, "o piso do degrau é outra face, só paralela");
        InclinedTriangleCount(porNormal, zMin: 18.5, zMax: 20.01).Should().BeGreaterThan(0,
            "sem o ponto vale o comportamento antigo: toda face com a mesma normal");
    }

    /// <summary>Triângulos com normal oblíqua (nem alinhada a um eixo) inteiramente entre zMin e zMax: faces de chanfro.</summary>
    private static int InclinedTriangleCount(AvaloniaCadEditor.Domain.Mesh.TriangleMesh mesh, double zMin, double zMax) =>
        Enumerable.Range(0, mesh.Indices.Length / 3).Count(t =>
        {
            var v = Enumerable.Range(0, 3)
                .Select(k => mesh.Indices[t * 3 + k] * 3)
                .Select(o => (X: (double)mesh.Positions[o], Y: (double)mesh.Positions[o + 1], Z: (double)mesh.Positions[o + 2]))
                .ToArray();
            if (!v.All(p => p.Z >= zMin && p.Z <= zMax)) return false;

            double ux = v[1].X - v[0].X, uy = v[1].Y - v[0].Y, uz = v[1].Z - v[0].Z;
            double wx = v[2].X - v[0].X, wy = v[2].Y - v[0].Y, wz = v[2].Z - v[0].Z;
            double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
            double len = System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-9) return false;
            // Chanfro de 45°: |nz| ≈ 0,707. Faces horizontais (|nz| ≈ 1) e paredes verticais (nz ≈ 0) ficam de fora.
            double az = System.Math.Abs(nz / len);
            return az > 0.3 && az < 0.95;
        });
}
