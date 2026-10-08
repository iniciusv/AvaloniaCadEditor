using System;
using System.Collections.Generic;
using System.Numerics;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

/// <summary>
/// Monta, a partir de uma <see cref="TriangleMesh"/>, os vértices de linha que o
/// <see cref="WireframeEdgeShader"/> consome. Só geometria, sem cor nem GL.
/// <para>
/// Classifica as arestas aqui mesmo, com o <see cref="EdgeDetector"/>, em vez de ler as da malha:
/// a silhueta precisa das normais das arestas suaves, que a malha não guarda, e assim o wireframe
/// não pede nada a mais de quem constrói malhas. De quebra, malha importada (STL), que chega sem
/// arestas, também ganha contorno.
/// </para>
/// </summary>
public static class WireframeGeometry
{
    /// <summary>
    /// Arestas suaves com faces vizinhas mais próximas que isto (em graus) não podem ser
    /// silhueta: são diagonais de triangulação de faces planas ou a linha de tangência de um
    /// fillet. Deixá-las de fora evita silhuetas espúrias numa face plana vista de lado.
    /// </summary>
    public const float MinSilhouetteDihedralDegrees = 0.5f;

    private static readonly float MaxSilhouetteCandidateCos = MathF.Cos(MinSilhouetteDihedralDegrees * MathF.PI / 180f);

    /// <summary>
    /// Dois vértices por aresta, <see cref="WireframeEdgeShader.FloatsPerVertex"/> floats cada:
    /// posição, outro extremo, normal 1, normal 2, (lado, candidata a silhueta). Primeiro todas
    /// as quinas (sempre desenhadas), depois as arestas suaves candidatas a silhueta.
    /// </summary>
    /// <param name="creaseAngle">O mesmo <c>CreaseAngle</c> da tesselação, para as quinas saírem
    /// iguais às do modo sombreado.</param>
    public static float[] BuildLineVertices(TriangleMesh mesh, float creaseAngle)
    {
        var edges = EdgeDetector.ClassifyEdges(mesh.Positions, mesh.Indices, creaseAngle);
        var result = new List<float>((edges.FeatureEdges.Length + edges.InternalEdges.Length) * WireframeEdgeShader.FloatsPerVertex);
        var positions = mesh.Positions;

        for (int i = 0; i + 1 < edges.FeatureEdges.Length; i += 2)
        {
            AddEdge(result, positions, edges.FeatureEdges[i], edges.FeatureEdges[i + 1], Vector3.Zero, Vector3.Zero, isSilhouetteCandidate: false);
        }

        for (int e = 0; e < edges.InternalNormals1.Length; e++)
        {
            var n1 = edges.InternalNormals1[e];
            var n2 = edges.InternalNormals2[e];
            if (Vector3.Dot(n1, n2) >= MaxSilhouetteCandidateCos) continue;

            AddEdge(result, positions, edges.InternalEdges[e * 2], edges.InternalEdges[e * 2 + 1], n1, n2, isSilhouetteCandidate: true);
        }

        return result.ToArray();
    }

    private static void AddEdge(List<float> target, float[] positions, uint a, uint b, Vector3 n1, Vector3 n2, bool isSilhouetteCandidate)
    {
        AddVertex(target, positions, a, b, n1, n2, side: 0f, isSilhouetteCandidate);
        AddVertex(target, positions, b, a, n1, n2, side: 1f, isSilhouetteCandidate);
    }

    private static void AddVertex(List<float> target, float[] positions, uint self, uint other, Vector3 n1, Vector3 n2, float side, bool isSilhouetteCandidate)
    {
        int s = (int)self * 3;
        int o = (int)other * 3;
        target.Add(positions[s]); target.Add(positions[s + 1]); target.Add(positions[s + 2]);
        target.Add(positions[o]); target.Add(positions[o + 1]); target.Add(positions[o + 2]);
        target.Add(n1.X); target.Add(n1.Y); target.Add(n1.Z);
        target.Add(n2.X); target.Add(n2.Y); target.Add(n2.Z);
        target.Add(side);
        target.Add(isSilhouetteCandidate ? 1f : 0f);
    }
}
