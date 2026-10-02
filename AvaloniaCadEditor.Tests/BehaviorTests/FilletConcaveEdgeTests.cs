using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class FilletConcaveEdgeTests
{
    /// <summary>
    /// O fillet genérico (base não-Box, ver OpenCascadeGeometryKernel.BuildFillet) precisa pular
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
        // cutter (offsets -r em cada normal) cairia dentro do material: Y em [10, 11] e Z em
        // [19, 20], deixando vértices como (10, 19) e (11, 20). Longe das pontas em X (onde as
        // arestas transversais, essas sim convexas, são chanfradas de verdade), esse miolo de
        // material não pode ganhar vértices — sem entalhe, ali só existe sólido.
        var verticesNoEntalheProibido = Enumerable.Range(0, mesh.VertexCount)
            .Where(i =>
            {
                double x = mesh.Positions[i * 3];
                double y = mesh.Positions[i * 3 + 1];
                double z = mesh.Positions[i * 3 + 2];
                return x is > 15 and < 25
                    && y is > 10.01 and < 10.99
                    && z is > 19.01 and < 19.99;
            })
            .ToList();

        verticesNoEntalheProibido.Should().BeEmpty(
            "a aresta côncava do degrau não deveria ser chanfrada (isso cortaria material que deve continuar sólido)");
    }
}
