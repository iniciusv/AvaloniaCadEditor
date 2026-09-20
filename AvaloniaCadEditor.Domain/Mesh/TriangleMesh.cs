using System;

namespace AvaloniaCadEditor.Domain.Mesh;

public class TriangleMesh
{
    public float[] Positions { get; }
    public float[] Normals { get; }
    public uint[] Indices { get; }

    public int TriangleCount => Indices.Length / 3;
    public int VertexCount => Positions.Length / 3;

    public TriangleMesh(float[] positions, float[] normals, uint[] indices)
    {
        Positions = positions ?? Array.Empty<float>();
        Normals = normals ?? Array.Empty<float>();
        Indices = indices ?? Array.Empty<uint>();
    }

    public static TriangleMesh Empty => new(Array.Empty<float>(), Array.Empty<float>(), Array.Empty<uint>());
}
