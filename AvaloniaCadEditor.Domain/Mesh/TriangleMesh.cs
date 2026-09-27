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
}

