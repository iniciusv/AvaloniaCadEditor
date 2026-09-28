using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Testes do <see cref="BaseProfileCutsAlgorithm"/>: deduz a peça como um projetista modelaria —
/// um perfil base extrudado no eixo em que ele se repete, menos alguns cortes — em vez de tentar
/// "varrer" cada eixo com múltiplas camadas independentes.
/// </summary>
public class BaseProfileCutsTests
{
    private static List<Point3D> HexagonoEm(double centerX, double centerY, double z, double circunraio)
    {
        var pontos = new List<Point3D>();
        for (int i = 0; i < 6; i++)
        {
            double angulo = i * Math.PI / 3.0;
            pontos.Add(new Point3D(centerX + circunraio * Math.Cos(angulo), centerY + circunraio * Math.Sin(angulo), z));
        }
        return pontos;
    }

    [Fact]
    public async Task BaseComBolsaoHexagonalECilindroPassante_ReconstroiComTresSolidos()
    {
        // A peça descrita pelo usuário: um perfil base que se repete ao longo de um eixo (aqui,
        // um "L" — mais largo na base, mais estreito no topo — extrudado em Y), menos um bolsão
        // hexagonal de profundidade parcial e um cilindro passante, ambos ao longo de Z, dentro
        // da parte mais larga do L. Como a largura em X muda com Z, só Y (não X, nem Z) tem o
        // MESMO contorno externo nas duas pontas — é o único eixo onde o perfil base se repete.
        var kernel = new OpenCascadeGeometryKernel();

        var baseSlab = new BoxFeature(new Point3D(0, 0, 0), width: 10, length: 30, height: 4);
        var upright = new BoxFeature(new Point3D(0, 0, 4), width: 4, length: 30, height: 12);
        var lShape = BooleanFeature.CreateUnion("Perfil L", baseSlab, upright);

        var hexProfile = new PolygonProfile(HexagonoEm(centerX: 7, centerY: 15, z: 2, circunraio: 2.5));
        var hexCut = new ExtrudeFeature(hexProfile, height: 2.5, direction: new Vector3D(0, 0, 2.5)); // Z=2 até 4,5 (passa 0,5 da prateleira)

        var cylCut = new CylinderFeature(new Point3D(7, 15, -1), new Vector3D(0, 0, 1), radius: 1.0, height: 6);

        var pecaDeTeste = BooleanFeature.CreateDifference("Peça de teste", lShape, hexCut, cylCut);
        var mesh = kernel.BuildFeatureMesh(pecaDeTeste);
        double expectedVolume = MeshVolumeCalculator.ComputeVolume(mesh);

        var algorithms = new List<IReconstructionAlgorithm>
        {
            new BaseFaceExtrusionAlgorithm(),
            new BaseProfileCutsAlgorithm()
        };
        var service = new ShapeReconstructionService(algorithms, kernel);

        var root = await service.ReconstructMeshAsync(mesh);

        root.Should().NotBeNull();
        root.Should().BeOfType<BooleanFeature>();
        var boolean = (BooleanFeature)root!;

        boolean.BaseFeature.Should().BeOfType<ExtrudeFeature>();
        var baseExtrude = (ExtrudeFeature)boolean.BaseFeature;

        // A base tem que ser extrudada em Y — nem em Z (que tem o degrau do L) nem em X (que
        // também muda de largura, já que o degrau em Z existe só para X < 4).
        Math.Abs(baseExtrude.Direction.Z).Should().BeLessThan(0.01);
        Math.Abs(baseExtrude.Direction.X).Should().BeLessThan(0.01);
        Math.Abs(baseExtrude.Direction.Y).Should().BeGreaterThan(0.01);

        boolean.ToolFeatures.Should().HaveCount(2, "exatamente o hexágono e o cilindro, sem repetição nem sobra");
        boolean.ToolFeatures.OfType<CylinderFeature>().Should().ContainSingle();
        boolean.ToolFeatures.OfType<ExtrudeFeature>()
            .Where(ext => ext.Profile is PolygonProfile poly && poly.Vertices.Count == 6)
            .Should().ContainSingle();

        var cylinder = (CylinderFeature)boolean.ToolFeatures.Single(f => f is CylinderFeature);
        cylinder.Radius.Should().BeApproximately(1.0, 0.1);
        Math.Abs(cylinder.GlobalDirection.Z).Should().BeGreaterThan(0.99, "o cilindro real é ao longo de Z");

        var resultMesh = kernel.BuildFeatureMesh(root!);
        double resultVolume = MeshVolumeCalculator.ComputeVolume(resultMesh);
        resultVolume.Should().BeApproximately(expectedVolume, expectedVolume * 0.02);
    }

