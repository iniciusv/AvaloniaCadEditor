using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

/// <summary>
/// Uma aresta viva convexa em arco de círculo, entre uma face plana e uma parede cilíndrica paralela ao
/// eixo: a boca de um furo, o topo de um ressalto, o canto arredondado de um perfil extrudado.
/// </summary>
/// <param name="Axis">Normal da face plana, apontando para fora do material.</param>
/// <param name="IsHole">O material fica fora do círculo (boca de furo, canto arredondado côncavo);
/// <c>false</c> quando fica dentro (ressalto, canto arredondado convexo).</param>
/// <param name="StartDirection">Do centro para o começo do arco, no plano da face; o arco corre no sentido
/// anti-horário em torno de <see cref="Axis"/> (regra da mão direita).</param>
/// <param name="EdgeIndices">Índices das arestas da malha que formam o arco (o par <c>2i, 2i+1</c> de
/// <see cref="EdgeClassification.FeatureEdges"/>).</param>
public sealed record CircularEdge(
    Point3D Center,
    Vector3D Axis,
    double Radius,
    bool IsHole,
    Vector3D StartDirection,
    double SweepRadians,
    bool IsFullCircle,
    IReadOnlyList<int> EdgeIndices);

/// <summary>
/// Reconhece, nas arestas vivas de uma malha, as cadeias de segmentos que são um arco de círculo.
/// <para>
/// Existe por desempenho: um círculo vira <c>ProfileSegments</c> segmentos na malha, e o chanfro por
/// malha (<c>OcctFilletBuilder</c>) fazia uma booleana por segmento. Reconhecido o arco, o chanfro dele é
/// um anel de revolução só. Um polígono de verdade (um sextavado) não pode passar por arco: o passo angular
/// de cada segmento tem de ser de tesselação, no máximo <c>maxStepRadians</c>.
/// </para>
/// </summary>
public static class ArcEdgeFinder
{
    private const int MinSegments = 3;
    private const double SameNormalCos = 0.9995;
    private const double MinTurnRadians = 1e-3;
    private const double PositionTolerance = 1e-3;

    /// <summary>
    /// Passo angular máximo de um arco reconhecido, para a tesselação dada: um pouco acima do passo de um
    /// círculo de <paramref name="profileSegments"/> lados e nunca acima de 25°, que é onde um polígono
    /// de verdade (hexágono: 60°) começa a parecer círculo.
    /// </summary>
    public static double MaxStepFor(int profileSegments) =>
        Math.Min(1.5 * 2 * Math.PI / Math.Max(profileSegments, 3), 25 * Math.PI / 180);

    public static IReadOnlyList<CircularEdge> Find(float[] positions, EdgeClassification edges, double maxStepRadians)
    {
        int edgeCount = edges.FeatureEdges.Length / 2;
        var byVertex = new Dictionary<uint, List<int>>();
        for (int e = 0; e < edgeCount; e++)
        {
            if (!edges.FeatureConvex[e]) continue;
            Add(byVertex, edges.FeatureEdges[2 * e], e);
            Add(byVertex, edges.FeatureEdges[2 * e + 1], e);
        }

        var finder = new Search(positions, edges, byVertex, maxStepRadians);
        var used = new bool[edgeCount];
        var arcs = new List<CircularEdge>();
        for (int e = 0; e < edgeCount; e++)
        {
            if (!edges.FeatureConvex[e] || used[e]) continue;

            // Qualquer das duas faces pode ser a plana.
            foreach (var plane in new[] { edges.FeatureNormals1[e], edges.FeatureNormals2[e] })
            {
                if (finder.Grow(e, plane, used) is not { } chain) continue;
                if (finder.Fit(chain, plane) is not { } arc) continue;

                foreach (int i in arc.EdgeIndices) used[i] = true;
                arcs.Add(arc);
                break;
            }
        }
        return arcs;
    }

    private static void Add(Dictionary<uint, List<int>> map, uint vertex, int edge)
    {
        if (!map.TryGetValue(vertex, out var list)) map[vertex] = list = new List<int>(2);
        list.Add(edge);
    }

    /// <summary>Uma cadeia: os vértices em ordem e as arestas entre eles (<c>Edges[k]</c> liga
    /// <c>Vertices[k]</c> a <c>Vertices[k + 1]</c>); fechada, o último vértice é o primeiro.</summary>
    private sealed record Chain(List<uint> Vertices, List<int> Edges, bool Closed);

    private sealed class Search(float[] positions, EdgeClassification edges, Dictionary<uint, List<int>> byVertex, double maxStep)
    {
        private Vector3 Pos(uint v) => new(positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2]);

        private uint Other(int edge, uint vertex) =>
            edges.FeatureEdges[2 * edge] == vertex ? edges.FeatureEdges[2 * edge + 1] : edges.FeatureEdges[2 * edge];

        /// <summary>A normal da aresta que não é <paramref name="plane"/>, ou null se nenhuma das duas é.</summary>
        private Vector3? WallNormal(int edge, Vector3 plane)
        {
            var n1 = edges.FeatureNormals1[edge];
            var n2 = edges.FeatureNormals2[edge];
            if (Vector3.Dot(n1, plane) > SameNormalCos) return Vector3.Dot(n2, plane) > SameNormalCos ? null : n2;
            if (Vector3.Dot(n2, plane) > SameNormalCos) return n1;
            return null;
        }

