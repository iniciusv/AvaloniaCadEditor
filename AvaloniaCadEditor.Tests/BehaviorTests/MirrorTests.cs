using System;
using System.Linq;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Espelho como <see cref="Placement"/>: <c>x.Transformed(Placement.MirrorX(...))</c> para sólidos
/// soltos e <c>at: Placement.Mirror...</c> para instâncias, reconstruídos no kernel como o giro.
/// </summary>
public class MirrorTests
{
    // ---------------------------------------------------------------- Placement

    [Fact]
    public void MirrorX_InverteXEmTornoDoPlano()
    {
        var mirror = Placement.MirrorX(atX: 50);

        mirror.Apply(new Point3D(60, 3, 4)).Should().Be(new Point3D(40, 3, 4));
        mirror.Apply(new Point3D(50, 7, 8)).Should().Be(new Point3D(50, 7, 8), "pontos do plano ficam parados");
        mirror.IsMirrored.Should().BeTrue();
        mirror.Determinant.Should().BeApproximately(-1, 1e-12);
    }

    [Fact]
    public void Mirror_PlanoQualquer_AplicadoDuasVezesVoltaAoOriginal()
    {
        var mirror = Placement.Mirror(new Point3D(10, 20, 30), new Vector3D(1, 2, 3));
        var p = new Point3D(-7, 4, 11);

        var twice = mirror.Then(mirror).Apply(p);

        twice.DistanceTo(p).Should().BeLessThan(1e-9);
        mirror.Then(mirror).IsMirrored.Should().BeFalse();
    }

    [Fact]
    public void Mirror_InversaDesfaz()
    {
        var placement = Placement.MirrorY(atY: 5).Then(Placement.At(100, 0, 0, rotationZDegrees: 30));
        var p = new Point3D(1, 2, 3);

        placement.Then(placement.Inverse()).Apply(p).DistanceTo(p).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void Mirror_NaoEhConfundidoComGiroEmZ()
    {
        // Diagonal (-1, 1, 1): passa no teste "Z continua Z", mas não é um giro de 180°.
        Placement.MirrorX().IsRotationAboutZOnly.Should().BeFalse();
        Placement.MirrorX().HasRotation.Should().BeTrue("quem só sabe transladar precisa reconstruir o sólido");
        Placement.At(0, 0, 0, rotationZDegrees: 180).IsRotationAboutZOnly.Should().BeTrue();
    }

    [Fact]
    public void Mirror_NormalNula_Recusa()
    {
        Action act = () => Placement.Mirror(Point3D.Origin, new Vector3D(0, 0, 0));

        act.Should().Throw<ArgumentException>();
    }

    // ---------------------------------------------------------------- Malha

    [Fact]
    public void MeshTransform_ComEspelho_MantemOsTriangulosVoltadosParaFora()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(new BoxFeature(Point3D.Origin, 10, 20, 30));
        double original = SignedVolume(mesh);

        var mirrored = mesh.Transform(Placement.MirrorX(atX: 50));

        SignedVolume(mirrored).Should().BeApproximately(original, Math.Abs(original) * 1e-6,
            "a ordem dos vértices é invertida junto com a reflexão, então o volume orientado não troca de sinal");
    }

    // ---------------------------------------------------------------- Kernel

    [Fact]
    public void PrismaEmL_Espelhado_MantemOVolumeEVaiParaOOutroLado()
    {
        var prisma = new PrismFeature(new[]
        {
            new Point3D(0, 0, 0), new Point3D(30, 0, 0), new Point3D(30, 10, 0),
            new Point3D(10, 10, 0), new Point3D(10, 40, 0), new Point3D(0, 40, 0)
        }, height: 5);
        var kernel = new OpenCascadeGeometryKernel();
        double original = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(prisma));
        original.Should().BeApproximately(30 * 10 * 5 + 10 * 30 * 5, 1);

