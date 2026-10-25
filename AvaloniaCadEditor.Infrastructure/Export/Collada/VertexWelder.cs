using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Infrastructure.Export.Collada;

/// <summary>
/// Funde vértices na mesma posição. A tesselação do OCCT repete o vértice em cada face que o toca;
/// sem a solda, o programa de destino recebe cada face solta, sem aresta compartilhada com a vizinha,
/// e não consegue mesclar as coplanares.
/// </summary>
internal static class VertexWelder
{
    public static (float[] Positions, int[] Indices) Weld(TriangleMesh mesh)
    {
        var slotByPosition = new Dictionary<(float, float, float), int>(mesh.VertexCount);
        var positions = new List<float>(mesh.Positions.Length);
        var remap = new int[mesh.VertexCount];

        for (int v = 0; v < mesh.VertexCount; v++)
        {
            var key = (mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]);
            if (!slotByPosition.TryGetValue(key, out var slot))
            {
                slot = slotByPosition.Count;
                slotByPosition[key] = slot;
                positions.Add(key.Item1);
                positions.Add(key.Item2);
                positions.Add(key.Item3);
            }
            remap[v] = slot;
        }

        var indices = new List<int>(mesh.Indices.Length);
        for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
        {
            int a = remap[mesh.Indices[t]], b = remap[mesh.Indices[t + 1]], c = remap[mesh.Indices[t + 2]];
            // Dois cantos no mesmo ponto: o triângulo não tem área e o destino o rejeitaria (ou criaria aresta solta).
            if (a == b || b == c || a == c) continue;
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        return (positions.ToArray(), indices.ToArray());
    }
}
