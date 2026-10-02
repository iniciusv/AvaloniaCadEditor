using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.Features;

public class EdgeDetectorTests
{
    /// <summary>
    /// Caixa 40(X) x 20(Y) x 30(Z) com um degrau (rebaixo) no topo, igual ao usado no
    /// BlocoConexaoMotor2020: para Y em [0, 10] o topo é rebaixado de Z = 30 para Z = 20.
    /// No corte transversal (Y, Z), a quina onde o chão do rebaixo encontra a parede vertical
    /// — (Y=10, Z=20) — é a única reentrância côncava; as demais quinas (inclusive a outra
    /// borda do próprio degrau, em Y=0, Z=20) são convexas.
    /// </summary>
    private static TriangleMesh BuildCaixaComDegrau()
    {
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 40, length: 20, height: 30);
        var notch = new BoxFeature(new Point3D(0, 0, 20), width: 40, length: 10, height: 11);
        var cut = BooleanFeature.CreateDifference("caixa_com_degrau", box, notch);

        var kernel = new OpenCascadeGeometryKernel();
        return kernel.BuildFeatureMesh(cut);
    }

    private static bool? ConvexidadeDaAresta(TriangleMesh mesh, double y, double z, double eps = 1e-3)
    {
        var (featureEdges, _, _, _, featureConvex) = EdgeDetector.ClassifyEdges(mesh.Positions, mesh.Indices);

        bool? result = null;
        for (int i = 0; i < featureEdges.Length; i += 2)
        {
            uint i1 = featureEdges[i];
            uint i2 = featureEdges[i + 1];

            bool p1NaLinha = Math.Abs(mesh.Positions[i1 * 3 + 1] - y) < eps && Math.Abs(mesh.Positions[i1 * 3 + 2] - z) < eps;
            bool p2NaLinha = Math.Abs(mesh.Positions[i2 * 3 + 1] - y) < eps && Math.Abs(mesh.Positions[i2 * 3 + 2] - z) < eps;

            if (p1NaLinha && p2NaLinha)
            {
                bool convexa = featureConvex[i / 2];
                if (result != null)
                {
                    // Todas as sub-arestas encontradas na mesma linha devem ser consistentes.
                    convexa.Should().Be(result.Value, $"aresta em Y={y}, Z={z} deveria ser consistente entre si");
                }
                result = convexa;
            }
        }

        return result;
    }

    [Fact]
    public void ClassifyEdges_QuinaInternaDoDegrauDeveSerConcava()
    {
        var mesh = BuildCaixaComDegrau();

        var concava = ConvexidadeDaAresta(mesh, y: 10, z: 20);

        concava.Should().NotBeNull("a aresta Y=10,Z=20 deveria existir como feature edge");
        concava!.Value.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 20)]   // outra borda do degrau (o "lábio" externo do rebaixo)
    [InlineData(10, 30)]  // onde o topo plano termina e a parede do degrau começa
    [InlineData(0, 0)]    // quina comum da caixa (controle)
    public void ClassifyEdges_DemaisQuinasDoDegrauDevemSerConvexas(double y, double z)
    {
        var mesh = BuildCaixaComDegrau();

        var convexa = ConvexidadeDaAresta(mesh, y, z);

        convexa.Should().NotBeNull($"a aresta Y={y},Z={z} deveria existir como feature edge");
        convexa!.Value.Should().BeTrue();
    }
}
