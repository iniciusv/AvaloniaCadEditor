using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

public static class MeshTransform
{
    /// <summary>
    /// Nova malha com posições transformadas e normais rotacionadas. Índices e arestas são
    /// compartilhados com a original (não mudam com a transformação).
    /// </summary>
    public static TriangleMesh Transform(this TriangleMesh mesh, Placement placement)
    {
        if (placement.IsIdentity || mesh.VertexCount == 0)
        {
            return mesh;
        }

        return new TriangleMesh(
            TransformArray(mesh.Positions, placement, isPoint: true),
            TransformArray(mesh.Normals, placement, isPoint: false),
            mesh.Indices,
            mesh.FeatureEdges,
            mesh.InternalEdges,
            TransformArray(mesh.FeatureNormals1, placement, isPoint: false),
            TransformArray(mesh.FeatureNormals2, placement, isPoint: false));
    }

    private static float[] TransformArray(float[] source, Placement placement, bool isPoint)
    {
        var result = (float[])source.Clone();
        for (int i = 0; i + 2 < result.Length; i += 3)
        {
            placement.ApplyToFloats(result, i, isPoint);
        }
        return result;
    }
}