        var espelhado = prisma.Transformed(Placement.MirrorX(atX: -5));
        var mesh = kernel.BuildFeatureMesh(espelhado);

        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(original, original * 0.01);
        var xs = mesh.Positions.Where((_, i) => i % 3 == 0).ToList();
        xs.Min().Should().BeApproximately(-40f, 0.01f, "x = 30 vai para 2·(-5) - 30");
        xs.Max().Should().BeApproximately(-10f, 0.01f, "x = 0 vai para 2·(-5) - 0");
    }

    [Fact]
    public void Cotovelo_Espelhado_DobraParaOLadoEspelhado()
    {
        // O eixo do Revolve é um pseudovetor: sem trocar o sinal dele no espelho, o cotovelo que sai
        // em +X e dobra para +Y espelhado em Y dobraria para -X em vez de seguir em +X.
        var curva = new SweepFeature(
            start: Point3D.Origin, startTangent: Vector3D.UnitX, sectionRadius: 5,
            bendDirection: Vector3D.UnitY, bendRadius: 30, angleDegrees: 90);
        var kernel = new OpenCascadeGeometryKernel();
        double original = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(curva));

        var mesh = kernel.BuildFeatureMesh(curva.Transformed(Placement.MirrorY()));

        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(original, original * 0.02);
        var xs = mesh.Positions.Where((_, i) => i % 3 == 0).ToList();
        var ys = mesh.Positions.Where((_, i) => i % 3 == 1).ToList();
        ys.Min().Should().BeApproximately(-30f, 0.2f, "a dobra agora desce para -Y");
        xs.Min().Should().BeGreaterThan(-0.2f, "e continua avançando em +X");
    }

    [Fact]
    public void Booleana_ComFerramentaEspelhada_CortaNoLadoEspelhado()
    {
        var chapa = new BoxFeature(new Point3D(-50, 0, 0), 100, 20, 5);
        var furo = new CylinderFeature(new Point3D(30, 10, -1), Vector3D.UnitZ, 3, 7) { IsTransientTool = true };
        var furo_espelhado = furo.Transformed(Placement.MirrorX(), name: "furo_espelhado");
        var kernel = new OpenCascadeGeometryKernel();

        double cheio = 100 * 20 * 5;
        double umFuro = cheio - MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(
            BooleanFeature.CreateDifference("um", chapa, furo)));
        double doisFuros = cheio - MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(
            BooleanFeature.CreateDifference("dois", chapa, furo, furo_espelhado)));

        doisFuros.Should().BeApproximately(2 * umFuro, umFuro * 0.02, "o furo espelhado cai em x = -30, sem sobrepor o original");
    }

    [Fact]
    public void BooleanFeature_RecusaTransformedEspelhadoComFillet()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);
        var comFillet = new FilletFeature("filete", box, 1) { ApplyToAllEdges = true };
        var espelhada = comFillet.Transformed(Placement.MirrorX());
        var ferramenta = new BoxFeature(new Point3D(-100, -100, -100), 5, 5, 5);

        Action act = () => BooleanFeature.CreateDifference("corte", espelhada, ferramenta);

        act.Should().Throw<NotSupportedException>().WithMessage("*espelhada*fillet*");
    }

    // ---------------------------------------------------------------- IA

    [Fact]
    public void ReferenciaDaApi_EnsinaEspelhoETransformed()
    {
        string reference = CadScriptApiReference.Build();

        reference.Should().Contain("Placement.MirrorX(double atX = 0)");
        reference.Should().Contain("Placement.Mirror(Point3D pointOnPlane, Vector3D normal)");
        reference.Should().Contain("feature.Transformed(Placement placement, string? name = null)");
        CadScriptRules.Transforms.Should().Contain("Placement.MirrorX(atX: 50)");
    }

    /// <summary>Volume com sinal (teorema da divergência): positivo quando os triângulos estão voltados para fora.</summary>
    private static double SignedVolume(TriangleMesh mesh)
    {
        double volume = 0;
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = Vertex(mesh, mesh.Indices[i]);
            var b = Vertex(mesh, mesh.Indices[i + 1]);
            var c = Vertex(mesh, mesh.Indices[i + 2]);
            volume += (a.X * (b.Y * c.Z - b.Z * c.Y) + a.Y * (b.Z * c.X - b.X * c.Z) + a.Z * (b.X * c.Y - b.Y * c.X)) / 6.0;
        }
        return volume;
    }

    private static Point3D Vertex(TriangleMesh mesh, uint index) =>
        new Point3D(mesh.Positions[index * 3], mesh.Positions[index * 3 + 1], mesh.Positions[index * 3 + 2]);
}
