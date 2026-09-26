using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using System.Linq;

namespace AvaloniaCadEditor.Tests.Features;

public class FilletFeatureTests
{
    [Fact]
    public void FilletFeature_Should_HideBaseFeature()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);
        box.IsVisible.Should().BeTrue();

        var fillet = new FilletFeature("Fillet1", box, 2.0);
        box.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void BoxFeature_Should_ReturnCorrectEdgeGeometry()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);
        var edgeGeometry = box.GetEdgeGeometry(EdgeType.TopFront);
        
        // v4 to v5
        edgeGeometry.Start.Should().Be(new Point3D(0, 0, 10));
        edgeGeometry.End.Should().Be(new Point3D(10, 0, 10));
        
        // Top normal
        edgeGeometry.Normal1.Should().Be(new Vector3D(0, 0, 1));
        // Front normal
        edgeGeometry.Normal2.Should().Be(new Vector3D(0, -1, 0));
    }

    [Fact]
    public void BoxFeature_Should_ReturnCorrectEdgesForFace()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);
        var edges = box.GetEdgesForFace(FaceType.Top);
        
        edges.Should().Contain(EdgeType.TopFront);
        edges.Should().Contain(EdgeType.TopBack);
        edges.Should().Contain(EdgeType.TopLeft);
        edges.Should().Contain(EdgeType.TopRight);
        edges.Should().HaveCount(4);
    }

    [Fact]
    public void BoxFeature_Should_ReturnAllEdges()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);
        var edges = box.GetAllEdges();
        
        edges.Should().HaveCount(12);
        edges.Should().Contain(EdgeType.TopFront);
        edges.Should().Contain(EdgeType.BottomBack);
        edges.Should().Contain(EdgeType.BackRight);
    }

    [Fact]
    public void FilletFeature_Should_StoreModeAndApplyAllEdges()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);
        var fillet = new FilletFeature("Fillet1", box, 2.0)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = true
        };

        fillet.Mode.Should().Be(FilletMode.Bevel);
        fillet.ApplyToAllEdges.Should().BeTrue();
    }
}
