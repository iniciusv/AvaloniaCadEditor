using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Tests.Export;

internal static class ExportTestMeshes
{
    /// <summary>
    /// Cubo como a tesselação do OCCT entrega: 4 vértices próprios por face (24 no total), triângulos
    /// anti-horários vistos de fora.
    /// </summary>
    public static TriangleMesh Cube(float size = 10)
    {
        float s = size;
        float[][] faces =
        [
            [0, 0, 0, 0, s, 0, s, s, 0, s, 0, 0], // z = 0, normal -Z
            [0, 0, s, s, 0, s, s, s, s, 0, s, s], // z = s, normal +Z
            [0, 0, 0, s, 0, 0, s, 0, s, 0, 0, s], // y = 0, normal -Y
            [0, s, 0, 0, s, s, s, s, s, s, s, 0], // y = s, normal +Y
            [0, 0, 0, 0, 0, s, 0, s, s, 0, s, 0], // x = 0, normal -X
            [s, 0, 0, s, s, 0, s, s, s, s, 0, s], // x = s, normal +X
        ];

        var positions = new List<float>();
        var indices = new List<uint>();
        foreach (var face in faces)
        {
            uint first = (uint)(positions.Count / 3);
            positions.AddRange(face);
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }

        return new TriangleMesh(positions.ToArray(), new float[positions.Count], indices.ToArray());
    }

    /// <summary>Volume com sinal (teorema da divergência): positivo para malha fechada com normais para fora.</summary>
    public static double Volume(IReadOnlyList<float> positions, IReadOnlyList<int> indices)
    {
        double volume = 0;
        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            int a = indices[t] * 3, b = indices[t + 1] * 3, c = indices[t + 2] * 3;
            volume += positions[a] * (positions[b + 1] * positions[c + 2] - positions[b + 2] * positions[c + 1])
                    - positions[a + 1] * (positions[b] * positions[c + 2] - positions[b + 2] * positions[c])
                    + positions[a + 2] * (positions[b] * positions[c + 1] - positions[b + 1] * positions[c]);
        }
        return volume / 6;
    }

    public static double Volume(TriangleMesh mesh) =>
        Volume(mesh.Positions, Array.ConvertAll(mesh.Indices, i => (int)i));
}
