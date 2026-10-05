using System;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class FilletConcaveEdgeTests
{
    /// <summary>
    /// O fillet genérico (base não-Box, ver OcctFilletBuilder.Build) precisa pular
    /// arestas côncavas: o cutter de chanfro/arredondamento só remove material, então numa aresta
    /// côncava — como a quina interna de um degrau — ele abriria um entalhe em vez de cortar a
    /// quina. Esse teste reproduz o caso do BlocoConexaoMotor2020: caixa com um rebaixo no topo,
    /// chanfrada com ApplyToAllEdges = true.
    /// </summary>
    [Fact]
    public void BuildFillet_NaoEntalhaAArestaConcavaDeUmDegrau()
    {
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 40, length: 20, height: 30);
        var notch = new BoxFeature(new Point3D(0, 0, 20), width: 40, length: 10, height: 11);
        var comDegrau = BooleanFeature.CreateDifference("caixa_com_degrau", box, notch);

        var chanfrado = new FilletFeature("chanfrado", comDegrau, 1.0)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = true
        };

        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(chanfrado);

        mesh.Should().NotBeNull();
        mesh.VertexCount.Should().BeGreaterThan(0);

        // A aresta côncava do degrau fica em Y=10, Z=20 (ver EdgeDetectorTests). As normais das
        // faces que se encontram ali são +Z (piso do rebaixo) e -Y (parede), então o perfil do
        // cutter (offsets -r em cada normal) cairia dentro do material e deixaria uma face
        // diagonal de (Y=10, Z=19) a (Y=11, Z=20) ao longo de todo o X. Sem o entalhe, junto a essa
        // aresta só existem o piso e a parede, com normais alinhadas a Z e a Y. Faces dos chanfros
        // legítimos nas pontas (X=0/40) têm componente X na normal e ficam de fora do filtro.
        var facesDoEntalhe = Enumerable.Range(0, mesh.Indices.Length / 3)
            .Where(t =>
            {
                var v = Enumerable.Range(0, 3)
                    .Select(k => mesh.Indices[t * 3 + k] * 3)
                    .Select(o => (X: (double)mesh.Positions[o], Y: (double)mesh.Positions[o + 1], Z: (double)mesh.Positions[o + 2]))
                    .ToArray();

                bool colada = v.All(p => p.Y is > 9.8 and < 11.2 && p.Z is > 18.8 and < 20.2);
                if (!colada) return false;

                double ux = v[1].X - v[0].X, uy = v[1].Y - v[0].Y, uz = v[1].Z - v[0].Z;
                double wx = v[2].X - v[0].X, wy = v[2].Y - v[0].Y, wz = v[2].Z - v[0].Z;
                double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-9) return false;
                nx /= len; ny /= len; nz /= len;

                return Math.Abs(nx) < 0.1 && Math.Abs(ny) > 0.3 && Math.Abs(nz) > 0.3;
            })
            .ToList();

        facesDoEntalhe.Should().BeEmpty(
            "a aresta côncava do degrau não deveria ser chanfrada (isso cortaria material que deve continuar sólido)");
    }
}
