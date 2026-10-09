using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
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

    // --- ConeFeature: tronco de cone por revolução exata ---
    // A lateral é uma superfície cônica de verdade, então a malha é um polígono INSCRITO de N lados
    // e o volume medido fica abaixo do exato. N vem do piso de facetas de TessellateShape
    // (ProfileSegments = 32), o que dá k = (N/2π)·sin(2π/N) = 0,9968 → 0,32% abaixo, dentro da
    // tolerância de 1% usada aqui. Sem esse piso, a deflexão padrão daria 13-23 facetas e 1,2-3,8%.

    [Fact]
    public void TroncoDeCone_VolumeDeveSerExato()
    {
        var cone = new ConeFeature(Point3D.Origin, new Vector3D(0, 0, 1), bottomRadius: 10, topRadius: 5, height: 20);
        var kernel = new OpenCascadeGeometryKernel();

        double volume = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(cone));
        double expected = Math.PI * 20 / 3.0 * (10 * 10 + 10 * 5 + 5 * 5);

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void ConeCompleto_TopRadiusZero_VolumeDeveSerExato()
    {
        // O ápice entra como UM ponto só no perfil; repetir (0, h) daria uma aresta de comprimento zero.
        var cone = new ConeFeature(Point3D.Origin, new Vector3D(0, 0, 1), bottomRadius: 10, topRadius: 0, height: 20);
        var kernel = new OpenCascadeGeometryKernel();

        double volume = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(cone));
        double expected = Math.PI * 10 * 10 * 20 / 3.0;

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void ConeComRaiosIguais_TemOVolumeDeUmCilindro()
    {
        // Conferência independente: se o revolve tivesse gerado um ANEL em vez de um sólido
        // (perfil que não encosta no eixo), este número sairia inconfundivelmente errado.
        var cone = new ConeFeature(Point3D.Origin, new Vector3D(0, 0, 1), bottomRadius: 6, topRadius: 6, height: 15);
        var kernel = new OpenCascadeGeometryKernel();

        double volume = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(cone));
        double expected = Math.PI * 6 * 6 * 15;

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void CaixaMenosCone_FuroEscareado_VolumeDeveSerExato()
    {
        var chapa = new BoxFeature(new Point3D(0, 0, 0), width: 40, length: 40, height: 10);
        var escareado = new ConeFeature(new Point3D(20, 20, 10), new Vector3D(0, 0, -1), bottomRadius: 6, topRadius: 3, height: 4);
        var cut = BooleanFeature.CreateDifference("Escareado", chapa, escareado);
        var kernel = new OpenCascadeGeometryKernel();

        double volume = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(cut));
        double expected = 40 * 40 * 10 - Math.PI * 4 / 3.0 * (6 * 6 + 6 * 3 + 3 * 3);

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void ConeGirado_ComoFeatureTransformada_MantemOVolumeEMudaDeLugar()
    {
        // O único teste que exercita o 'transform' de BuildCone. Um revolve feito em torno do eixo
        // NÃO transformado pode ter o volume certo e estar na orientação errada — daí a asserção
        // de posição: depois de girar 90° em torno de X, o ápice tem que ir de +Z para -Y.
        var cone = new ConeFeature(Point3D.Origin, new Vector3D(0, 0, 1), bottomRadius: 8, topRadius: 0, height: 20);
        var kernel = new OpenCascadeGeometryKernel();

        double originalVolume = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(cone));
        originalVolume.Should().BeApproximately(Math.PI * 8 * 8 * 20 / 3.0, Math.PI * 8 * 8 * 20 / 3.0 * 0.01);

        var girado = cone.Transformed(Placement.RotationAbout(Point3D.Origin, Vector3D.UnitX, 90));
        var mesh = kernel.BuildFeatureMesh(girado);

        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(originalVolume, originalVolume * 0.02);

        var ys = mesh.Positions.Where((_, i) => i % 3 == 1).ToList();
        ys.Min().Should().BeApproximately(-20f, 0.1f, "o ápice foi de +Z para -Y");
        ys.Max().Should().BeApproximately(0f, 0.1f, "a base ficou no plano y = 0");
    }

    [Fact]
    public void ConeDegenerado_AlturaZero_DevolveMalhaVaziaSemExcecao()
    {
        var cone = new ConeFeature(Point3D.Origin, new Vector3D(0, 0, 1), bottomRadius: 10, topRadius: 5, height: 0);
        var kernel = new OpenCascadeGeometryKernel();

        kernel.BuildFeatureMesh(cone).VertexCount.Should().Be(0);
    }

    // --- SweepFeature: cotovelo por uma única revolução (etapa 1 da varredura) ---
    // O volume de um sólido de revolução é o de Pappus: A · Rc · θ, com Rc = distância do eixo ao
    // centroide da seção. Duas fontes de déficit, e os testes separam as duas:
    //   - seção CIRCULAR vira polígono inscrito de ProfileSegments(32) lados → k = 0,9968;
    //   - a superfície de revolução é exata no modelo, mas a MALHA dela é inscrita.
    // Por isso a seção retangular (sem o primeiro déficit) admite tolerância bem mais apertada.

    [Fact]
    public void CotoveloDeSecaoRetangular_VolumeDeveSeguirPappus()
    {
        // Seção 10x4 centrada no caminho, raio de dobra 50, 90°. A seção é exata (4 vértices),
        // então o único erro é a tesselação do cotovelo.
        var secao = new RectangleProfile(
            Point3D.Origin, width: 10, height: 4,
            plane: new WorkPlane(Vector3D.UnitX, Point3D.Origin));
        var sweep = new SweepFeature(secao, bendDirection: Vector3D.UnitY, bendRadius: 50, angleDegrees: 90);
        var kernel = new OpenCascadeGeometryKernel();

        double volume = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(sweep));
        double expected = 10 * 4 * 50 * (Math.PI / 2);

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void CotoveloDeSecaoCircular_VolumeDeveSeguirPappus()
    {
        var sweep = new SweepFeature(
            start: Point3D.Origin, startTangent: Vector3D.UnitX, sectionRadius: 5,
            bendDirection: Vector3D.UnitY, bendRadius: 30, angleDegrees: 90);
        var kernel = new OpenCascadeGeometryKernel();

        double volume = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(sweep));
        double expected = Math.PI * 5 * 5 * 30 * (Math.PI / 2);

        volume.Should().BeApproximately(expected, expected * 0.02);
    }

    [Fact]
    public void Varredura360_DeSecaoDeslocada_FechaUmAnel()
    {
        // Conferência independente do caso fechado: a seção NÃO encosta no eixo, então o revolve
        // tem que dar um anel (toro de seção quadrada), não um disco maciço. Se o sólido saísse
        // cheio, o volume seria π·R²·h em vez de Pappus, um erro inconfundível.
        var secao = new RectangleProfile(
            Point3D.Origin, width: 6, height: 6,
            plane: new WorkPlane(Vector3D.UnitX, new Point3D(0, 0, 0)));
        var anel = new SweepFeature(secao, bendDirection: Vector3D.UnitY, bendRadius: 40, angleDegrees: 360);
        var kernel = new OpenCascadeGeometryKernel();

        double volume = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(anel));
        double expected = 6 * 6 * 40 * 2 * Math.PI;

        volume.Should().BeApproximately(expected, expected * 0.01);
    }

    [Fact]
    public void CotoveloEmendadoNoTuboReto_VolumeDeveSerASoma()
    {
        // O teste que de fato valida o eixo e o referencial: se BendCenter ou BendAxis estivessem
        // errados, o cotovelo não encostaria na tampa do cilindro (união com vão, volume maior que
        // a soma) ou entraria por dentro dele (sobreposição, volume menor). Também é a prévia da
        // etapa 2, que vai fundir trechos exatamente assim, só que dentro da própria feature.
        var reto = new CylinderFeature(Point3D.Origin, Vector3D.UnitX, radius: 5, height: 50);
        var curva = new SweepFeature(
            start: new Point3D(50, 0, 0), startTangent: Vector3D.UnitX, sectionRadius: 5,
            bendDirection: Vector3D.UnitY, bendRadius: 30, angleDegrees: 90);
        var tubo = BooleanFeature.CreateUnion("tubo", reto, curva);
        var kernel = new OpenCascadeGeometryKernel();

        var meshUniao = kernel.BuildFeatureMesh(tubo);
        double volume = MeshVolumeCalculator.ComputeVolume(meshUniao);
        double expected = Math.PI * 5 * 5 * 50 + Math.PI * 5 * 5 * 30 * (Math.PI / 2);

        volume.Should().BeApproximately(expected, expected * 0.02);

        // O volume sozinho NÃO distingue um sólido fundido de um compound com os dois pedaços
        // apenas encostados: os dois têm o mesmo volume. A área distingue — a fusão de verdade come
        // as DUAS tampas coincidentes da junção. Comparar a área da união com a soma das partes
        // dispensa prever a área das superfícies curvas: elas se cancelam entre os dois lados.
        double areaUniao = MeshAreaCalculator.ComputeArea(meshUniao);
        double areaPartes = MeshAreaCalculator.ComputeArea(kernel.BuildFeatureMesh(reto))
                          + MeshAreaCalculator.ComputeArea(kernel.BuildFeatureMesh(curva));
        double tampa = MeshAreaCalculator.InscribedPolygonArea(5, 32);

        areaUniao.Should().BeApproximately(areaPartes - 2 * tampa, tampa * 0.1,
            "a união tem que eliminar as duas tampas da junção, não só encostar os sólidos");
    }

    [Fact]
    public void CotoveloSaiNoFimDoArco_ComATangenteGirada()
    {
        // EndCenter/EndTangent são o contrato para encadear trechos; o teste acima prova que o
        // sólido realmente chega lá, este prova que a feature SABE onde é.
        var curva = new SweepFeature(
            start: new Point3D(50, 0, 0), startTangent: Vector3D.UnitX, sectionRadius: 5,
            bendDirection: Vector3D.UnitY, bendRadius: 30, angleDegrees: 90);

        var arco = curva.Path.Segments.Should().ContainSingle().Subject;
        arco.IsArc.Should().BeTrue();
        arco.ArcCenter.X.Should().BeApproximately(50, 1e-9);
        arco.ArcCenter.Y.Should().BeApproximately(30, 1e-9);

        curva.EndCenter.X.Should().BeApproximately(80, 1e-6, "o arco avança um raio em X");
        curva.EndCenter.Y.Should().BeApproximately(30, 1e-6, "e sobe um raio em Y");
        curva.EndCenter.Z.Should().BeApproximately(0, 1e-6);

        curva.EndTangent.X.Should().BeApproximately(0, 1e-6);
        curva.EndTangent.Y.Should().BeApproximately(1, 1e-6, "depois de 90° o tubo segue em +Y");
    }

    [Fact]
    public void CotoveloGirado_ComoFeatureTransformada_MantemOVolumeEMudaDeLugar()
    {
        // O único teste que exercita o 'transform' de BuildSweep. Revolver em torno do eixo NÃO
        // transformado daria o volume certo na orientação errada — daí a asserção de posição.
        var curva = new SweepFeature(
            start: Point3D.Origin, startTangent: Vector3D.UnitX, sectionRadius: 5,
            bendDirection: Vector3D.UnitY, bendRadius: 30, angleDegrees: 90);
        var kernel = new OpenCascadeGeometryKernel();

        double original = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(curva));
        original.Should().BeGreaterThan(0);

        // 90° em torno de X leva o plano da dobra de XY para XZ: o que subia em +Y passa a subir em +Z.
        var girado = curva.Transformed(Placement.RotationAbout(Point3D.Origin, Vector3D.UnitX, 90));
        var mesh = kernel.BuildFeatureMesh(girado);

        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(original, original * 0.02);

        // Antes de girar: y vai até 30 (a face final é o plano y = 30, perpendicular à tangente
        // final — o raio da seção se estende ao longo do raio do arco e do eixo, não além dessa
        // face) e z só até 5 (meia seção para cada lado do plano da dobra). Girar 90° em torno de X
        // troca os dois papéis, então os dois limites juntos provam que o cotovelo girou de fato.
        var ys = mesh.Positions.Where((_, i) => i % 3 == 1).ToList();
        var zs = mesh.Positions.Where((_, i) => i % 3 == 2).ToList();
        zs.Max().Should().BeApproximately(30f, 0.2f, "o plano da dobra foi de XY para XZ");
        ys.Max().Should().BeApproximately(5f, 0.2f, "e o que sobrava em z virou a espessura em y");
    }

    [Theory]
    // Ângulo nulo, negativo e acima de uma volta inteira.
    [InlineData(5.0, 30.0, 0.0)]
    [InlineData(5.0, 30.0, -90.0)]
    [InlineData(5.0, 30.0, 400.0)]
    // Raio de dobra nulo.
    [InlineData(5.0, 0.0, 90.0)]
    // Raio de dobra MENOR que a seção: a parede interna cruzaria o eixo e o sólido dobraria para
    // dentro de si mesmo. É o raio mínimo de curvatura de uma dobra real.
    [InlineData(5.0, 3.0, 90.0)]
    public void VarreduraDegenerada_DevolveMalhaVaziaSemExcecao(double sectionRadius, double bendRadius, double angleDegrees)
    {
        var sweep = new SweepFeature(
            start: Point3D.Origin, startTangent: Vector3D.UnitX, sectionRadius: sectionRadius,
            bendDirection: Vector3D.UnitY, bendRadius: bendRadius, angleDegrees: angleDegrees);
        var kernel = new OpenCascadeGeometryKernel();

        kernel.BuildFeatureMesh(sweep).VertexCount.Should().Be(0);
    }

    [Fact]
    public void DobraParalelaATangente_NaoDefineArco_EDevolveMalhaVazia()
    {
        // bendDirection paralela à tangente não tem componente radial nenhum: sobra o vetor nulo,
        // que não define arco.
        var sweep = new SweepFeature(
            start: Point3D.Origin, startTangent: Vector3D.UnitX, sectionRadius: 5,
            bendDirection: Vector3D.UnitX, bendRadius: 30, angleDegrees: 90);
        var kernel = new OpenCascadeGeometryKernel();

        sweep.Path.Error.Should().Contain("paralela");
        kernel.BuildFeatureMesh(sweep).VertexCount.Should().Be(0);
    }

    // --- SweepFeature: caminho de vários trechos (etapa 2) ---
    // Para uma seção cujo centroide anda sobre o caminho, Pappus dá volume = A·L e área lateral =
    // P·L, com L o comprimento do caminho. Isso vale igual para reta e arco, então as duas contas
    // não dependem de como o caminho foi dividido — é exatamente o que se quer para conferir a
    // emenda dos trechos. A área é a asserção que importa: o volume de um compound mal fundido é o
    // mesmo de um sólido, mas cada junção que não fundiu deixa DUAS tampas para trás.

    private const int Segs = 32;   // TessellationSettings.ProfileSegments

    [Fact]
    public void TuboEmL_PolilinhaComCantoArredondado_VolumeEAreaDeSeguirPappus()
    {
        // Canto de 90° com raio 30: cada reta recua R·tan(45°) = 30. 100 e 80 viram 70 e 50.
        var caminho = SweepPath.FromPolyline(
            new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) },
            cornerRadius: 30);
        caminho.Error.Should().BeNull();
        caminho.Segments.Should().HaveCount(3, "reta, arco, reta");

        var tubo = new SweepFeature(SweepSection.Circle(5), caminho);
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(tubo);

        double comprimento = 70 + 30 * (Math.PI / 2) + 50;
        double areaSecao = MeshAreaCalculator.InscribedPolygonArea(5, Segs);
        double perimetro = MeshAreaCalculator.InscribedPolygonPerimeter(5, Segs);

        MeshVolumeCalculator.ComputeVolume(mesh)
            .Should().BeApproximately(areaSecao * comprimento, areaSecao * comprimento * 0.01);

        // Lateral + as duas tampas das PONTAS. Se alguma das duas junções não tivesse fundido,
        // sobrariam mais duas tampas por junção — 4 × 78 ≈ 313 num total de ~5400, bem fora de 1%.
        MeshAreaCalculator.ComputeArea(mesh)
            .Should().BeApproximately(perimetro * comprimento + 2 * areaSecao,
                                      (perimetro * comprimento + 2 * areaSecao) * 0.01,
                                      "um sólido fundido só tem as tampas das pontas");
    }

    [Fact]
    public void CaminhoComCantoForaDePlano_Funciona3D()
    {
        // O segundo canto vira de +Y para +Z: o arco dele NÃO está no plano do primeiro. Se o
        // solver tratasse o caminho como plano, este volume sairia errado ou o sólido nem fecharia.
        var caminho = SweepPath.FromPolyline(
            new[]
            {
                Point3D.Origin,
                new Point3D(100, 0, 0),
                new Point3D(100, 80, 0),
                new Point3D(100, 80, 60)
            },
            cornerRadius: 20);
        caminho.Error.Should().BeNull();
        caminho.Segments.Should().HaveCount(5, "reta, arco, reta, arco, reta");

        var tubo = new SweepFeature(SweepSection.Circle(4), caminho);
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(tubo);

        // Cada canto de 90° recua 20 de cada reta vizinha.
        double comprimento = (100 - 20) + 20 * (Math.PI / 2) + (80 - 40) + 20 * (Math.PI / 2) + (60 - 20);
        double areaSecao = MeshAreaCalculator.InscribedPolygonArea(4, Segs);
        double perimetro = MeshAreaCalculator.InscribedPolygonPerimeter(4, Segs);

        MeshVolumeCalculator.ComputeVolume(mesh)
            .Should().BeApproximately(areaSecao * comprimento, areaSecao * comprimento * 0.01);
        MeshAreaCalculator.ComputeArea(mesh)
            .Should().BeApproximately(perimetro * comprimento + 2 * areaSecao,
                                      (perimetro * comprimento + 2 * areaSecao) * 0.01,
                                      "as quatro junções fundiram");
    }

    [Fact]
    public void CaminhoDeDoisPontos_EhUmaRetaSo_SemExigirRaioDeCanto()
    {
        var caminho = SweepPath.FromPolyline(new[] { Point3D.Origin, new Point3D(0, 0, 40) }, cornerRadius: 0);

        caminho.Error.Should().BeNull();
        caminho.Segments.Should().ContainSingle().Which.IsArc.Should().BeFalse();

        var barra = new SweepFeature(SweepSection.Rectangle(20, 10), caminho);
        MeshVolumeCalculator.ComputeVolume(new OpenCascadeGeometryKernel().BuildFeatureMesh(barra))
            .Should().BeApproximately(20 * 10 * 40, 1.0);
    }

    [Fact]
    public void PontoAlinhadoNoMeio_NaoViraCanto()
    {
        // Um ponto a mais em cima da mesma reta não muda a geometria — e não pode virar dois
        // prismas colados nem exigir raio de canto.
        var caminho = SweepPath.FromPolyline(
            new[] { Point3D.Origin, new Point3D(50, 0, 0), new Point3D(120, 0, 0) },
            cornerRadius: 10);

        caminho.Error.Should().BeNull();
        caminho.Segments.Should().ContainSingle("os três pontos são a mesma reta");
        caminho.Segments[0].LineVector.Length.Should().BeApproximately(120, 1e-9);
    }

    [Fact]
    public void ReferenciaDeCima_GiraASecaoEmTornoDoCaminho()
    {
        // Seção 20 × 4 varrida ao longo de +X. Sem referência, o padrão do WorkPlane põe o lado
        // largo em +Y; pedindo "cima = +Y", ele vai para +Z. É o único controle de torção.
        var caminho = SweepPath.FromPolyline(new[] { Point3D.Origin, new Point3D(60, 0, 0) }, cornerRadius: 0);
        var kernel = new OpenCascadeGeometryKernel();

        var padrao = kernel.BuildFeatureMesh(new SweepFeature(SweepSection.Rectangle(20, 4), caminho));
        var girada = kernel.BuildFeatureMesh(new SweepFeature(SweepSection.Rectangle(20, 4), caminho, upReference: Vector3D.UnitY));

        Extensao(padrao, 1).Should().BeApproximately(20f, 0.01f, "o lado largo cai em y");
        Extensao(padrao, 2).Should().BeApproximately(4f, 0.01f);
        Extensao(girada, 1).Should().BeApproximately(4f, 0.01f, "com cima = +Y os dois lados trocam");
        Extensao(girada, 2).Should().BeApproximately(20f, 0.01f);

        static float Extensao(TriangleMesh m, int eixo)
        {
            var vals = m.Positions.Where((_, i) => i % 3 == eixo).ToList();
            return vals.Max() - vals.Min();
        }
    }

    [Fact]
    public void CaminhoMultiTrecho_Girado_MantemOVolumeEMudaDeLugar()
    {
        var caminho = SweepPath.FromPolyline(
            new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) },
            cornerRadius: 30);
        var tubo = new SweepFeature(SweepSection.Circle(5), caminho);
        var kernel = new OpenCascadeGeometryKernel();

        double original = MeshVolumeCalculator.ComputeVolume(kernel.BuildFeatureMesh(tubo));
        original.Should().BeGreaterThan(0);

        var girado = tubo.Transformed(Placement.RotationAbout(Point3D.Origin, Vector3D.UnitX, 90));
        var mesh = kernel.BuildFeatureMesh(girado);

        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(original, original * 0.01);

        // 90° em torno de X leva o braço que ia para +Y a apontar para +Z.
        var zs = mesh.Positions.Where((_, i) => i % 3 == 2).ToList();
        zs.Max().Should().BeApproximately(80f, 0.2f, "o segundo braço subiu para +Z");
    }

    [Fact]
    public void FimDoCaminhoMultiTrecho_EhOCantoDeEncaixeDoProximoTrecho()
    {
        var tubo = new SweepFeature(
            SweepSection.Circle(5),
            SweepPath.FromPolyline(
                new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) },
                cornerRadius: 30));

        tubo.EndCenter.X.Should().BeApproximately(100, 1e-6);
        tubo.EndCenter.Y.Should().BeApproximately(80, 1e-6);
        tubo.EndTangent.Y.Should().BeApproximately(1, 1e-6, "o caminho termina seguindo em +Y");
        tubo.GetVertices().Should().HaveCount(4, "três trechos têm quatro estações");
    }

    [Theory]
    // Raio grande demais: os dois cantos querem 60 de recuo de uma reta de 50.
    [InlineData(new double[] { 0, 0, 0, 100, 0, 0, 150, 0, 0, 150, 90, 0 }, 60.0, "não cabe")]
    // Reversão de 180°.
    [InlineData(new double[] { 0, 0, 0, 100, 0, 0, 20, 0, 0 }, 10.0, "180°")]
    // Polilinha com canto e raio zero: canto vivo não se constrói com varredura.
    [InlineData(new double[] { 0, 0, 0, 100, 0, 0, 100, 80, 0 }, 0.0, "canto vivo")]
    // Um ponto só (os dois coincidentes viram um na deduplicação).
    [InlineData(new double[] { 0, 0, 0, 0, 0, 0 }, 10.0, "dois pontos distintos")]
    public void CaminhoInvalido_ExplicaOPorque_EDevolveMalhaVazia(double[] coords, double raio, string trecho)
    {
        var pontos = new List<Point3D>();
        for (int i = 0; i < coords.Length; i += 3) pontos.Add(new Point3D(coords[i], coords[i + 1], coords[i + 2]));

        var caminho = SweepPath.FromPolyline(pontos, raio);

        caminho.Error.Should().Contain(trecho);
        caminho.Segments.Should().BeEmpty();

        var tubo = new SweepFeature(SweepSection.Circle(5), caminho);
        new OpenCascadeGeometryKernel().BuildFeatureMesh(tubo).VertexCount.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(14)]
    public void FuseAguentaCaminhoLongo_AreaNaoAcusaJuncaoPerdida(int cantos)
    {
        // O risco que sobrou da etapa 2: o fuse funciona com 2 ou 3 peças, mas pode degradar em
        // silêncio conforme o caminho cresce — 14 cantos são 29 sólidos fundidos. O volume não
        // denunciaria (um compound tem o mesmo volume); a área sim, porque cada junção não fundida
        // deixa DUAS tampas para trás. Este teste mapeia o teto prático.
        const double L = 40, R = 10, raioSecao = 4;

        // Escada no plano XY: todo canto é de 90° e toda aresta tem exatamente L.
        var pontos = new List<Point3D> { Point3D.Origin };
        for (int i = 0; i <= cantos; i++)
        {
            var anterior = pontos[^1];
            pontos.Add(i % 2 == 0
                ? new Point3D(anterior.X + L, anterior.Y, 0)
                : new Point3D(anterior.X, anterior.Y + L, 0));
        }

        var caminho = SweepPath.FromPolyline(pontos, R);
        caminho.Error.Should().BeNull();
        caminho.Segments.Should().HaveCount(2 * cantos + 1, "cada canto parte a reta em duas e vira um arco");

        var tubo = new SweepFeature(SweepSection.Circle(raioSecao), caminho);
        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(tubo);

        // Cada canto de 90° troca 2·R·tan(45°) de reta por um arco de R·π/2.
        double comprimento = (cantos + 1) * L - cantos * (2 * R - R * (Math.PI / 2));
        double areaSecao = MeshAreaCalculator.InscribedPolygonArea(raioSecao, Segs);
        double perimetro = MeshAreaCalculator.InscribedPolygonPerimeter(raioSecao, Segs);
        double areaEsperada = perimetro * comprimento + 2 * areaSecao;

        MeshVolumeCalculator.ComputeVolume(mesh)
            .Should().BeApproximately(areaSecao * comprimento, areaSecao * comprimento * 0.01);
        MeshAreaCalculator.ComputeArea(mesh)
            .Should().BeApproximately(areaEsperada, areaEsperada * 0.01,
                $"as {2 * cantos} junções têm que sumir, não só encostar");
    }

    [Fact]
    public void CaminhoFechado_MolduraQuadrada_NaoTemTampaNenhuma()
    {
        // A asserção que discrimina sozinha: um laço realmente fechado não tem ponta, então a área
        // é só a lateral. Se a emenda de volta ao começo não tivesse fundido, apareceriam DUAS
        // tampas (2 × 50 = 100 num total de ~9000), bem fora de 1%.
        const double L = 120, R = 25, raioSecao = 4;

        var caminho = SweepPath.FromPolyline(
            new[]
            {
                Point3D.Origin, new Point3D(L, 0, 0), new Point3D(L, L, 0), new Point3D(0, L, 0)
            },
            cornerRadius: R, closed: true);

        caminho.Error.Should().BeNull();
        caminho.IsClosed.Should().BeTrue();
        caminho.Segments.Should().HaveCount(8, "quatro cantos e quatro retas, sem ponta sobrando");

        var moldura = new SweepFeature(SweepSection.Circle(raioSecao), caminho);
        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(moldura);

        double comprimento = 4 * L - 4 * (2 * R - R * (Math.PI / 2));
        double areaSecao = MeshAreaCalculator.InscribedPolygonArea(raioSecao, Segs);
        double perimetro = MeshAreaCalculator.InscribedPolygonPerimeter(raioSecao, Segs);

        MeshVolumeCalculator.ComputeVolume(mesh)
            .Should().BeApproximately(areaSecao * comprimento, areaSecao * comprimento * 0.01);
        MeshAreaCalculator.ComputeArea(mesh)
            .Should().BeApproximately(perimetro * comprimento, perimetro * comprimento * 0.01,
                "um laço fechado é só lateral: nenhuma tampa");
    }

    [Fact]
    public void CaminhoDeVoltaInteira_FullCircle_FechaUmAnelSemTampa()
    {
        // Uma volta inteira é UM arco de 360°: não há canto, logo não há raio de canto, e o revolve
        // de 2π fecha o sólido sozinho. A área é a asserção que discrimina: um anel de verdade é só
        // lateral, e se a volta não tivesse fechado apareceriam duas tampas.
        const double raio = 40, lado = 6;
        var caminho = SweepPath.FullCircle(Point3D.Origin, Vector3D.UnitZ, raio);

        caminho.Error.Should().BeNull();
        caminho.IsClosed.Should().BeTrue();
        caminho.Segments.Should().ContainSingle(because: "uma volta inteira é um arco só");

        var anel = new SweepFeature(SweepSection.Rectangle(lado, lado), caminho);
        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(anel);

        // Pappus: o centroide da seção percorre a circunferência do próprio raio do caminho.
        double comprimento = 2 * Math.PI * raio;
        double volume = lado * lado * comprimento;
        double area = 4 * lado * comprimento;

        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(volume, volume * 0.01);
        MeshAreaCalculator.ComputeArea(mesh)
            .Should().BeApproximately(area, area * 0.01, "um anel é só lateral: nenhuma tampa");
    }

    [Fact]
    public void FullCircle_ComecaNaCircunferencia_ComATangentePerpendicularAoEixo()
    {
        var caminho = SweepPath.FullCircle(new Point3D(10, 20, 30), Vector3D.UnitZ, 40);

        caminho.Error.Should().BeNull();
        caminho.Start.DistanceTo(new Point3D(10, 20, 30)).Should().BeApproximately(40, 1e-9,
            "o ponto de partida fica na circunferência");
        caminho.StartTangent.DotProduct(Vector3D.UnitZ).Should().BeApproximately(0, 1e-9,
            "o caminho corre perpendicular ao eixo");
        caminho.StartTangent.Length.Should().BeApproximately(1, 1e-9);
        caminho.Segments[0].IsArc.Should().BeTrue();
        caminho.Segments[0].ArcAngle.Should().BeApproximately(2 * Math.PI, 1e-9);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 1.0)]        // raio zero
    [InlineData(-5.0, 0.0, 0.0, 1.0)]       // raio negativo
    [InlineData(40.0, 0.0, 0.0, 0.0)]       // eixo nulo
    public void FullCircle_Degenerado_ExplicaEDevolveMalhaVazia(double raio, double ax, double ay, double az)
    {
        // Geometria que falha devolve malha vazia e registra o motivo - nunca exceção.
        var caminho = SweepPath.FullCircle(Point3D.Origin, new Vector3D(ax, ay, az), raio);

        caminho.Error.Should().NotBeNull();
        caminho.Segments.Should().BeEmpty();
        new OpenCascadeGeometryKernel()
            .BuildFeatureMesh(new SweepFeature(SweepSection.Circle(4), caminho))
            .VertexCount.Should().Be(0);
    }

    [Fact]
    public void FullCircle_SecaoMaiorQueOAnel_ReportaProblema()
    {
        // A seção cruzaria o eixo: um ponto do lado de dentro acabaria em raio negativo e o sólido
        // dobraria para dentro de si mesmo. É o raio mínimo de curvatura de uma dobra real.
        var caminho = SweepPath.FullCircle(Point3D.Origin, Vector3D.UnitZ, radius: 20);
        caminho.Error.Should().BeNull("o caminho em si é válido; quem não cabe é a seção");

        var anel = new SweepFeature(SweepSection.Rectangle(100, 6), caminho);
        var kernel = new OpenCascadeGeometryKernel();

        kernel.BuildFeatureMesh(anel).VertexCount.Should().Be(0);
        kernel.BuildProblems.Should().ContainKey(anel.Id);
    }

    [Fact]
    public void CaminhoFechado_PontoRepetidoNoFim_EhAMesmaCoisa()
    {
        // Repetir o primeiro ponto no fim é a grafia natural de um contorno fechado; a aresta de
        // volta já é implícita, então o ponto repetido não pode virar um canto a mais.
        var pontos = new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 100, 0), new Point3D(0, 100, 0) };
        var semRepetir = SweepPath.FromPolyline(pontos, 20, closed: true);
        var repetindo = SweepPath.FromPolyline(new List<Point3D>(pontos) { Point3D.Origin }, 20, closed: true);

        repetindo.Error.Should().BeNull();
        repetindo.Segments.Should().HaveCount(semRepetir.Segments.Count);
        repetindo.Points.Should().HaveCount(4);
    }

    [Fact]
    public void CaminhoFechadoComTorcaoAcumulada_ERecusado()
    {
        // Transporte paralelo em volta de um laço 3D em geral NÃO volta ao referencial de partida
        // (holonomia não-nula): a última face chegaria girada e a emenda não coincidiria. Vale
        // mesmo para seção circular, porque o kernel varre um POLÍGONO, que não é simétrico a
        // qualquer ângulo. Este laço acumula 5,47°.
        var caminho = SweepPath.FromPolyline(
            new[]
            {
                Point3D.Origin, new Point3D(120, 0, 0), new Point3D(120, 100, 40), new Point3D(30, 140, 0)
            },
            cornerRadius: 20, closed: true);

        caminho.Error.Should().Contain("não é plano");
        caminho.Segments.Should().BeEmpty();
        new OpenCascadeGeometryKernel()
            .BuildFeatureMesh(new SweepFeature(SweepSection.Circle(4), caminho))
            .VertexCount.Should().Be(0);
    }

    [Fact]
    public void CaminhoFechadoForaDePlano_QueFecha_EhAceito()
    {
        // A regra NÃO é "laço fechado tem que ser plano": é que o referencial tem que voltar ao
        // lugar, e isso é MEDIDO. Alguns laços 3D simétricos têm holonomia múltipla de uma volta
        // inteira e fecham perfeitamente — este, por três arestas do mesmo tetraedro, é um deles.
        // Rejeitá-lo por ser 3D seria conservador à toa.
        const double R = 25, raioSecao = 5;
        var caminho = SweepPath.FromPolyline(
            new[]
            {
                Point3D.Origin, new Point3D(120, 0, 0), new Point3D(0, 120, 0), new Point3D(0, 0, 120)
            },
            cornerRadius: R, closed: true);

        caminho.Error.Should().BeNull();

        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(new SweepFeature(SweepSection.Circle(raioSecao), caminho));

        // Comprimento somado dos próprios trechos: num caminho de polilinha todo arco tem o raio de
        // canto, então isto não depende da conta de recuo (que os testes de caminho aberto já cobrem)
        // — o que está sob teste aqui é a emenda.
        double comprimento = caminho.Segments.Sum(s => s.IsArc ? caminho.CornerRadius * s.ArcAngle : s.LineVector.Length);
        double areaSecao = MeshAreaCalculator.InscribedPolygonArea(raioSecao, Segs);
        double perimetro = MeshAreaCalculator.InscribedPolygonPerimeter(raioSecao, Segs);

        MeshVolumeCalculator.ComputeVolume(mesh)
            .Should().BeApproximately(areaSecao * comprimento, areaSecao * comprimento * 0.01);
        MeshAreaCalculator.ComputeArea(mesh)
            .Should().BeApproximately(perimetro * comprimento, perimetro * comprimento * 0.01,
                "fecha de verdade, então não sobra tampa nenhuma");
    }

    [Fact]
    public void CaminhoFechado_ComMenosDeTresPontos_NaoFecha()
    {
        SweepPath.FromPolyline(new[] { Point3D.Origin, new Point3D(50, 0, 0) }, 10, closed: true)
            .Error.Should().Contain("três pontos");
    }

    [Fact]
    public void RaioDeCantoMenorQueASecao_NaoConstroi()
    {
        // A parede interna cruzaria o eixo do arco e o sólido dobraria para dentro de si mesmo.
        // O caminho em si é válido — quem barra é o kernel, que é quem conhece a seção.
        var caminho = SweepPath.FromPolyline(
            new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) },
            cornerRadius: 3);
        caminho.Error.Should().BeNull();

        var tubo = new SweepFeature(SweepSection.Circle(5), caminho);
        new OpenCascadeGeometryKernel().BuildFeatureMesh(tubo).VertexCount.Should().Be(0);
    }

    // --- Diagnóstico: por que uma feature não gerou geometria ---
    // Uma primitiva degenerada devolve malha VAZIA, não erro. Sem isto o sólido some da viewport e
    // a explicação morre dentro do kernel. O problema fica guardado com a mesma vida do cache de
    // formas, e não só da última chamada, porque o cache de malhas da cena guarda a malha vazia e
    // não chamaria o kernel de novo.

    [Fact]
    public void CaminhoInvalido_RegistraOProblemaComONomeDaFeature()
    {
        var caminho = SweepPath.FromPolyline(
            new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(150, 0, 0), new Point3D(150, 90, 0) },
            cornerRadius: 60);
        var tubo = new SweepFeature(SweepSection.Circle(5), caminho, name: "tubo_ruim");
        var kernel = new OpenCascadeGeometryKernel();

        kernel.BuildFeatureMesh(tubo).VertexCount.Should().Be(0);

        kernel.BuildProblems.Should().ContainKey(tubo.Id);
        kernel.BuildProblems[tubo.Id].Should().StartWith("tubo_ruim: ").And.Contain("não cabe");
    }

    [Fact]
    public void SecaoQueNaoCabeNoRaio_RegistraQualTrechoFalhou()
    {
        // Este não é barrado pelo caminho (que é válido), e sim pelo kernel, que é quem conhece a
        // seção — daí a mensagem citar o trecho.
        var caminho = SweepPath.FromPolyline(
            new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) },
            cornerRadius: 3);
        var tubo = new SweepFeature(SweepSection.Circle(5), caminho, name: "tubo_apertado");
        var kernel = new OpenCascadeGeometryKernel();

        kernel.BuildFeatureMesh(tubo);

        kernel.BuildProblems[tubo.Id].Should().Contain("tubo_apertado").And.Contain("raio de curvatura");
    }

    [Fact]
    public void FeatureQueConstroi_NaoDeixaProblemaPendurado()
    {
        var tubo = new SweepFeature(
            SweepSection.Circle(5),
            SweepPath.FromPolyline(new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) }, 30),
            name: "tubo_bom");
        var kernel = new OpenCascadeGeometryKernel();

        kernel.BuildFeatureMesh(tubo).VertexCount.Should().BeGreaterThan(0);
        kernel.BuildProblems.Should().BeEmpty();
    }

    [Fact]
    public void ConsertarAFeature_LimpaOProblemaAnterior()
    {
        // O cenário real: a IA (ou o usuário) escreve um raio que não cabe, vê a mensagem, corrige.
        // Se o problema ficasse pendurado, a barra de status mentiria para sempre.
        var tubo = new SweepFeature(
            SweepSection.Circle(5),
            SweepPath.FromPolyline(new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) }, 3),
            name: "tubo");
        var kernel = new OpenCascadeGeometryKernel();

        kernel.BuildFeatureMesh(tubo);
        kernel.BuildProblems.Should().ContainKey(tubo.Id);

        tubo.Path = SweepPath.FromPolyline(
            new[] { Point3D.Origin, new Point3D(100, 0, 0), new Point3D(100, 80, 0) }, 30);
        kernel.InvalidateShape(tubo.Id);
        kernel.BuildFeatureMesh(tubo).VertexCount.Should().BeGreaterThan(0);

        kernel.BuildProblems.Should().BeEmpty();
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
