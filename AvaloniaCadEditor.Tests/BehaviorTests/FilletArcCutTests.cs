using System;
using System.Diagnostics;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Chanfro/arredondamento de todas as arestas DEPOIS dos furos: cada boca de furo é um anel só (antes, um
/// corte por segmento da tesselação). A peça de <c>AiCore/Cena.cs</c> que motivou isto não terminava em
/// 8 minutos; o mesmo chanfro antes dos furos levava 1,5 s.
/// </summary>
public class FilletArcCutTests
{
    /// <summary>A tesselação do app quando isto foi medido (<c>tessellation_settings.json</c>).</summary>
    private static TessellationSettings App() => new() { LinearDeflection = 0.1, AngularDeflection = 0.5, ProfileSegments = 51, FaceSegments = 36, CreaseAngle = 30 };

    private const double R = 3;     // raio dos furos
    private const double Chanfro = 1;

    private static IFeature[] Furos(int quantidade) => Enumerable.Range(0, quantidade)
        .Select(i => (IFeature)new CylinderFeature(new Point3D(10 + 20 * (i % 4), 10 + 20 * (i / 4), -1), Vector3D.UnitZ, R, 12) { Name = $"furo{i}" })
        .ToArray();

    private static BoxFeature Placa() => new(Point3D.Origin, 80, 60, 10) { Name = "placa" };

    private static FilletFeature ChanfroDepoisDosFuros(int furos = 12, bool pularFuros = false, FilletMode modo = FilletMode.Bevel) =>
        new("chanfro", BooleanFeature.CreateDifference("furada", Placa(), Furos(furos)), Chanfro)
        { Mode = modo, ApplyToAllEdges = true, SkipHoleEdges = pularFuros };

    private static IFeature ChanfroAntesDosFuros(int furos = 12) =>
        BooleanFeature.CreateDifference("furada", new FilletFeature("chanfro", Placa(), Chanfro) { Mode = FilletMode.Bevel, ApplyToAllEdges = true }, Furos(furos));

    private static bool TemVerticeNoTopoARaio(TriangleMesh mesh, double cx, double cy, double raio)
    {
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            double x = mesh.Positions[i * 3], y = mesh.Positions[i * 3 + 1], z = mesh.Positions[i * 3 + 2];
            if (Math.Abs(z - 10) < 1e-3 && Math.Abs(Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - raio) < 1e-2) return true;
        }
        return false;
    }

    [Fact]
    public void PlacaCom12Furos_ChanfroDepois_SaiEmSegundosEEscareiaAsBocas()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var relogio = Stopwatch.StartNew();
        var mesh = kernel.BuildFeatureMesh(ChanfroDepoisDosFuros(), App());
        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20), "um anel por boca, não um corte por segmento");
        kernel.BuildProblems.Should().BeEmpty();

        TemVerticeNoTopoARaio(mesh, 10, 10, R + Chanfro).Should().BeTrue("o escareado abre a boca até R + chanfro");

        // Mesmo volume do chanfro feito antes dos furos, menos um escareado (anel triangular) por boca.
        double antes = MeshVolumeCalculator.ComputeVolume(new OpenCascadeGeometryKernel().BuildFeatureMesh(ChanfroAntesDosFuros(), App()));
        double escareado = 2 * Math.PI * (R + Chanfro / 3) * Chanfro * Chanfro / 2;
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(antes - 24 * escareado, 24 * escareado * 0.05);
    }

    [Fact]
    public void SkipHoleEdges_ChanfraSoAsArestasExternas()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(ChanfroDepoisDosFuros(pularFuros: true), App());

        kernel.BuildProblems.Should().BeEmpty();
        TemVerticeNoTopoARaio(mesh, 10, 10, R + Chanfro).Should().BeFalse("a boca do furo fica viva");
        double antes = MeshVolumeCalculator.ComputeVolume(new OpenCascadeGeometryKernel().BuildFeatureMesh(ChanfroAntesDosFuros(), App()));
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(antes, antes * 1e-3);
    }

    [Fact]
    public void Arredondamento_DepoisDosFuros_TiraMenosQueOChanfro()
    {
        var kernel = new OpenCascadeGeometryKernel();
        double chanfrado = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(ChanfroDepoisDosFuros(furos: 4), App()));
        double arredondado = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(ChanfroDepoisDosFuros(furos: 4, modo: FilletMode.Round), App()));
        double semNada = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(BooleanFeature.CreateDifference("furada", Placa(), Furos(4)), App()));

        kernel.BuildProblems.Should().BeEmpty();
        arredondado.Should().BeGreaterThan(chanfrado).And.BeLessThan(semNada);
    }

    [Fact]
    public void Ressalto_ChanfroNoTopoDoCilindroUnido()
    {
        var ressalto = new CylinderFeature(new Point3D(40, 30, 10), Vector3D.UnitZ, 5, 4) { Name = "ressalto" };
        var corpo = BooleanFeature.CreateUnion("corpo", Placa(), ressalto);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(new FilletFeature("chanfro", corpo, Chanfro) { Mode = FilletMode.Bevel, ApplyToAllEdges = true }, App());

        kernel.BuildProblems.Should().BeEmpty();
        bool bordaDoTopo = Enumerable.Range(0, mesh.VertexCount).Any(i =>
        {
            double x = mesh.Positions[i * 3] - 40, y = mesh.Positions[i * 3 + 1] - 30, z = mesh.Positions[i * 3 + 2];
            return Math.Abs(z - 14) < 1e-3 && Math.Abs(Math.Sqrt(x * x + y * y) - (5 - Chanfro)) < 1e-2;
        });
        bordaDoTopo.Should().BeTrue("o topo do ressalto encolhe para R − chanfro");
    }

    [Fact]
    public void ArestasDemaisSemFormaReconhecida_SaiSemChanfroEComAviso()
    {
        // Com 8 segmentos por círculo o furo é um octógono (45° por lado): não passa por arco. 13 furos dão
        // 13 × 2 × 8 = 208 arestas de boca, acima do teto.
        var grosseiro = new TessellationSettings { ProfileSegments = 8 };
        var kernel = new OpenCascadeGeometryKernel();
        var relogio = Stopwatch.StartNew();

        var mesh = kernel.BuildFeatureMesh(ChanfroDepoisDosFuros(furos: 13), grosseiro);

        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
        kernel.BuildProblems.Values.Should().ContainSingle().Which.Should().Contain("não foi aplicado");
        double semChanfro = MeshVolumeCalculator.ComputeVolume(new OpenCascadeGeometryKernel().BuildFeatureMesh(BooleanFeature.CreateDifference("furada", Placa(), Furos(13)), grosseiro));
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(semChanfro, semChanfro * 1e-6);
    }

    [Fact]
    public void AvisoDoTeto_VoltaNaReexecucao_MesmoComAMalhaJaConstruida()
    {
        var grosseiro = new TessellationSettings { ProfileSegments = 8 };
        var kernel = new OpenCascadeGeometryKernel();
        kernel.BuildFeatureMesh(ChanfroDepoisDosFuros(furos: 13), grosseiro);
        kernel.BuildProblems.Should().NotBeEmpty();

        kernel.BeginSceneRebuild();
        kernel.BuildFeatureMesh(ChanfroDepoisDosFuros(furos: 13), grosseiro);

        kernel.BuildProblems.Should().NotBeEmpty("a malha com aviso não pode ir para o cache por geometria");
    }

    /// <summary>A peça de <c>AiCore/Cena.cs</c> (2026-10-21), com o chanfro de 2 mm depois dos 4 furos Ø4.</summary>
    [Fact]
    public void PecaDaCena_ChanfroDe2mmDepoisDosFurosDeRaio2()
    {
        var prism_f13c = new PrismFeature(new[]
        {
            new Point3D(0, 20.7, 0), new Point3D(2.5, 20.7, 0), new Point3D(17.5, 20.7, 0), new Point3D(34, 20.7, 0),
            new Point3D(34, 40.7, 0), new Point3D(17.5, 40.7, 0), new Point3D(2.5, 40.7, 0), new Point3D(0, 40.7, 0)
        }, height: 5, plane: new WorkPlane(new Point3D(152.11133, -178.5, 4.9), new Vector3D(-1, 0, 0), new Vector3D(0, -1, 0))) { Name = "prism_f13c" };
        var prism_d849 = new PrismFeature(new[]
        {
            new Point3D(0, 17.7, 0), new Point3D(0, 20.7, 0), new Point3D(34, 20.7, 0), new Point3D(34, 14.7, 0), new Point3D(0, 14.7, 0)
        }, height: 10, plane: new WorkPlane(new Point3D(152.1118, -178.5, 4.9), new Vector3D(-1, 0, 0), new Vector3D(0, -1, 0))) { Name = "prism_d849" };
        var box_ac1b = new BoxFeature(new Point3D(142.1118, -212.5, 45.6), 10, 34, 6) { Name = "box_ac1b" };
        var furos = new IFeature[]
        {
            new CylinderFeature(new Point3D(152.1118, -208.5, 22.6), new Vector3D(-1, 0, 0), 2, 20) { Name = "cylinder_5a7f" },
            new CylinderFeature(new Point3D(152.1118, -182.5, 22.6), new Vector3D(-1, 0, 0), 2, 20) { Name = "cylinder_4fc6" },
            new CylinderFeature(new Point3D(152.1118, -208.5, 48.6), new Vector3D(-1, 0, 0), 2, 20) { Name = "cylinder_e320" },
            new CylinderFeature(new Point3D(152.1118, -182.5, 48.6), new Vector3D(-1, 0, 0), 2, 20) { Name = "cylinder_8623" },
        };
        var uniao = new BooleanFeature("boolOp_244e", BooleanOperationType.Union, prism_d849, new IFeature[] { prism_f13c, box_ac1b });
        var furada = new BooleanFeature("boolOp_50c3", BooleanOperationType.Subtract, uniao, furos);
        var chanfro = new FilletFeature("fillet_daaf", furada, 2.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };
        var kernel = new OpenCascadeGeometryKernel();
        var relogio = Stopwatch.StartNew();

        var mesh = kernel.BuildFeatureMesh(chanfro, App());

        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        kernel.BuildProblems.Should().BeEmpty();
        double furadaSemChanfro = MeshVolumeCalculator.ComputeVolume(new OpenCascadeGeometryKernel().BuildFeatureMesh(furada, App()));
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeLessThan(furadaSemChanfro).And.BeGreaterThan(furadaSemChanfro * 0.5);
    }
}
