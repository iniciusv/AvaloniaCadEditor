using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>Sub-peça de teste: um furo vertical, já no lugar da hospedeira.</summary>
public sealed record FuroTeste(double X = 0, double Y = 0, double Raio = 2) : Part
{
    protected override IFeature Build() =>
        new CylinderFeature(new Point3D(X, Y, -1), Vector3D.UnitZ, Raio, 12) { Name = "furo" };
}

/// <summary>Bloco 40 × 20 × 10 com três FuroTeste: dois iguais e um diferente.</summary>
public sealed record BlocoComFurosTeste(bool Girado = false) : Part
{
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(Point3D.Origin, 40, 20, 10) { Name = "corpo" };
        var furo_1 = new FuroTeste(10, 10).Instance("furo_1");
        var furo_2 = new FuroTeste(10, 10).Instance("furo_2", at: Placement.At(10, 0, 0));
        // Girado 90° em Y em torno do próprio centro: vira um furo horizontal ao longo de X.
        var furo_3 = new FuroTeste(35, 10, Raio: 3).Instance("furo_3",
            at: Girado ? Placement.RotationAbout(new Point3D(35, 10, 5), Vector3D.UnitY, 90) : null);
        return BooleanFeature.CreateDifference("bloco", corpo, furo_1, furo_2, furo_3);
    }
}

/// <summary>Peça usada como ferramenta booleana dentro do Build() de outra (<see cref="PartExtensions.Instance{T}"/>).</summary>
public class NestedPartTests
{
    [Fact]
    public void InstanciaDentroDoBuild_CompartilhaDefinicao_ENaoEntraNaCena()
    {
        var builder = new SceneBuilder();
        PartInstance<BlocoComFurosTeste> bloco;
        using (SceneHost.Begin(builder))
        {
            bloco = Scene.Add(new BlocoComFurosTeste());
        }

        builder.Features.Should().ContainSingle().Which.Should().BeSameAs(bloco);
        // Bloco + FuroTeste(10, 10) + FuroTeste(35, 10, 3): os dois furos iguais dividem a geometria.
        builder.DefinitionBuildCount.Should().Be(3);

        var ferramentas = ((BooleanFeature)bloco.Definition).ToolFeatures.Cast<PartInstance<FuroTeste>>().ToList();
        ferramentas.Select(f => f.Name).Should().Equal("furo_1", "furo_2", "furo_3");
        ferramentas[0].Definition.Should().BeSameAs(ferramentas[1].Definition);
        ferramentas[2].Definition.Should().NotBeSameAs(ferramentas[0].Definition);
    }

    [Fact]
    public void InstanciaComoFerramenta_CortaOMesmoQueOsSolidosSoltos()
    {
        var builder = new SceneBuilder();
        IFeature aninhada;
        using (SceneHost.Begin(builder))
        {
            aninhada = Scene.Add(new BlocoComFurosTeste()).Definition;
        }

        var solta = BooleanFeature.CreateDifference("bloco",
            new BoxFeature(Point3D.Origin, 40, 20, 10) { Name = "corpo" },
            new CylinderFeature(new Point3D(10, 10, -1), Vector3D.UnitZ, 2, 12) { Name = "furo_1" },
            new CylinderFeature(new Point3D(20, 10, -1), Vector3D.UnitZ, 2, 12) { Name = "furo_2" },
            new CylinderFeature(new Point3D(35, 10, -1), Vector3D.UnitZ, 3, 12) { Name = "furo_3" });

        var kernel = new OpenCascadeGeometryKernel();
        var volumeAninhada = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(aninhada));
        var volumeSolta = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(solta));

        volumeAninhada.Should().BeLessThan(40 * 20 * 10 - 500, "os três furos têm que ter sido cortados");
        volumeAninhada.Should().BeApproximately(volumeSolta, 1e-3);
    }

    [Fact]
    public void InstanciaGiradaComoFerramenta_CortaNaOrientacaoDoPlacement()
    {
        var builder = new SceneBuilder();
        IFeature bloco;
        using (SceneHost.Begin(builder))
        {
            bloco = Scene.Add(new BlocoComFurosTeste(Girado: true)).Definition;
        }

        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(bloco);

        // Furo horizontal de x = 29 a 41, eixo em (y = 10, z = 5): a face x = 40 do bloco ganha uma
        // boca de raio 3, ou seja, vértices nessa face a ~3 mm do eixo.
        var raiosNaFace = Enumerable.Range(0, mesh.VertexCount)
            .Where(i => mesh.Positions[i * 3] > 39.99)
            .Select(i => System.Math.Sqrt(
                System.Math.Pow(mesh.Positions[i * 3 + 1] - 10, 2) + System.Math.Pow(mesh.Positions[i * 3 + 2] - 5, 2)))
            .ToList();
        raiosNaFace.Min().Should().BeApproximately(3, 0.05);
    }
}