    [Fact]
    public async Task PlacaComFurosVerticais_ReconstroiComBaseEDoisCilindros()
    {
        // Regressão: o caso simples que a extrusão da face inferior já resolvia continua sendo
        // reconstruído corretamente, agora pelo caminho do perfil base + cortes.
        var kernel = new OpenCascadeGeometryKernel();
        var plate = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 5);
        var hole1 = new CylinderFeature(new Point3D(5, 5, -1), new Vector3D(0, 0, 1), radius: 2, height: 7, name: "Furo 1");
        var hole2 = new CylinderFeature(new Point3D(15, 15, -1), new Vector3D(0, 0, 1), radius: 2, height: 7, name: "Furo 2");
        var plateWithHoles = BooleanFeature.CreateDifference("Placa com furos", plate, hole1, hole2);
        var mesh = kernel.BuildFeatureMesh(plateWithHoles);

        double expectedVolume = 20 * 20 * 5 - 2 * Math.PI * 2 * 2 * 5;

        var algorithms = new List<IReconstructionAlgorithm>
        {
            new BaseFaceExtrusionAlgorithm(),
            new BaseProfileCutsAlgorithm()
        };
        var service = new ShapeReconstructionService(algorithms, kernel);

        var root = await service.ReconstructMeshAsync(mesh);

        root.Should().NotBeNull();
        var resultMesh = kernel.BuildFeatureMesh(root!);
        double resultVolume = MeshVolumeCalculator.ComputeVolume(resultMesh);

        resultVolume.Should().BeApproximately(expectedVolume, expectedVolume * 0.02);
    }

    [Fact]
    public async Task BlocoComFuroTransversalDescentrado_DetectaUmUnicoCorteNoEixoCorreto()
    {
        // Bloco alto com um furo cilíndrico passante ao longo de X, deslocado da meia-altura em
        // Z. Verifica que o algoritmo não inventa um segundo corte "fantasma" ao varrer um eixo
        // perpendicular ao furo real (uma superfície cilíndrica cortada de raspão por outro eixo
        // aparece como uma fenda que só bate a assinatura no meio da camada — a validação por
        // reamostragem tem que rejeitar isso).
        var kernel = new OpenCascadeGeometryKernel();
        var block = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 20);
        var hole = new CylinderFeature(new Point3D(-2, 10, 4), new Vector3D(1, 0, 0), radius: 2, height: 24, name: "Furo Transversal");
        var blockWithHole = BooleanFeature.CreateDifference("Bloco com furo", block, hole);
        var mesh = kernel.BuildFeatureMesh(blockWithHole);

        double expectedVolume = 20 * 20 * 20 - Math.PI * 2 * 2 * 20;

        var algorithms = new List<IReconstructionAlgorithm>
        {
            new BaseFaceExtrusionAlgorithm(),
            new BaseProfileCutsAlgorithm()
        };
        var service = new ShapeReconstructionService(algorithms, kernel);

        var root = await service.ReconstructMeshAsync(mesh);

        root.Should().NotBeNull();
        root.Should().BeOfType<BooleanFeature>();
        var boolean = (BooleanFeature)root!;

        boolean.ToolFeatures.Should().HaveCount(1, "só existe UM furo real; nenhum corte fantasma em outro eixo");
        boolean.ToolFeatures[0].Should().BeOfType<CylinderFeature>();
        var cylinder = (CylinderFeature)boolean.ToolFeatures[0];
        Math.Abs(cylinder.GlobalDirection.X).Should().BeGreaterThan(0.99, "o furo real é ao longo de X");

        var resultMesh = kernel.BuildFeatureMesh(root!);
        double resultVolume = MeshVolumeCalculator.ComputeVolume(resultMesh);
        resultVolume.Should().BeApproximately(expectedVolume, expectedVolume * 0.02);
    }
}
