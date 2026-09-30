using System;

namespace AvaloniaCadEditor.Domain.Mesh;

public class TriangleMesh
{
    public float[] Positions { get; }
    public float[] Normals { get; }
    public uint[] Indices { get; }
    
    // Arrays contendo pares de vértices (idx1, idx2) para desenhar linhas GL_LINES
    public uint[] FeatureEdges { get; }
    public uint[] InternalEdges { get; }
    public float[] FeatureNormals1 { get; }
    public float[] FeatureNormals2 { get; }

    public int TriangleCount => Indices.Length / 3;
    public int VertexCount => Positions.Length / 3;

    public TriangleMesh(float[] positions, float[] normals, uint[] indices, uint[]? featureEdges = null, uint[]? internalEdges = null, float[]? featureNormals1 = null, float[]? featureNormals2 = null)
    {
        Positions = positions ?? Array.Empty<float>();
        Normals = normals ?? Array.Empty<float>();
        Indices = indices ?? Array.Empty<uint>();
        FeatureEdges = featureEdges ?? Array.Empty<uint>();
        InternalEdges = internalEdges ?? Array.Empty<uint>();
        FeatureNormals1 = featureNormals1 ?? Array.Empty<float>();
        FeatureNormals2 = featureNormals2 ?? Array.Empty<float>();
    }

    public static TriangleMesh Empty => new(Array.Empty<float>(), Array.Empty<float>(), Array.Empty<uint>());

    /// <summary>
    /// Junta várias malhas numa só, deslocando índices e arestas pelo número de vértices das
    /// anteriores. Os arrays de saída são alocados uma vez, no tamanho final. Malhas sem vértices
    /// são ignoradas.
    /// </summary>
    /// <param name="includeFeatureNormals">Também concatena <see cref="FeatureNormals1"/>/<see cref="FeatureNormals2"/>.</param>
    public static TriangleMesh Combine(System.Collections.Generic.IReadOnlyList<TriangleMesh> meshes, bool includeFeatureNormals)
    {
        int positions = 0, normals = 0, indices = 0, featureEdges = 0, internalEdges = 0, featureNormals1 = 0, featureNormals2 = 0;
        foreach (var mesh in meshes)
        {
            if (mesh.VertexCount == 0) continue;
            positions += mesh.Positions.Length;
            normals += mesh.Normals.Length;
            indices += mesh.Indices.Length;
            featureEdges += mesh.FeatureEdges.Length;
            internalEdges += mesh.InternalEdges.Length;
            featureNormals1 += mesh.FeatureNormals1.Length;
            featureNormals2 += mesh.FeatureNormals2.Length;
        }

        var result = new TriangleMesh(
            new float[positions],
            new float[normals],
            new uint[indices],
            new uint[featureEdges],
            new uint[internalEdges],
            includeFeatureNormals ? new float[featureNormals1] : null,
            includeFeatureNormals ? new float[featureNormals2] : null);

        int p = 0, n = 0, i = 0, fe = 0, ie = 0, fn1 = 0, fn2 = 0;
        uint vertexOffset = 0;
        foreach (var mesh in meshes)
        {
            if (mesh.VertexCount == 0) continue;

            Array.Copy(mesh.Positions, 0, result.Positions, p, mesh.Positions.Length);
            p += mesh.Positions.Length;
            Array.Copy(mesh.Normals, 0, result.Normals, n, mesh.Normals.Length);
            n += mesh.Normals.Length;
            CopyOffset(mesh.Indices, result.Indices, ref i, vertexOffset);
            CopyOffset(mesh.FeatureEdges, result.FeatureEdges, ref fe, vertexOffset);
            CopyOffset(mesh.InternalEdges, result.InternalEdges, ref ie, vertexOffset);

            if (includeFeatureNormals)
            {
                Array.Copy(mesh.FeatureNormals1, 0, result.FeatureNormals1, fn1, mesh.FeatureNormals1.Length);
                fn1 += mesh.FeatureNormals1.Length;
                Array.Copy(mesh.FeatureNormals2, 0, result.FeatureNormals2, fn2, mesh.FeatureNormals2.Length);
                fn2 += mesh.FeatureNormals2.Length;
            }

            vertexOffset += (uint)mesh.VertexCount;
        }

        return result;
    }

    private static void CopyOffset(uint[] source, uint[] target, ref int position, uint offset)
    {
        for (int k = 0; k < source.Length; k++)
        {
            target[position + k] = source[k] + offset;
        }
        position += source.Length;
    }
}

