using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.UI.Controls.Rendering;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.Features;

public class WireframeGeometryTests
{
    private const int Stride = WireframeEdgeShader.FloatsPerVertex;
    private const float CreaseAngle = 30f;

    private static TriangleMesh Caixa() => new OpenCascadeGeometryKernel().BuildFeatureMesh(
        new BoxFeature(new Point3D(0, 0, 0), width: 40, length: 20, height: 30));

    private static List<(float[] A, float[] B)> CandidatasASilhueta(float[] vertices)
    {
        var result = new List<(float[], float[])>();
        for (int v = 0; v < vertices.Length; v += Stride * 2)
        {
            if (vertices[v + 13] < 0.5f) continue;
            result.Add((vertices[v..(v + 3)], vertices[(v + 3)..(v + 6)]));
        }
        return result;
    }

    [Fact]
    public void Caixa_SoTemQuinas_AsDiagonaisDasFacesPlanasNaoViramSilhueta()
    {
        var mesh = Caixa();

        var vertices = WireframeGeometry.BuildLineVertices(mesh, CreaseAngle);

        mesh.InternalEdges.Should().NotBeEmpty("as faces da caixa são trianguladas, com diagonais");
        vertices.Should().HaveCount(mesh.FeatureEdges.Length * Stride,
            "dois vértices por quina e nada mais: as mesmas quinas que o modo sombreado desenha");
        CandidatasASilhueta(vertices).Should().BeEmpty();
    }

    [Fact]
    public void Cilindro_AsArestasDaLateralSaoCandidatasASilhueta()
    {
        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(
            new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, 10, 20));

        var candidatas = CandidatasASilhueta(WireframeGeometry.BuildLineVertices(mesh, CreaseAngle));

        candidatas.Should().NotBeEmpty();
        candidatas.Should().OnlyContain(e =>
            Math.Abs(e.A[0] - e.B[0]) < 1e-3f && Math.Abs(e.A[1] - e.B[1]) < 1e-3f,
            "só as geratrizes (verticais) da lateral; as diagonais das tampas ficam de fora");
    }

    [Fact]
    public void MalhaSemArestasPreCalculadas_ComoUmSTL_GanhaContorno()
    {
        // Mesmos triângulos da caixa, com vértices duplicados entre faces como num STL, mas sem
        // nenhuma aresta classificada: é assim que uma ImportedMeshFeature chega ao render.
        var caixa = Caixa();
        var semArestas = new TriangleMesh(caixa.Positions, caixa.Normals, caixa.Indices);

        var vertices = WireframeGeometry.BuildLineVertices(semArestas, CreaseAngle);

        semArestas.FeatureEdges.Should().BeEmpty();
        vertices.Should().HaveCount(caixa.FeatureEdges.Length * Stride, "as quinas saem do próprio detector");
    }

    [Fact]
    public void CadaVerticeApontaParaOOutroExtremoDaAresta()
    {
        var triangulo = new TriangleMesh(
            new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f },
            new uint[] { 0, 1, 2 });

        var vertices = WireframeGeometry.BuildLineVertices(triangulo, CreaseAngle);

        vertices.Should().HaveCount(3 * 2 * Stride, "três arestas de borda, dois vértices cada");
        for (int v = 0; v < vertices.Length; v += Stride * 2)
        {
            vertices[v..(v + 3)].Should().Equal(vertices[(v + Stride + 3)..(v + Stride + 6)], "o início aponta para o fim");
            vertices[(v + 3)..(v + 6)].Should().Equal(vertices[(v + Stride)..(v + Stride + 3)], "o fim aponta para o início");
            vertices[v + 12].Should().Be(0f);
            vertices[v + Stride + 12].Should().Be(1f);
        }
    }
}
