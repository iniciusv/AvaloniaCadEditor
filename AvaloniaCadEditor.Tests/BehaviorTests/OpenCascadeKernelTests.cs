using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
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
        var extrude = new ExtrudeFeature(rect, depth: 30.0);
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
        var extrude = new ExtrudeFeature(circle, depth: 15.0);
        var kernel = new OpenCascadeGeometryKernel();

        // Act
        var mesh = kernel.BuildFeatureMesh(extrude);

        // Assert
        mesh.Should().NotBeNull();
        mesh.VertexCount.Should().BeGreaterThan(0);
        mesh.TriangleCount.Should().BeGreaterThan(12); // Cylinder tessellates into many triangles
    }
}
