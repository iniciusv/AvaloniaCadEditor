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
        result.Features.Should().HaveCount(1);
        result.Features[0].Should().BeOfType<CylinderFeature>();

        var kernel = new OpenCascadeGeometryKernel();
        var originalMesh = kernel.BuildFeatureMesh(extrude);
        var rebuiltMesh = kernel.BuildFeatureMesh(result.Features[0]);

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
}
