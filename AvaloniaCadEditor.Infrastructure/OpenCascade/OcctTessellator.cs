using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Settings;
using OcctNet.Wrapper;

namespace AvaloniaCadEditor.Infrastructure.OpenCascade;

/// <summary>
/// Converte um sólido B-Rep do OCCT (<see cref="OcctShape"/>) na malha de triângulos que a
/// viewport consome (<see cref="TriangleMesh"/>): tesselação, correção de orientação dos
/// triângulos, normais suavizadas por ângulo de quina e classificação de arestas.
/// <para>
/// Não sabe nada sobre <c>IFeature</c>, cache ou a árvore da cena — só recebe uma forma pronta e
/// as <see cref="TessellationSettings"/>. Por isso é estática: não há estado a guardar entre
/// chamadas. Quem constrói as formas é <see cref="OpenCascadeGeometryKernel"/>.
/// </para>
/// </summary>
internal static class OcctTessellator
{
    public static TriangleMesh Tessellate(OcctShape shape, TessellationSettings settings)
    {
        // ProfileSegments é o knob de "quão redondo é redondo" em todo o app: o cilindro e a caixa o
        // usam direto (polígono extrudado) e aqui ele vira um PISO de facetas para as superfícies
        // curvas exatas (cone, fillet), para um cone e um cilindro de mesmo raio não saírem com
        // contagens de faceta diferentes. Faces planas ignoram a deflexão, então isto só alcança
        // geometria curva. GeometryKey já inclui ProfileSegments, então o cache continua coerente.
        double angularDeflection = Math.Min(
            settings.AngularDeflection,
            2.0 * Math.PI / Math.Max(settings.ProfileSegments, 3));

        var occtMesh = shape.Triangulate(settings.LinearDeflection, angularDeflection);
        if (occtMesh == null || occtMesh.TriangleCount == 0)
            return TriangleMesh.Empty;

        var positions = new List<float>();
        var normals = new List<float>();

        var verts = occtMesh.Vertices;

        // Corrige a orientação dos triângulos ANTES de calcular normais/volume. Uma heurística
        // "a normal deve apontar para longe do centróide de TODOS os vértices" (usada antes
        // aqui) só funciona para sólidos convexos e de um corpo só — ela inverte triângulos
        // silenciosamente em qualquer peça côncava, com furo, ou resultado de boolean com mais
        // de um sólido (o centro médio dos vértices não é "dentro" do sólido nesses casos). O
        // jeito robusto é propagar a orientação pelas arestas compartilhadas (vizinhos devem
        // percorrer a aresta em sentidos opostos) e, por componente conexo, corrigir com o
        // sinal do volume (teorema da divergência) qual das duas orientações relativas é a de
        // fora.
        var triIndices = FixTriangleWinding(occtMesh);

        var normalArrays = new float[verts.Count * 3];

        for (int i = 0; i < verts.Count; i++)
        {
            positions.Add((float)verts[i].X);
            positions.Add((float)verts[i].Y);
            positions.Add((float)verts[i].Z);
        }

        // Calculate flat normals since OcctMeshVertex doesn't provide them
        // (a orientação já foi corrigida por FixTriangleWinding, então basta o produto vetorial)
        for (int i = 0; i < triIndices.Length; i += 3)
        {
            uint i1 = triIndices[i];
            uint i2 = triIndices[i + 1];
            uint i3 = triIndices[i + 2];

            var p1 = verts[(int)i1];
            var p2 = verts[(int)i2];
            var p3 = verts[(int)i3];

            // cross product
            var ux = p2.X - p1.X;
            var uy = p2.Y - p1.Y;
            var uz = p2.Z - p1.Z;

            var vx = p3.X - p1.X;
            var vy = p3.Y - p1.Y;
            var vz = p3.Z - p1.Z;

            var nx = (uy * vz) - (uz * vy);
            var ny = (uz * vx) - (ux * vz);
            var nz = (ux * vy) - (uy * vx);

            // Normalize face normal
            var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0)
            {
                nx /= len;
                ny /= len;
                nz /= len;
            }

            // Accumulate flat normals
            for (int j = 0; j < 3; j++)
            {
                int idx = (int)(j == 0 ? i1 : (j == 1 ? i2 : i3)) * 3;
                normalArrays[idx] += (float)nx;
                normalArrays[idx + 1] += (float)ny;
                normalArrays[idx + 2] += (float)nz;
            }
        }

        var indices = new List<uint>(triIndices);

        // Pre-normalize the accumulated normals so we have pure directions for comparison
        for (int i = 0; i < normalArrays.Length; i += 3)
        {
            var len = (float)Math.Sqrt(normalArrays[i]*normalArrays[i] + normalArrays[i+1]*normalArrays[i+1] + normalArrays[i+2]*normalArrays[i+2]);
            if (len > 0)
            {
                normalArrays[i] /= len;
                normalArrays[i+1] /= len;
                normalArrays[i+2] /= len;
            }
        }

