using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Push/pull numa face genérica (FaceProfile, extraída da malha de uma peça que não é um
/// BoxFeature direto): se a face for um retângulo, a extrusão deve sair como BoxFeature, não prisma.
/// </summary>
public class FaceExtrusionCodeGenerationTests
{
    // Face lateral (normal -Y, em y=18) da placa de 45 x 6 do suporte do NEMA17.
    private static readonly WorkPlane FacePlane =
        new(new Point3D(19.45, 18, 16), new Vector3D(0, -1, 0), new Vector3D(1, 0, 0));

    [Fact]
    public void FaceRetangular_ComVerticeColinearNaAresta_DeveGerarBoxFeature()
    {
        var a = new Point3D(19.45, 18, 16);
        var m = new Point3D(40, 18, 16); // vértice intermediário colinear na aresta A-B
        var b = new Point3D(64.45, 18, 16);
        var c = new Point3D(64.45, 18, 22);
        var d = new Point3D(19.45, 18, 22);
        var face = FaceFrom(new[] { a, m, b, c, d }, new uint[] { 0, 1, 4, 1, 2, 3, 1, 3, 4 });

        var code = FeatureCodeGenerator.GenerateExtrusionCode(face, height: 8, new Sketch());

        code.Should().NotContain("PrismFeature");
        code.Should().Contain("new BoxFeature(baseCorner: new Point3D(19.45, 10, 16), width: 45, length: 8, height: 6)");
    }

    [Fact]
    public void FaceTrapezoidal_DeveContinuarGerandoPrisma()
    {
        var a = new Point3D(19.45, 18, 16);
        var b = new Point3D(64.45, 18, 16);
        var c = new Point3D(54.45, 18, 22);
        var d = new Point3D(29.45, 18, 22);
        var face = FaceFrom(new[] { a, b, c, d }, new uint[] { 0, 1, 2, 0, 2, 3 });

        var code = FeatureCodeGenerator.GenerateExtrusionCode(face, height: 8, new Sketch());

        code.Should().Contain("PrismFeature");
        code.Should().NotContain("BoxFeature");
    }

    private static FaceProfile FaceFrom(Point3D[] points, uint[] indices)
    {
        var n = FacePlane.Normal;
        var positions = points.SelectMany(p => new[] { (float)p.X, (float)p.Y, (float)p.Z }).ToArray();
        var normals = points.SelectMany(_ => new[] { (float)n.X, (float)n.Y, (float)n.Z }).ToArray();
        var mesh = new TriangleMesh(positions, normals, indices);
        var owner = new BoxFeature(new Point3D(0, 0, 0), 1, 1, 1) { Name = "dono" };
        return new FaceProfile(owner, FacePlane, mesh);
    }
}
