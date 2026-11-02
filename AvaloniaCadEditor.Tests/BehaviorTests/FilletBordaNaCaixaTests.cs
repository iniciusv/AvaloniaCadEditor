using System;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Chanfro por malha numa borda curva que encosta na caixa envolvente: a borda de um disco com um pino
/// unido em cima. A caixa do sólido tesselado é a do polígono (em −X o de 51 lados chega a 24,95 de um
/// raio 25), a do sólido recém-cortado é a exata, e o corte válido parecia uma booleana que fez a forma
/// crescer: era descartado em silêncio e só o topo do pino saía chanfrado.
/// </summary>
public class FilletBordaNaCaixaTests
{
    /// <summary>A tesselação do app (<c>tessellation_settings.json</c>).</summary>
    private static TessellationSettings App() => new() { LinearDeflection = 0.1, AngularDeflection = 0.5, ProfileSegments = 51, FaceSegments = 36, CreaseAngle = 30 };

    private const double Chanfro = 0.5;

    private static BooleanFeature DiscoComPino(double raioPino = 1.5) => new("disco_pino", BooleanOperationType.Union,
        new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, 25, 3) { Name = "disco" },
        new IFeature[] { new CylinderFeature(new Point3D(5, 0, 3), Vector3D.UnitZ, raioPino, 3) { Name = "pino" } });

    /// <summary>Maior distância ao eixo (cx, cy) entre os vértices no plano z, perto do raio <paramref name="raio"/>:
    /// <paramref name="raio"/> com a quina viva, <paramref name="raio"/> − chanfro com ela cortada.</summary>
    private static double RaioNoPlano(TriangleMesh mesh, double cx, double cy, double z, double raio)
    {
        double maior = 0;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            double x = mesh.Positions[i * 3] - cx, y = mesh.Positions[i * 3 + 1] - cy;
            if (Math.Abs(mesh.Positions[i * 3 + 2] - z) > 1e-3) continue;
            double r = Math.Sqrt(x * x + y * y);
            if (r > raio - 2 && r < raio + 0.05) maior = Math.Max(maior, r);
        }
        return maior;
    }

    [Fact]
    public void TodasAsArestas_ChanfraAsBordasDoDiscoEOTopoDoPino()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(new FilletFeature("chanfro", DiscoComPino(), Chanfro) { Mode = FilletMode.Bevel, ApplyToAllEdges = true }, App());

        kernel.BuildProblems.Should().BeEmpty();
        RaioNoPlano(mesh, 0, 0, 3, 25).Should().BeApproximately(25 - Chanfro, 1e-2, "a borda do topo do disco encosta na caixa e tem de ser cortada");
        RaioNoPlano(mesh, 0, 0, 0, 25).Should().BeApproximately(25 - Chanfro, 1e-2, "idem a do fundo");
        RaioNoPlano(mesh, 5, 0, 6, 1.5).Should().BeApproximately(1.5 - Chanfro, 1e-2);
    }

    [Fact]
    public void SoAFaceDeCimaDoDisco_ChanfraABordaExternaSemTocarNaUniaoComOPino()
    {
        // O que a ferramenta gera ao selecionar a face: o plano dela (normal + um ponto).
        var uniao = DiscoComPino();
        var chanfro = new FilletFeature("chanfro", uniao, Chanfro) { Mode = FilletMode.Bevel };
        chanfro.AddFace(new FaceReference(uniao, -1, Vector3D.UnitZ, new Point3D(0, 0, 3), "CustomFace"));

        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(chanfro, App());

        kernel.BuildProblems.Should().BeEmpty();
        RaioNoPlano(mesh, 0, 0, 3, 25).Should().BeApproximately(25 - Chanfro, 1e-2);
        RaioNoPlano(mesh, 0, 0, 0, 25).Should().BeApproximately(25, 1e-2, "o fundo não é a face escolhida");
        RaioNoPlano(mesh, 5, 0, 6, 1.5).Should().BeApproximately(1.5, 1e-2, "o topo do pino fica em outro plano");
        RaioNoPlano(mesh, 5, 0, 3, 1.5).Should().BeApproximately(1.5, 1e-2, "a emenda com o pino é côncava e fica como está");
    }

    [Fact]
    public void FaceTopDoProprioCilindro_ChanfraSoOTopo()
    {
        // O outro caminho da regra da IA: FaceType num primitivo, aqui antes da união.
        var disco = new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, 25, 3) { Name = "disco" };
        var chanfro_disco = new FilletFeature("chanfro_disco", disco, Chanfro) { Mode = FilletMode.Bevel };
        chanfro_disco.AddFace(new FaceReference(disco, (int)FaceType.Top, Vector3D.UnitZ, "topo"));
        var pino = new CylinderFeature(new Point3D(5, 0, 3), Vector3D.UnitZ, 1.5, 3) { Name = "pino" };
        var chanfro_pino = new FilletFeature("chanfro_pino", pino, Chanfro) { Mode = FilletMode.Bevel };
        chanfro_pino.AddFace(new FaceReference(pino, (int)FaceType.Top, Vector3D.UnitZ, "topo"));

        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(new BooleanFeature("uniao", BooleanOperationType.Union, chanfro_disco, new IFeature[] { chanfro_pino }), App());

        kernel.BuildProblems.Should().BeEmpty();
        RaioNoPlano(mesh, 0, 0, 3, 25).Should().BeApproximately(25 - Chanfro, 1e-2);
        RaioNoPlano(mesh, 5, 0, 6, 1.5).Should().BeApproximately(1.5 - Chanfro, 1e-2);
        RaioNoPlano(mesh, 0, 0, 0, 25).Should().BeApproximately(25, 1e-2);
        RaioNoPlano(mesh, 5, 0, 3, 1.5).Should().BeApproximately(1.5, 1e-2, "a base do pino não ganha entalhe");
    }

    [Fact]
    public void ArcoMenorQueOChanfro_FicaSemEleEAvisa()
    {
        // Pino de raio 0,4 com chanfro de 0,5: o chanfro passaria do eixo.
        var kernel = new OpenCascadeGeometryKernel();
        var chanfro = new FilletFeature("chanfro", DiscoComPino(raioPino: 0.4), Chanfro) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };
        var mesh = kernel.BuildFeatureMesh(chanfro, App());

        kernel.BuildProblems.Values.Should().ContainSingle().Which.Should().Contain("chanfro").And.Contain("1 aresta");
        RaioNoPlano(mesh, 0, 0, 3, 25).Should().BeApproximately(25 - Chanfro, 1e-2, "as outras arestas continuam chanfradas");
    }
}
