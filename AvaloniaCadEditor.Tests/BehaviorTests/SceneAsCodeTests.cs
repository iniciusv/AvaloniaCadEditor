using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Infrastructure.Scripting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class SceneAsCodeTests
{
    [Fact]
    public void CylinderFeature_DeveGerarMalhaNoKernel()
    {
        var cyl = new CylinderFeature(Point3D.Origin, new Vector3D(0, 0, 1), 10, 30);
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(cyl);
        mesh.TriangleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BoxFeature_DeveGerarMalhaNoKernel()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 20, 30);
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(box);
        mesh.TriangleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RoundTrip_CodigoGerado_DeveReconstruirModeloEquivalente()
    {
        // 1. Cria modelo via domínio
        var circle = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var extrude = new ExtrudeFeature(circle, 30.0);
        var sketch = new Sketch();
        sketch.AddFeature(extrude);

        // 2. Gera código C#
        var generator = new CsCodeGenerator();
        var code = generator.GenerateCode(sketch);

        // 3. Avalia o código
        var evaluator = new RoslynSceneEvaluator();
        var result = await evaluator.EvaluateAsync(code);

        // 4. Assert: equivalência geométrica
        System.Console.WriteLine($"Erro do Evaluator: {result.ErrorMessage}");
        result.Success.Should().BeTrue($"O evaluator deveria ter compilado o código gerado. Erro: {result.ErrorMessage}");
        result.Features.Should().HaveCount(2); // WorkPlane (XY base) and the Cylinder
        result.Features[1].Should().BeOfType<CylinderFeature>();

        var kernel = new OpenCascadeGeometryKernel();
        var originalMesh = kernel.BuildFeatureMesh(extrude);
        var rebuiltMesh = kernel.BuildFeatureMesh(result.Features[1]);

        var origBounds = MeshVolumeEstimator.GetBoundsX(originalMesh);
        var newBounds = MeshVolumeEstimator.GetBoundsX(rebuiltMesh);
        
        origBounds.Min.Should().BeApproximately(newBounds.Min, 0.5f);
        origBounds.Max.Should().BeApproximately(newBounds.Max, 0.5f);
    }

    [Fact]
    public async Task Evaluator_CodigoInvalido_DeveRetornarErroSemExcecao()
    {
        var evaluator = new RoslynSceneEvaluator();
        var result = await evaluator.EvaluateAsync("isso não é código C# válido !!!");
        
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RoundTrip_CenarioComplexoComBoolean_DeveReconstruirModeloEquivalente()
    {
        // 1. Cria modelo complexo via domínio (dois cilindros e um boolean cut)
        var circle1 = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var cylinder1 = new ExtrudeFeature(circle1, 30.0);

        var circle2 = new CircleProfile(new Point3D(15, 0, 0), 10.0);
        var cylinder2 = new ExtrudeFeature(circle2, 30.0);

        var booleanCut = new BooleanFeature("Cut Test", BooleanOperationType.Subtract, cylinder1, new[] { cylinder2 });

        var sketch = new Sketch();
        sketch.AddFeature(cylinder1);
        sketch.AddFeature(cylinder2);
        sketch.AddFeature(booleanCut);

        // 2. Gera código C# do cenário completo
        var generator = new CsCodeGenerator();
        var code = generator.GenerateCode(sketch);

        // 3. Avalia o código C#
        var evaluator = new RoslynSceneEvaluator();
        var result = await evaluator.EvaluateAsync(code);

        // 4. Assert
        System.Console.WriteLine($"Erro do Evaluator: {result.ErrorMessage}");
        result.Success.Should().BeTrue($"O evaluator deveria ter compilado o código gerado. Erro: {result.ErrorMessage}");
        
        // Espera-se os seguintes recursos injetados no Sketch:
        // - WorkPlane (XY base criado pelo gerador/sketch padrão)
        // - cylinder_1 (CylinderFeature correspondente ao ExtrudeFeature 1)
        // - cylinder_2 (CylinderFeature correspondente ao ExtrudeFeature 2)
        // - boolOp_1 (BooleanFeature)
        result.Features.Should().HaveCount(4);
        
        var generatedBooleanOp = result.Features[3] as BooleanFeature;
        generatedBooleanOp.Should().NotBeNull();
        generatedBooleanOp!.Operation.Should().Be(BooleanOperationType.Subtract);
        generatedBooleanOp.BaseFeature.Should().BeOfType<CylinderFeature>();

        // Reconstrução geométrica para validar as Bounding Boxes (equivalência do shape matemático C# com C++)
        var kernel = new OpenCascadeGeometryKernel();
        var originalMesh = kernel.BuildFeatureMesh(booleanCut);
        var rebuiltMesh = kernel.BuildFeatureMesh(generatedBooleanOp);

        var origBounds = MeshVolumeEstimator.GetBoundsX(originalMesh);
        var newBounds = MeshVolumeEstimator.GetBoundsX(rebuiltMesh);
        
        origBounds.Min.Should().BeApproximately(newBounds.Min, 0.5f);
        origBounds.Max.Should().BeApproximately(newBounds.Max, 0.5f);
    }
}
