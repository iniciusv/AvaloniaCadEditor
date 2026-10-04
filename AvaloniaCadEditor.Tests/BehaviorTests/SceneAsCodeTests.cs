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
    public void ConeFeature_DeveGerarMalhaNoKernel()
    {
        var cone = new ConeFeature(Point3D.Origin, new Vector3D(0, 0, 1), bottomRadius: 10, topRadius: 4, height: 30);
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(cone);
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
    public async Task Evaluator_CodigoInvalido_DeveRetornarErroSemExcecao()
    {
        var evaluator = new RoslynSceneEvaluator();
        var result = await evaluator.EvaluateAsync("isso não é código C# válido !!!");
        
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