        // Group coincident vertices to average normals across faces
        var posMap = new System.Collections.Generic.Dictionary<System.Numerics.Vector3, System.Collections.Generic.List<int>>();
        for (int i = 0; i < verts.Count; i++)
        {
            var p = new System.Numerics.Vector3(
                (float)Math.Round(positions[i * 3], 4),
                (float)Math.Round(positions[i * 3 + 1], 4),
                (float)Math.Round(positions[i * 3 + 2], 4));

            if (!posMap.TryGetValue(p, out var list))
            {
                list = new System.Collections.Generic.List<int>();
                posMap[p] = list;
            }
            list.Add(i);
        }

        float thresholdCos = (float)Math.Cos(settings.CreaseAngle * Math.PI / 180.0);
        var smoothedNormals = new float[normalArrays.Length];

        foreach (var group in posMap.Values)
        {
            foreach (var vIdx in group)
            {
                var vNx = normalArrays[vIdx * 3];
                var vNy = normalArrays[vIdx * 3 + 1];
                var vNz = normalArrays[vIdx * 3 + 2];

                float sumNx = 0, sumNy = 0, sumNz = 0;

                foreach (var uIdx in group)
                {
                    var uNx = normalArrays[uIdx * 3];
                    var uNy = normalArrays[uIdx * 3 + 1];
                    var uNz = normalArrays[uIdx * 3 + 2];

                    float dot = vNx * uNx + vNy * uNy + vNz * uNz;
                    if (dot > thresholdCos - 1e-4f) // Smooth if angle is less than crease angle
                    {
                        sumNx += uNx;
                        sumNy += uNy;
                        sumNz += uNz;
                    }
                }

                // Normalize final smoothed normal
                float len = (float)Math.Sqrt(sumNx * sumNx + sumNy * sumNy + sumNz * sumNz);
                if (len > 0)
                {
                    smoothedNormals[vIdx * 3] = sumNx / len;
                    smoothedNormals[vIdx * 3 + 1] = sumNy / len;
                    smoothedNormals[vIdx * 3 + 2] = sumNz / len;
                }
                else
                {
                    smoothedNormals[vIdx * 3] = vNx;
                    smoothedNormals[vIdx * 3 + 1] = vNy;
                    smoothedNormals[vIdx * 3 + 2] = vNz;
                }
            }
        }

        normals.AddRange(smoothedNormals);

        var posArr = positions.ToArray();
        var idxArr = indices.ToArray();
        var (featureEdges, internalEdges, n1, n2, _) = EdgeDetector.ClassifyEdges(posArr, idxArr, settings.CreaseAngle);
        var featureNormals1 = new float[n1.Length * 3];
        var featureNormals2 = new float[n2.Length * 3];
        for (int i = 0; i < n1.Length; i++) { featureNormals1[i*3] = n1[i].X; featureNormals1[i*3+1] = n1[i].Y; featureNormals1[i*3+2] = n1[i].Z; featureNormals2[i*3] = n2[i].X; featureNormals2[i*3+1] = n2[i].Y; featureNormals2[i*3+2] = n2[i].Z; }

