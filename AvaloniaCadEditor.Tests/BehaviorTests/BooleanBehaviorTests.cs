using System;
using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class BooleanBehaviorTests
{
    [Fact]
    public void BooleanCut_Dominio_DeveGerarMalhaValida()
    {
        // Arrange
        var circle1 = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var cylinder1 = new ExtrudeFeature(circle1, 30.0);

        var circle2 = new CircleProfile(new Point3D(15, 0, 0), 10.0);
        var cylinder2 = new ExtrudeFeature(circle2, 30.0);

        var booleanCut = new BooleanFeature("Cut Test", BooleanOperationType.Subtract, cylinder1, new[] { cylinder2 });

        var kernel = new OpenCascadeGeometryKernel();

        // Act
        var mesh = kernel.BuildFeatureMesh(booleanCut);

        // Assert
        mesh.Should().NotBeNull();
        mesh.VertexCount.Should().BeGreaterThan(0);
        mesh.TriangleCount.Should().BeGreaterThan(0);

        var baseMesh = kernel.BuildFeatureMesh(cylinder1);
        mesh.TriangleCount.Should().NotBe(baseMesh.TriangleCount);
    }

    [Fact]
    public void FluxoE2E_CriarDoisCirculos_Extrudar_BooleanCut_ViaAtalhosDeTeclado()
    {
        // Arrange
        var kernel = new OpenCascadeGeometryKernel();
        var mockEngine = new Mock<IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        mockSnap.Setup(s => s.GetSnappedPoint(It.IsAny<Sketch>(), It.IsAny<Point3D>(), It.IsAny<double>(), It.IsAny<AvaloniaCadEditor.Domain.Mesh.TriangleMesh>()))
                .Returns<Sketch, Point3D, double, AvaloniaCadEditor.Domain.Mesh.TriangleMesh>((s, p, t, m) => p);
        var profileService = new ProfileRecognitionService();
        var selectionService = new SelectionService();
        var vm = new CadViewModel(mockEngine.Object, mockSnap.Object, profileService, kernel, selectionService);

        // Act - Passo 1: Desenhar Círculo 1 (r=10, origem)
        vm.OnKeyDown("C");
        vm.OnPointerPressed(new Point3D(0, 0, 0));
        vm.OnKeyDown("D1"); vm.OnKeyDown("D0"); vm.OnKeyDown("Return");

        // Act - Passo 2: Extrudar Círculo 1 (h=30)
        vm.OnKeyDown("E");
        vm.OnKeyDown("D3"); vm.OnKeyDown("D0"); vm.OnKeyDown("Return");

        // Act - Passo 3: Desenhar Círculo 2 (r=10, offset x=15)
        vm.OnKeyDown("C");
        vm.OnPointerPressed(new Point3D(15, 0, 0));
        vm.OnKeyDown("D1"); vm.OnKeyDown("D0"); vm.OnKeyDown("Return");

        // Act - Passo 4: Extrudar Círculo 2 (h=30)
        vm.OnKeyDown("E");
        vm.OnKeyDown("D3"); vm.OnKeyDown("D0"); vm.OnKeyDown("Return");

        // Assert: Setup is correct
        var extrudes = vm.Sketch.Features.OfType<ExtrudeFeature>().ToList();
        extrudes.Should().HaveCount(2);

        // Act - Passo 5: Boolean Cut
        vm.SelectBooleanCutCommand.Execute(null);
        var booleanTool = (BooleanTool)vm.CurrentTool!;

        // Selecionar Base (Cilindro 1) via SelectionService (Option B)
        selectionService.SelectFeature(extrudes[0]);

        // Clicar no Cilindro 2 para usá-lo como ferramenta
        booleanTool.OnMouseDown(vm.Sketch, extrudes[1].Centroid);

        // Assert: Boolean Feature created
        var booleanFeatures = vm.Sketch.Features.OfType<BooleanFeature>().ToList();
        booleanFeatures.Should().ContainSingle();
        var cutFeature = booleanFeatures.Single();
        cutFeature.Operation.Should().Be(BooleanOperationType.Subtract);
        cutFeature.BaseFeature.Should().Be(extrudes[0]);
        cutFeature.ToolFeatures.Should().ContainSingle().Which.Should().Be(extrudes[1]);
    }

    [Fact]
    public void BooleanCut_BoundingBoxDoResultado_DeveSerReduzida()
    {
        // Arrange
        var circle1 = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var cylinder1 = new ExtrudeFeature(circle1, 30.0);

        var circle2 = new CircleProfile(new Point3D(15, 0, 0), 10.0);
        var cylinder2 = new ExtrudeFeature(circle2, 30.0);

        var booleanCut = new BooleanFeature("Cut Test", BooleanOperationType.Subtract, cylinder1, new[] { cylinder2 });
        var kernel = new OpenCascadeGeometryKernel();

        // Act
        var resultMesh = kernel.BuildFeatureMesh(booleanCut);
        var baseMesh = kernel.BuildFeatureMesh(cylinder1);

        // A Bounding Box em X do cilindro base era [-10, 10] (tamanho 20)
        // O cilindro 2 tem centro em x=15 e raio=10
        // A interseção de dois círculos de raio 10 separados por 15:
        // x^2 + y^2 = 100
        // (x-15)^2 + y^2 = 100  =>  x = 7.5
        // Portanto, a figura resultante (Cut) não passará de X = 7.5 nos "chifres" da meia-lua
        
        double minX = double.MaxValue;
        double maxX = double.MinValue;
        for (int i = 0; i < resultMesh.Positions.Length; i += 3)
        {
            if (resultMesh.Positions[i] < minX) minX = resultMesh.Positions[i];
            if (resultMesh.Positions[i] > maxX) maxX = resultMesh.Positions[i];
        }

        // Assert
        resultMesh.Positions.Length.Should().BeGreaterThan(0);
        
        minX.Should().BeApproximately(-10.0, 0.5);
        maxX.Should().BeApproximately(7.5, 0.5, "os vértices extremos do corte nos chifres ficam em X = 7.5");
    }
}
