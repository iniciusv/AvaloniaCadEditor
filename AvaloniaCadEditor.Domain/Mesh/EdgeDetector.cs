using System;
using System.Collections.Generic;
using System.Numerics;

namespace AvaloniaCadEditor.Domain.Mesh;

public static class EdgeDetector
{
    private struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly uint V1;
        public readonly uint V2;

        public EdgeKey(uint v1, uint v2)
        {
            if (v1 < v2)
            {
                V1 = v1;
                V2 = v2;
            }
            else
            {
                V1 = v2;
                V2 = v1;
            }
        }

        public bool Equals(EdgeKey other) => V1 == other.V1 && V2 == other.V2;
        public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(V1, V2);
    }

    public static (uint[] featureEdges, uint[] internalEdges, Vector3[] featureNormals1, Vector3[] featureNormals2, bool[] featureConvex) ClassifyEdges(
        float[] positions, uint[] indices, float creaseAngleDegrees = 30.0f)
    {
        if (indices.Length == 0)
            return (Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<bool>());

        // 1. Weld coincident vertices to find true topological edges
        uint[] remappedIndices = new uint[indices.Length];
        var posMap = new Dictionary<Vector3, uint>();
        uint[] vertexRemap = new uint[positions.Length / 3];

        for (int i = 0; i < positions.Length / 3; i++)
        {
            var p = new Vector3(
                (float)Math.Round(positions[i * 3], 4),
                (float)Math.Round(positions[i * 3 + 1], 4),
                (float)Math.Round(positions[i * 3 + 2], 4));

            if (!posMap.TryGetValue(p, out uint mappedIdx))
            {
                mappedIdx = (uint)i;
                posMap[p] = mappedIdx;
            }
            vertexRemap[i] = mappedIdx;
        }

        for (int i = 0; i < indices.Length; i++)
        {
            remappedIndices[i] = vertexRemap[indices[i]];
        }

        // 2. Calculate Face Normals
        int triCount = indices.Length / 3;
        var faceNormals = new Vector3[triCount];

        for (int i = 0; i < triCount; i++)
        {
            uint i0 = indices[i * 3 + 0];
            uint i1 = indices[i * 3 + 1];
            uint i2 = indices[i * 3 + 2];

            var p0 = new Vector3(positions[i0 * 3], positions[i0 * 3 + 1], positions[i0 * 3 + 2]);
            var p1 = new Vector3(positions[i1 * 3], positions[i1 * 3 + 1], positions[i1 * 3 + 2]);
            var p2 = new Vector3(positions[i2 * 3], positions[i2 * 3 + 1], positions[i2 * 3 + 2]);

            var normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
            faceNormals[i] = normal;
        }

        // 3. Build Edge to Faces map using REMAPPED indices
        var edgeToFaces = new Dictionary<EdgeKey, List<int>>();
        
        for (int i = 0; i < triCount; i++)
        {
            uint i0 = remappedIndices[i * 3 + 0];
            uint i1 = remappedIndices[i * 3 + 1];
            uint i2 = remappedIndices[i * 3 + 2];

            AddFaceToEdge(edgeToFaces, i0, i1, i);
            AddFaceToEdge(edgeToFaces, i1, i2, i);
            AddFaceToEdge(edgeToFaces, i2, i0, i);
        }

        // 3. Classify edges based on Crease Angle
        var featureEdges = new List<uint>();
        var internalEdges = new List<uint>();
        var featureNormals1 = new List<Vector3>();
        var featureNormals2 = new List<Vector3>();
        var featureConvex = new List<bool>();

        float thresholdCos = (float)Math.Cos(creaseAngleDegrees * Math.PI / 180.0);

        foreach (var kvp in edgeToFaces)
        {
            var edge = kvp.Key;
            var faces = kvp.Value;

            if (faces.Count == 1)
            {
                // Boundary edge (hole in mesh)
                featureEdges.Add(edge.V1);
                featureEdges.Add(edge.V2);
                featureNormals1.Add(faceNormals[faces[0]]);
                featureNormals2.Add(faceNormals[faces[0]]);
                featureConvex.Add(true);
            }
            else if (faces.Count == 2)
            {
                var n1 = faceNormals[faces[0]];
                var n2 = faceNormals[faces[1]];

                float dot = Vector3.Dot(n1, n2);
                // Clamp to prevent floating point inaccuracies
                dot = Math.Clamp(dot, -1f, 1f);

                if (dot < thresholdCos)
                {
                    // Angle is larger than threshold, so it's a sharp edge
                    featureEdges.Add(edge.V1);
                    featureEdges.Add(edge.V2);
                    featureNormals1.Add(n1);
                    featureNormals2.Add(n2);
                    featureConvex.Add(IsConvexEdge(edge, faces[1], remappedIndices, positions, n1));
                }
                else
                {
                    // Angle is smaller, smooth edge
                    internalEdges.Add(edge.V1);
                    internalEdges.Add(edge.V2);
                }
            }
            else
            {
                // Non-manifold geometry (>2 faces sharing an edge) - mark as feature to highlight
                featureEdges.Add(edge.V1);
                featureEdges.Add(edge.V2);
                featureNormals1.Add(faceNormals[faces[0]]);
                featureNormals2.Add(faceNormals[faces[1]]);
                featureConvex.Add(true);
            }
        }

        return (featureEdges.ToArray(), internalEdges.ToArray(), featureNormals1.ToArray(), featureNormals2.ToArray(), featureConvex.ToArray());
    }

    /// <summary>
    /// Diz se uma feature edge (com duas faces, normais já apontando para fora) é convexa ou
    /// côncava. Pega o vértice do outro triângulo que não está na aresta e vê de que lado do plano
    /// da primeira face ele cai: do lado de dentro (atrás do plano, na direção oposta à normal) é
    /// uma quina convexa; do lado de fora (na direção da normal) é uma reentrância côncava, como a
    /// aresta interna de um degrau. Usado para não tentar chanfrar/arredondar arestas côncavas — o
    /// cutter desse fillet só remove material, então numa aresta côncava ele abriria um entalhe em
    /// vez de cortar a quina.
    /// </summary>
    private static bool IsConvexEdge(EdgeKey edge, int otherFaceIndex, uint[] remappedIndices, float[] positions, Vector3 n0)
    {
        uint ov0 = remappedIndices[otherFaceIndex * 3 + 0];
        uint ov1 = remappedIndices[otherFaceIndex * 3 + 1];
        uint ov2 = remappedIndices[otherFaceIndex * 3 + 2];

        uint thirdVertex = ov0 != edge.V1 && ov0 != edge.V2 ? ov0
            : ov1 != edge.V1 && ov1 != edge.V2 ? ov1
            : ov2;

        var edgeStart = new Vector3(positions[edge.V1 * 3], positions[edge.V1 * 3 + 1], positions[edge.V1 * 3 + 2]);
        var p3 = new Vector3(positions[thirdVertex * 3], positions[thirdVertex * 3 + 1], positions[thirdVertex * 3 + 2]);

        float d = Vector3.Dot(n0, p3 - edgeStart);
        return d <= 1e-6f;
    }

    private static void AddFaceToEdge(Dictionary<EdgeKey, List<int>> map, uint v1, uint v2, int faceIdx)
    {
        var key = new EdgeKey(v1, v2);
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<int>(2);
            map[key] = list;
        }
        list.Add(faceIdx);
    }
}


