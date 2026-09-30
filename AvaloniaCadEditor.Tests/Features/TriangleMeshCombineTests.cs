using AvaloniaCadEditor.Domain.Mesh;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.Features;

public class TriangleMeshCombineTests
{
    private static TriangleMesh Triangle(float x) => new(
        new[] { x, 0f, 0f, x + 1, 0f, 0f, x, 1f, 0f },
        new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f },
        new uint[] { 0, 1, 2 },
        featureEdges: new uint[] { 0, 1 },
        internalEdges: new uint[] { 1, 2 },
        featureNormals1: new[] { 0f, 0f, 1f },
        featureNormals2: new[] { 0f, 1f, 0f });

    [Fact]
    public void Combine_DeslocaIndicesEArestasPelosVerticesAnteriores()
    {
        var combined = TriangleMesh.Combine(new[] { Triangle(0), TriangleMesh.Empty, Triangle(5) }, includeFeatureNormals: true);

        combined.VertexCount.Should().Be(6);
        combined.Positions.Should().HaveCount(18);
        combined.Positions[9].Should().Be(5f);
        combined.Indices.Should().Equal(0u, 1u, 2u, 3u, 4u, 5u);
        combined.FeatureEdges.Should().Equal(0u, 1u, 3u, 4u);
        combined.InternalEdges.Should().Equal(1u, 2u, 4u, 5u);
        combined.FeatureNormals1.Should().HaveCount(6);
        combined.FeatureNormals2.Should().HaveCount(6);
    }

    [Fact]
    public void Combine_SemNormaisDeAresta_DeixaEssesArraysVazios()
    {
        var combined = TriangleMesh.Combine(new[] { Triangle(0), Triangle(5) }, includeFeatureNormals: false);

        combined.Indices.Should().HaveCount(6);
        combined.FeatureNormals1.Should().BeEmpty();
        combined.FeatureNormals2.Should().BeEmpty();
    }
}
