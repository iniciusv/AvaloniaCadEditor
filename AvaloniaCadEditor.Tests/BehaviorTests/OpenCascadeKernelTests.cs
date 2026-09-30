using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class OpenCascadeKernelTests
{
    [Fact]
    public void ExtrusaoRetangulo_DeveGerarMalha3DValidaComTriangulos()
    {
        // Arrange
        var rect = new RectangleProfile(new Point3D(0, 0, 0), width: 10, height: 20);
        var extrude = new ExtrudeFeature(rect, height: 30.0);
        var kernel = new OpenCascadeGeometryKernel();

        // Act
        var mesh = kernel.BuildFeatureMesh(extrude);

        // Assert: A box has 6 faces * 2 triangles = 12 triangles minimum
        mesh.Should().NotBeNull();
        mesh.VertexCount.Should().BeGreaterThan(0);
        mesh.TriangleCount.Should().BeGreaterThanOrEqualTo(12);
        mesh.Positions.Length.Should().Be(mesh.VertexCount * 3);
        mesh.Indices.Length.Should().Be(mesh.TriangleCount * 3);
    }

    [Fact]
    public void ExtrusaoCirculo_DeveGerarCilindro3DComMalha()
    {
        // Arrange
        var circle = new CircleProfile(new Point3D(0, 0, 0), radius: 5.0);
        var extrude = new ExtrudeFeature(circle, height: 15.0);
        var kernel = new OpenCascadeGeometryKernel();

        // Act
        var mesh = kernel.BuildFeatureMesh(extrude);

        // Assert
        mesh.Should().NotBeNull();
        mesh.VertexCount.Should().BeGreaterThan(0);
        mesh.TriangleCount.Should().BeGreaterThan(12); // Cylinder tessellates into many triangles
    }

    // --- Regressão da orientação de tesselação (ver TessellateShape/FixTriangleWinding) ---
    // A tesselação do OCCT precisa de correção de sentido de giro por componente conexo (não a
    // heurística antiga "normal aponta para longe do centróide de todos os vértices", que falha
    // para peças côncavas, com furo, ou boolean com vários sólidos). Sem isso, o volume calculado
    // por integração (teorema da divergência) sai errado silenciosamente, mesmo a malha "parecendo"
    // certa visualmente e a IoU (baseada em paridade de raycasting) não denunciando nada.

    [Fact]
    public void UniaoComDegrauEmZ_VolumeDeveSerExato()
    {
        var baseBlock = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 5);
        var topBlock = new BoxFeature(new Point3D(5, 5, 5), width: 10, length: 10, height: 10);
        var stepped = BooleanFeature.CreateUnion("Degrau", baseBlock, topBlock);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(stepped);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);

        volume.Should().BeApproximately(3000.0, 3000.0 * 0.01);
    }

    [Fact]
    public void UniaoDeSolidosDesconexos_VolumeDeveSomarOsDois()
    {
        var far1 = new BoxFeature(new Point3D(0, 0, 0), width: 5, length: 5, height: 5);
        var far2 = new BoxFeature(new Point3D(100, 100, 100), width: 5, length: 5, height: 5);
        var farUnion = BooleanFeature.CreateUnion("Far", far1, far2);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(farUnion);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);

        volume.Should().BeApproximately(250.0, 250.0 * 0.01);
    }

    [Fact]
    public void CaixaMenosCilindro_VolumeDeveSerExato()
    {
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 10, length: 10, height: 10);
        var hole = new CylinderFeature(new Point3D(5, 5, -1), new Vector3D(0, 0, 1), radius: 2, height: 12);
        var cut = BooleanFeature.CreateDifference("Cut", box, hole);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(cut);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);
        double expected = 10 * 10 * 10 - Math.PI * 2 * 2 * 10;

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    // --- Booleanas com várias bases/ferramentas numa operação só (em vez de uma cadeia de features) ---

    [Fact]
    public void CaixaMenosVariasFerramentas_VolumeDeveSerExato()
    {
        // Furos quadrados atravessando a caixa de baixo a cima (não cavidades isoladas por dentro:
        // a malha de uma cavidade totalmente interna dispara à parte a heurística de correção de
        // sentido de giro comentada no topo do arquivo, sem relação com a operação booleana em si).
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 20);
        var hole1 = new BoxFeature(new Point3D(2, 2, -1), width: 4, length: 4, height: 22);
        var hole2 = new BoxFeature(new Point3D(8, 8, -1), width: 4, length: 4, height: 22);
        var hole3 = new BoxFeature(new Point3D(14, 14, -1), width: 4, length: 4, height: 22);
        var cut = BooleanFeature.CreateDifference("Cut", box, hole1, hole2, hole3);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(cut);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);
        double expected = 20 * 20 * 20 - 3 * (4 * 4 * 20);

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void CadeiaDeSubtractsEncadeados_DeveDarMesmoVolumeQueUmaOperacaoComVariasFerramentas()
    {
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 20);
        var hole1 = new BoxFeature(new Point3D(2, 2, -1), width: 4, length: 4, height: 22);
        var hole2 = new BoxFeature(new Point3D(8, 8, -1), width: 4, length: 4, height: 22);
        var hole3 = new BoxFeature(new Point3D(14, 14, -1), width: 4, length: 4, height: 22);

        // O mesmo corte, mas como uma cadeia de 3 BooleanFeature (base da próxima = resultado da anterior),
        // do jeito que a UI gerava antes de acrescentar a ferramenta a uma feature existente.
        var step1 = BooleanFeature.CreateDifference("Step1", box, hole1);
        var step2 = BooleanFeature.CreateDifference("Step2", step1, hole2);
        var step3 = BooleanFeature.CreateDifference("Step3", step2, hole3);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(step3);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);
        double expected = 20 * 20 * 20 - 3 * (4 * 4 * 20);

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void UniaoComVariasBasesENenhumaFerramenta_VolumeDeveSomarTodas()
    {
        var b1 = new BoxFeature(new Point3D(0, 0, 0), width: 5, length: 5, height: 5);
        var b2 = new BoxFeature(new Point3D(100, 0, 0), width: 5, length: 5, height: 5);
        var b3 = new BoxFeature(new Point3D(0, 100, 0), width: 5, length: 5, height: 5);
        var union = new BooleanFeature("MultiBaseUnion", BooleanOperationType.Union,
            new IFeature[] { b1, b2, b3 }, Array.Empty<IFeature>());
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(union);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);
        double expected = 3 * (5 * 5 * 5);

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void SubtractComVariasBases_UneAsBasesEDepoisCorta()
    {
        // Duas bases adjacentes que, unidas, formam uma caixa 20x10x10; um furo atravessando a
        // costura entre elas prova que a união das bases acontece antes do corte, não uma por vez.
        var base1 = new BoxFeature(new Point3D(0, 0, 0), width: 10, length: 10, height: 10);
        var base2 = new BoxFeature(new Point3D(10, 0, 0), width: 10, length: 10, height: 10);
        var hole = new CylinderFeature(new Point3D(10, 5, -1), new Vector3D(0, 0, 1), radius: 2, height: 12);

        var cut = BooleanFeature.CreateDifference("Cut", new IFeature[] { base1, base2 }, hole);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(cut);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);
        double expected = 20 * 10 * 10 - Math.PI * 2 * 2 * 10;

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void ExtrusaoDePoligonoConcavo_VolumeDeveSerExato()
    {
        // Perfil em "L" (côncavo) — a heurística antiga de centróide inverte parte dos
        // triângulos desse tipo de perfil, subestimando o volume pela metade ou mais.
        var perfilL = new PolygonProfile(new List<Point3D>
        {
            new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 0, 4),
            new Point3D(4, 0, 4), new Point3D(4, 0, 16), new Point3D(0, 0, 16)
        });
        var extrude = new ExtrudeFeature(perfilL, height: 20.0, direction: new Vector3D(0, 20, 0));
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(extrude);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);

        volume.Should().BeApproximately(1760.0, 1760.0 * 0.01);
    }

    // --- TransformedFeature: copiar/girar/mover uma feature solta (ver prism_60e9 do usuário) ---

    [Fact]
    public void TransformedFeature_PrismaComPlanoProprio_Girado_MantemOVolume()
    {
        // Regressão: um PrismFeature com WorkPlane próprio (vértices locais ao plano, não ao mundo)
        // precisa girar corretamente via Transformed, sem duplicar nem perder volume.
        var verts = new List<Point3D> { new(0, 0, 0), new(20, 0, 0), new(20, 10, 0), new(0, 10, 0) };
        var plane = new WorkPlane(new Point3D(20, 0, 0), Vector3D.UnitX, Vector3D.UnitY);
        var prism = new PrismFeature(verts, height: 4, plane: plane);
        var kernel = new OpenCascadeGeometryKernel();

        double originalVolume = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(prism));
        originalVolume.Should().BeApproximately(800.0, 800.0 * 0.02);

        var girado = prism.Transformed(Placement.RotationAbout(new Point3D(20, 0, 0), Vector3D.UnitZ, 90));
        var mesh = kernel.BuildFeatureMesh(girado);

        mesh.VertexCount.Should().BeGreaterThan(0);
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(originalVolume, Math.Abs(originalVolume) * 0.02);
    }

    [Fact]
    public void TransformedFeature_TranslacaoDeFeatureComFillet_GeraMalhaValida()
    {
        // Só translação reaproveita o sólido da Source (cache) e translada uma cópia - funciona até
        // para features que não podem ser reconstruídas giradas (fillet depende de arestas fixas).
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);
        var comFillet = new FilletFeature("filete", box, 1) { ApplyToAllEdges = true };
        var kernel = new OpenCascadeGeometryKernel();

        var movido = comFillet.Transformed(Placement.Translate(new Vector3D(5, 0, 0)));
        var mesh = kernel.BuildFeatureMesh(movido);

        mesh.VertexCount.Should().BeGreaterThan(0);
    }
}