        return new TriangleMesh(posArr, normals.ToArray(), idxArr, featureEdges, internalEdges, featureNormals1, featureNormals2);
    }

    /// <summary>
    /// Corrige a orientação (sentido de giro) de cada triângulo tesselado pelo OCCT, garantindo
    /// que todas as normais de um mesmo sólido apontem consistentemente para fora — mesmo em
    /// peças côncavas, com furos, ou resultantes de um boolean com vários corpos.
    ///
    /// Funciona em duas etapas por componente conexo (grupo de triângulos ligados por arestas
    /// compartilhadas — cada "casca" de um sólido, mesmo que a malha tenha vários sólidos
    /// desconexos de uma vez):
    ///  1. Propaga a orientação por BFS: dois triângulos vizinhos que compartilham uma aresta
    ///     (exatamente 2 usuários — arestas de borda ou não-manifold ficam de fora) devem
    ///     percorrê-la em sentidos opostos entre si. Isso já deixa a casca inteira consistente
    ///     entre si, mas ainda pode estar "de dentro para fora" como um todo.
    ///  2. Calcula o volume com sinal (teorema da divergência) da casca já consistente; se vier
    ///     negativo, inverte todos os triângulos do componente de uma vez.
    /// </summary>
    private static uint[] FixTriangleWinding(OcctMesh occtMesh)
    {
        var verts = occtMesh.Vertices;
        var rawIndices = occtMesh.TriangleIndices;
        int triCount = rawIndices.Count / 3;

        // 1. Agrupa vértices coincidentes (a tesselação do OCCT duplica vértices entre faces
        //    adjacentes) num id canônico, para achar vizinhança por aresta.
        var canonicalId = new int[verts.Count];
        var canonicalMap = new Dictionary<(long, long, long), int>();
        const double weldScale = 10000.0; // 4 casas decimais
        for (int i = 0; i < verts.Count; i++)
        {
            var key = (
                (long)Math.Round(verts[i].X * weldScale),
                (long)Math.Round(verts[i].Y * weldScale),
                (long)Math.Round(verts[i].Z * weldScale));
            if (!canonicalMap.TryGetValue(key, out var id))
            {
                id = canonicalMap.Count;
                canonicalMap[key] = id;
            }
            canonicalId[i] = id;
        }

        var triCanon = new (int A, int B, int C)[triCount];
        for (int t = 0; t < triCount; t++)
        {
            triCanon[t] = (
                canonicalId[rawIndices[t * 3]],
                canonicalId[rawIndices[t * 3 + 1]],
                canonicalId[rawIndices[t * 3 + 2]]);
        }

        // 2. Para cada aresta (não direcionada, em ids canônicos), guarda quem a usa e em que
        //    sentido (a direção original, ainda sem nenhuma correção aplicada).
        var edgeUsers = new Dictionary<(int, int), List<(int Triangle, int From, int To)>>();
        void AddEdge(int t, int u, int v)
        {
            var key = u < v ? (u, v) : (v, u);
            if (!edgeUsers.TryGetValue(key, out var list))
            {
                list = new List<(int, int, int)>();
                edgeUsers[key] = list;
            }
            list.Add((t, u, v));
        }
        for (int t = 0; t < triCount; t++)
        {
            var (a, b, c) = triCanon[t];
            AddEdge(t, a, b);
            AddEdge(t, b, c);
            AddEdge(t, c, a);
        }

        var flipped = new bool[triCount];
        var visited = new bool[triCount];
        var queue = new Queue<int>();

        for (int seed = 0; seed < triCount; seed++)
        {
            if (visited[seed]) continue;
            visited[seed] = true;
            var component = new List<int> { seed };
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                var (a, b, c) = triCanon[t];

                foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                {
                    var key = u < v ? (u, v) : (v, u);
                    var users = edgeUsers[key];
                    if (users.Count != 2) continue; // borda ou não-manifold: não propaga por aqui

                    // Direção efetiva de t nessa aresta, já considerando se t foi invertido.
                    bool effectiveFromIsU = !flipped[t];
                    int effectiveFrom = effectiveFromIsU ? u : v;
                    int effectiveTo = effectiveFromIsU ? v : u;

                    foreach (var (otherT, origFrom, origTo) in users)
                    {
                        if (otherT == t || visited[otherT]) continue;

                        // Quer que a direção efetiva de otherT seja a OPOSTA (effectiveTo -> effectiveFrom).
                        // origFrom/origTo é a direção original (sem flip) de otherT nessa mesma aresta.
                        flipped[otherT] = origFrom == effectiveFrom;
                        visited[otherT] = true;
                        component.Add(otherT);
                        queue.Enqueue(otherT);
                    }
                }
            }

            // 3. Corrige a orientação global do componente pelo sinal do volume.
            double signedVolume = 0;
            foreach (var t in component)
            {
                signedVolume += SignedTetrahedronVolume(verts, rawIndices, t, flipped[t]);
            }

            if (signedVolume < 0)
            {
                foreach (var t in component) flipped[t] = !flipped[t];
            }
        }

        var result = new uint[rawIndices.Count];
        for (int t = 0; t < triCount; t++)
        {
            uint i1 = (uint)rawIndices[t * 3];
            uint i2 = (uint)rawIndices[t * 3 + 1];
            uint i3 = (uint)rawIndices[t * 3 + 2];
            if (flipped[t]) { (i2, i3) = (i3, i2); }

            result[t * 3] = i1;
            result[t * 3 + 1] = i2;
            result[t * 3 + 2] = i3;
        }
        return result;
    }

    private static double SignedTetrahedronVolume(IReadOnlyList<OcctMeshVertex> verts, IReadOnlyList<int> rawIndices, int triangle, bool flipped)
    {
        int i1 = rawIndices[triangle * 3];
        int i2 = rawIndices[triangle * 3 + 1];
        int i3 = rawIndices[triangle * 3 + 2];
        if (flipped) { (i2, i3) = (i3, i2); }

        var p1 = verts[i1];
        var p2 = verts[i2];
        var p3 = verts[i3];

        return (p1.X * (p2.Y * p3.Z - p2.Z * p3.Y)
              + p1.Y * (p2.Z * p3.X - p2.X * p3.Z)
              + p1.Z * (p2.X * p3.Y - p2.Y * p3.X)) / 6.0;
    }
}
