using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>Peça de teste: uma placa retangular.</summary>
public sealed record Placa([Range(10, 500)] double Largura, double Comprimento = 40, double Espessura = 5) : Part
{
    public Point3D CantoOposto => new(Largura, Comprimento, Espessura);

    protected override IFeature Build() =>
        new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "placa" };
}

/// <summary>Fase 2: peças paramétricas como records, instâncias posicionadas e cache por valor.</summary>
public class PartTests
{
    private const string FlangeSource = """
        using System.ComponentModel.DataAnnotations;

        /// <summary>Flange circular com furos de fixação.</summary>
        public sealed record Flange([Range(20, 500)] double Diametro, int Furos = 6, double Espessura = 10) : Part
        {
            public double RaioFuros => Diametro * 0.4;
            public Point3D CentroTopo => new(0, 0, Espessura);

            protected override IFeature Build()
            {
                var disco = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, Diametro / 2, Espessura) { Name = "disco" };
                var pinos = Enumerable.Range(0, Furos).Select(i =>
                {
                    double a = 2 * Math.PI * i / Furos;
                    var centro = new Point3D(RaioFuros * Math.Cos(a), RaioFuros * Math.Sin(a), 0);
                    return (IFeature)new CylinderFeature(centro, Vector3D.UnitZ, 4, Espessura) { IsTransientTool = true };
                }).ToArray();
                return BooleanFeature.CreateDifference("corpo", disco, pinos);
            }
        }
        """;

    private const string FlangeScene = """
        for (int i = 0; i < 5; i++)
            Add(new Flange(120, Furos: 8), at: Placement.At(i * 150, 0, 0));

        var base1 = Add(new Flange(80));
        for (int i = 1; i < 5; i++)
            Add(base1.Part, at: Placement.At(i * 150, 200, 0));

        var tampa = Add(base1.Part with { Espessura = 4 }, at: Placement.At(base1.World(f => f.CentroTopo)), name: "tampa");
        """;

    [Fact]
    public void DezInstancias_DuasParametrizacoes_ConstroemAGeometriaDuasVezes()
    {
        var builder = new SceneBuilder();
        using (SceneHost.Begin(builder))
        {
            for (int i = 0; i < 5; i++)
                Scene.Add(new Placa(100), at: Placement.At(i * 120, 0, 0));
            for (int i = 0; i < 5; i++)
                Scene.Add(new Placa(100) with { Espessura = 8 }, at: Placement.At(i * 120, 100, 0));
        }

        builder.Features.Should().HaveCount(10);
        builder.DefinitionBuildCount.Should().Be(2);
        builder.Features.OfType<PartInstance>().Select(i => i.Definition).Distinct().Should().HaveCount(2);
        builder.Features[0].Name.Should().Be("Placa_1");
        builder.Features[9].Name.Should().Be("Placa_10");
    }

    [Fact]
    public void Placement_RotacionaAntesDeTransladarEComboComThen()
    {
        Placement.At(10, 0, 0, rotationZDegrees: 90).Apply(new Point3D(1, 0, 0))
            .Should().Be(new Point3D(10, 1, 0));

        Placement.Rotation(Vector3D.UnitX, 90).Apply(new Point3D(0, 1, 0))
            .Should().Be(new Point3D(0, 0, 1));

        var combinado = Placement.RotationZ(90).Then(Placement.At(0, 0, 5));
        combinado.Apply(new Point3D(2, 0, 0)).Should().Be(new Point3D(0, 2, 5));
        combinado.ApplyVector(Vector3D.UnitX).Should().Be(new Vector3D(0, 1, 0));
    }

    [Fact]
    public void World_ConvertePortaDaPecaParaCoordenadasDoMundo()
    {
        using var _ = SceneHost.Begin(new SceneBuilder());

        var placa = Scene.Add(new Placa(100), at: Placement.At(0, 0, 50, rotationZDegrees: 90));

        placa.World(p => p.CantoOposto).Should().Be(new Point3D(-40, 100, 55));
    }

    [Fact]
    public void ParametroForaDoRange_FalhaComMensagemClara()
    {
        using var _ = SceneHost.Begin(new SceneBuilder());

        Action act = () => Scene.Add(new Placa(5));

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Placa.Largura = 5*[10, 500]*");
    }

    [Fact]
    public void GetParameters_ListaOConstrutorPrimarioComValoresEIntervalos()
    {
        var parametros = new Placa(100, Espessura: 3).GetParameters();

        parametros.Select(p => p.Name).Should().Equal("Largura", "Comprimento", "Espessura");
        parametros.Select(p => p.Value).Should().Equal(100.0, 40.0, 3.0);
        parametros[0].Range.Should().NotBeNull();
        parametros[0].ToString().Should().Be("Largura = 100");
    }

    [Fact]
    public async Task CenaComPecaEmOutroArquivo_ReusaDefinicoesPorValor()
    {
        var docs = new[] { new ScriptDocument("Pecas/Flange.cs", FlangeSource), new ScriptDocument("Cena.cs", FlangeScene) };

        var result = await new RoslynSceneEvaluator().EvaluateAsync(docs);

        result.Success.Should().BeTrue(because: result.ErrorMessage);
        var instancias = result.Features.Cast<PartInstance>().ToList();
        instancias.Should().HaveCount(11);
        instancias.Select(i => i.Definition).Distinct().Should().HaveCount(3, "(120, 8 furos), (80) e (80, espessura 4)");

        var tampa = instancias.Single(i => i.Name == "tampa");
        tampa.Placement.Translation.Z.Should().BeApproximately(10, 1e-9, "a tampa fica no topo da base de espessura 10");
        FeatureOrigin.Of(tampa)!.FilePath.Should().Be("Cena.cs");
    }

    [Fact]
    public void Kernel_ConstroiInstanciaTransformandoAMalhaDaDefinicao()
    {
        using var _ = SceneHost.Begin(new SceneBuilder());
        var a = Scene.Add(new Placa(100));
        var b = Scene.Add(new Placa(100), at: Placement.At(1000, 0, 0));
        var kernel = new OpenCascadeGeometryKernel();

        var ma = kernel.BuildFeatureMesh(a);
        var mb = kernel.BuildFeatureMesh(b);

        ma.TriangleCount.Should().BeGreaterThan(0);
        mb.TriangleCount.Should().Be(ma.TriangleCount);
        MinX(mb).Should().BeApproximately(MinX(ma) + 1000f, 1e-3f);
    }

    [Fact]
    public async Task Check_CenaComPecas_ValidaAGeometriaDeTodasAsInstancias()
    {
        using var folder = new SceneTestFolder("CenaFlanges");
        var project = folder.CreateScene();
        folder.Write("Pecas/Flange.cs", FlangeSource);
        folder.Write("Cena.cs", FlangeScene);

        var report = await new SceneCheckService(new RoslynSceneEvaluator(), new OpenCascadeGeometryKernel())
            .CheckAsync(project, checkGeometry: true);

        report.Success.Should().BeTrue(because: report.Format());
        report.FeatureCount.Should().Be(11);
    }

    [Fact]
    public void ReferenciaDaApi_DocumentaPecas()
    {
        string reference = CadScriptApiReference.Build();

        reference.Should().Contain("PEÇAS PARAMÉTRICAS");
        reference.Should().Contain("Add<T>(T part");
        reference.Should().Contain("static Placement Placement.At(");
    }

    private static float MinX(TriangleMesh mesh) =>
        Enumerable.Range(0, mesh.VertexCount).Min(i => mesh.Positions[i * 3]);
}