        /// <summary>A partir da aresta <paramref name="start"/>, as vizinhas que seguem o mesmo arco: face plana
        /// <paramref name="plane"/> dos dois lados da emenda, aresta deitada no plano e uma curva pequena e
        /// sempre para o mesmo lado. Bifurcação para a cadeia ali.</summary>
        public Chain? Grow(int start, Vector3 plane, bool[] used)
        {
            if (WallNormal(start, plane) == null || !LiesInPlane(start, plane)) return null;

            uint a = edges.FeatureEdges[2 * start];
            uint b = edges.FeatureEdges[2 * start + 1];
            var vertices = new List<uint> { a, b };
            var chainEdges = new List<int> { start };
            var inChain = new HashSet<int> { start };
            int turnSign = 0;

            bool closed = Extend(vertices, chainEdges, inChain, plane, used, ref turnSign);
            if (!closed)
            {
                // Para trás: anda a partir do outro extremo e depois desvira.
                vertices.Reverse();
                chainEdges.Reverse();
                turnSign = -turnSign;
                Extend(vertices, chainEdges, inChain, plane, used, ref turnSign);
            }

            return chainEdges.Count >= MinSegments ? new Chain(vertices, chainEdges, closed) : null;
        }

        /// <returns><c>true</c> se a cadeia fechou no primeiro vértice.</returns>
        private bool Extend(List<uint> vertices, List<int> chainEdges, HashSet<int> inChain, Vector3 plane, bool[] used, ref int turnSign)
        {
            while (true)
            {
                uint tip = vertices[^1];
                var incoming = Vector3.Normalize(Pos(tip) - Pos(vertices[^2]));

                int next = -1;
                int nextSign = 0;
                foreach (int candidate in byVertex.GetValueOrDefault(tip) ?? new List<int>())
                {
                    if (inChain.Contains(candidate) || used[candidate]) continue;
                    if (WallNormal(candidate, plane) == null || !LiesInPlane(candidate, plane)) continue;

                    var outgoing = Vector3.Normalize(Pos(Other(candidate, tip)) - Pos(tip));
                    double turn = Math.Acos(Math.Clamp(Vector3.Dot(incoming, outgoing), -1f, 1f));
                    if (turn < MinTurnRadians || turn > maxStep) continue;

                    int sign = Math.Sign(Vector3.Dot(Vector3.Cross(incoming, outgoing), plane));
                    if (turnSign != 0 && sign != turnSign) continue;

                    if (next != -1) return false; // bifurcação: não é um arco simples
                    next = candidate;
                    nextSign = sign;
                }

                if (next == -1) return false;
                turnSign = nextSign;
                uint far = Other(next, tip);
                chainEdges.Add(next);
                inChain.Add(next);
                vertices.Add(far);
                if (far == vertices[0]) return true;
            }
        }

        private bool LiesInPlane(int edge, Vector3 plane)
        {
            var d = Pos(edges.FeatureEdges[2 * edge + 1]) - Pos(edges.FeatureEdges[2 * edge]);
            float length = d.Length();
            return length > 1e-6f && Math.Abs(Vector3.Dot(d / length, plane)) < 1e-3f;
        }

        /// <summary>
        /// O círculo da cadeia, ou null se ela não for um. Numa cadeia aberta, a ponta pode ter puxado a
        /// primeira reta tangente (a curva até ela é pequena): as pontas que fogem do círculo saem, uma por vez.
        /// </summary>
        public CircularEdge? Fit(Chain chain, Vector3 plane)
        {
            var vertices = chain.Vertices;
            var chainEdges = chain.Edges;
            while (chainEdges.Count >= MinSegments)
            {
                if (TryFit(vertices, chainEdges, chain.Closed, plane, out var arc, out bool startIsWorse))
                    return arc;
                if (chain.Closed) return null;

                vertices = new List<uint>(vertices);
                chainEdges = new List<int>(chainEdges);
                if (startIsWorse)
                {
                    vertices.RemoveAt(0);
                    chainEdges.RemoveAt(0);
                }
                else
                {
                    vertices.RemoveAt(vertices.Count - 1);
                    chainEdges.RemoveAt(chainEdges.Count - 1);
                }
            }
            return null;
        }

        private bool TryFit(List<uint> vertices, List<int> chainEdges, bool closed, Vector3 plane, out CircularEdge? arc, out bool startIsWorse)
        {
            arc = null;
            startIsWorse = false;
            int count = closed ? vertices.Count - 1 : vertices.Count;
            var points = vertices.Take(count).Select(v => ToDouble(Pos(v))).ToList();

            var axis = ToDouble(plane);
            axis /= axis.Length();
            var origin = points[0];
            var u = Reject(points[1] - origin, axis);
            u /= u.Length();
            var v = Cross(axis, u);

            // Círculo por mínimos quadrados (Kåsa) no plano: x² + y² + D x + E y + F = 0.
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sxz = 0, syz = 0, sz = 0;
            foreach (var p in points)
            {
                var d = p - origin;
                if (Math.Abs(Dot(d, axis)) > PositionTolerance) return false; // fora do plano
                double x = Dot(d, u), y = Dot(d, v), z = x * x + y * y;
                sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y;
                sxz += x * z; syz += y * z; sz += z;
            }
            int n = points.Count;
            if (!Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, n, -sxz, -syz, -sz, out double dd, out double ee, out double ff)) return false;

