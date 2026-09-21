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

    public static (uint[] featureEdges, uint[] internalEdges) ClassifyEdges(
        float[] positions, uint[] indices, float creaseAngleDegrees = 30.0f)
    {
        if (indices.Length == 0)
            return (Array.Empty<uint>(), Array.Empty<uint>());

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
            }
        }

        return (featureEdges.ToArray(), internalEdges.ToArray());
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