            double cx = -dd / 2, cy = -ee / 2;
            double radius2 = cx * cx + cy * cy - ff;
            if (radius2 <= 0) return false;
            double radius = Math.Sqrt(radius2);

            var center = origin + u * cx + v * cy;
            double Residual(int k) => Math.Abs(((points[k] - center)).Length() - radius);
            double tolerance = PositionTolerance + 1e-4 * radius;
            if (Enumerable.Range(0, n).Any(k => Residual(k) > tolerance))
            {
                startIsWorse = Residual(0) >= Residual(n - 1);
                return false;
            }

            // A outra face de cada segmento é uma parede paralela ao eixo, radial, e do mesmo lado em todos.
            // Na falha, a ponta mais perto do segmento ruim é a que sai (ex.: a reta tangente puxada pela cadeia).
            int side = 0;
            for (int k = 0; k < chainEdges.Count; k++)
            {
                int edge = chainEdges[k];
                startIsWorse = k < chainEdges.Count / 2;
                var wall = ToDouble(WallNormal(edge, plane)!.Value);
                if (Math.Abs(Dot(wall, axis)) > 0.1) return false;
                var mid = (ToDouble(Pos(edges.FeatureEdges[2 * edge])) + ToDouble(Pos(edges.FeatureEdges[2 * edge + 1]))) / 2;
                var radial = Reject(mid - center, axis);
                radial /= radial.Length();
                double dot = Dot(wall, radial) / wall.Length();
                if (Math.Abs(dot) < 0.9) return false;
                int s = Math.Sign(dot);
                if (side != 0 && s != side) return false;
                side = s;
            }

            Vector3D start;
            double sweep;
            if (closed)
            {
                start = Direction(points[0] - center);
                sweep = 2 * Math.PI;
            }
            else
            {
                var u0 = Reject(points[0] - center, axis);
                u0 /= u0.Length();
                var v0 = Cross(axis, u0);
                double previous = 0, total = 0;
                for (int k = 1; k < n; k++)
                {
                    var d = points[k] - center;
                    double angle = Math.Atan2(Dot(d, v0), Dot(d, u0));
                    double step = angle - previous;
                    if (step > Math.PI) step -= 2 * Math.PI;
                    if (step < -Math.PI) step += 2 * Math.PI;
                    total += step;
                    previous = angle;
                }
                start = Direction(total > 0 ? points[0] - center : points[n - 1] - center);
                sweep = Math.Abs(total);
            }

            arc = new CircularEdge(
                new Point3D(center.X, center.Y, center.Z),
                new Vector3D(axis.X, axis.Y, axis.Z),
                radius,
                IsHole: side < 0, // a parede olha para o eixo: o vazio está dentro do círculo
                start,
                sweep,
                closed,
                chainEdges.ToArray());
            return true;

            Vector3D Direction(Vector3d d)
            {
                var r = Reject(d, axis);
                r /= r.Length();
                return new Vector3D(r.X, r.Y, r.Z);
            }
        }

        private static bool Solve3(double a11, double a12, double a13, double a21, double a22, double a23, double a31, double a32, double a33,
            double b1, double b2, double b3, out double x1, out double x2, out double x3)
        {
            double det = a11 * (a22 * a33 - a23 * a32) - a12 * (a21 * a33 - a23 * a31) + a13 * (a21 * a32 - a22 * a31);
            x1 = x2 = x3 = 0;
            if (Math.Abs(det) < 1e-12) return false;
            x1 = (b1 * (a22 * a33 - a23 * a32) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a32 - a22 * b3)) / det;
            x2 = (a11 * (b2 * a33 - a23 * b3) - b1 * (a21 * a33 - a23 * a31) + a13 * (a21 * b3 - b2 * a31)) / det;
            x3 = (a11 * (a22 * b3 - b2 * a32) - a12 * (a21 * b3 - b2 * a31) + b1 * (a21 * a32 - a22 * a31)) / det;
            return true;
        }

        private static Vector3d ToDouble(Vector3 v) => new(v.X, v.Y, v.Z);
        private static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        private static Vector3d Cross(Vector3d a, Vector3d b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        private static Vector3d Reject(Vector3d d, Vector3d axis) => d - axis * Dot(d, axis);
    }

    /// <summary>Vetor em double só para as contas daqui: as posições da malha são float, e o ajuste do
    /// círculo em coordenadas de centenas de mm perderia precisão em float.</summary>
    private readonly record struct Vector3d(double X, double Y, double Z)
    {
        public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
        public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3d operator *(Vector3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static Vector3d operator /(Vector3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    }
}
