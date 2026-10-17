using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

/// <summary>
/// Topologia de uma <see cref="TriangleMesh"/>: vértices soldados pela posição (o OCCT duplica os vértices
/// entre faces vizinhas), as arestas com os triângulos que as usam e a normal de cada triângulo. É a base
/// para achar uma face (<see cref="MeshFaces"/>) e para projetar arestas e silhuetas num plano
/// (<see cref="PlaneProjection"/>).
/// </summary>
public sealed class MeshEdgeGraph
{
    /// <summary>Mesma solda (4 casas) do <see cref="EdgeDetector"/> e do tesselador.</summary>
    private const double WeldScale = 1e4;

    /// <summary>Mesmo ângulo de quina padrão de <c>TessellationSettings.CreaseAngle</c>.</summary>
    public const double DefaultCreaseAngleDegrees = 30.0;

    /// <summary>Normal abaixo deste tamanho é de triângulo degenerado (lasca da tesselação).</summary>
    private const double DegenerateNormalLength = 1e-12;

    /// <summary>Lado de triângulo degenerado cujas duas pontas soldaram no mesmo vértice.</summary>
    public const int NoEdge = -1;
    private static readonly (int, int) NoEdgeKey = (-1, -1);

    /// <summary>Uma aresta soldada e os triângulos que passam por ela (1 = borda, 3+ = não-manifold).</summary>
    public sealed record Edge(int A, int B, IReadOnlyList<int> Triangles);

    private readonly Point3D[] _vertices;
    private readonly int[] _triangleVertices;
    private readonly Vector3D[] _normals;
    private readonly bool[] _degenerate;
    private readonly int[] _triangleEdges;
    private readonly double _creaseCos;

    public IReadOnlyList<Edge> Edges { get; }
    public int TriangleCount => _normals.Length;

    private MeshEdgeGraph(Point3D[] vertices, int[] triangleVertices, Vector3D[] normals, bool[] degenerate,
        List<Edge> edges, int[] triangleEdges, double creaseAngleDegrees)
    {
        _vertices = vertices;
        _triangleVertices = triangleVertices;
        _normals = normals;
        _degenerate = degenerate;
        Edges = edges;
        _triangleEdges = triangleEdges;
        _creaseCos = Math.Cos(creaseAngleDegrees * Math.PI / 180.0);
    }

    public static MeshEdgeGraph Build(TriangleMesh mesh, double creaseAngleDegrees = DefaultCreaseAngleDegrees)
    {
        var (vertices, remap) = Weld(mesh);
        int triCount = mesh.TriangleCount;
        var triangleVertices = new int[triCount * 3];
        var normals = new Vector3D[triCount];
        var degenerate = new bool[triCount];

        for (int t = 0; t < triCount; t++)
        {
            for (int k = 0; k < 3; k++) triangleVertices[t * 3 + k] = remap[mesh.Indices[t * 3 + k]];
            var p0 = vertices[triangleVertices[t * 3]];
            var cross = (vertices[triangleVertices[t * 3 + 1]] - p0).CrossProduct(vertices[triangleVertices[t * 3 + 2]] - p0);
            degenerate[t] = cross.Length < DegenerateNormalLength;
            normals[t] = degenerate[t] ? default : cross.Normalize();
        }

        var (edges, triangleEdges) = BuildEdges(triangleVertices, triCount);
        return new MeshEdgeGraph(vertices, triangleVertices, normals, degenerate, edges, triangleEdges, creaseAngleDegrees);
    }

    public Point3D Vertex(int index) => _vertices[index];

    public Vector3D Normal(int triangle) => _normals[triangle];

    public (Point3D A, Point3D B, Point3D C) Triangle(int triangle) => (
        _vertices[_triangleVertices[triangle * 3]],
        _vertices[_triangleVertices[triangle * 3 + 1]],
        _vertices[_triangleVertices[triangle * 3 + 2]]);

    /// <summary>Índices em <see cref="Edges"/> das três arestas do triângulo (<see cref="NoEdge"/> no lado que colapsou).</summary>
    public ReadOnlySpan<int> EdgesOf(int triangle) => new(_triangleEdges, triangle * 3, 3);

    /// <summary>
    /// Aresta suave: entre exatamente dois triângulos com ângulo menor que o de quina — a que o wireframe não
    /// desenha. Triângulo degenerado não tem normal, então não separa nada.
    /// </summary>
    public bool IsSmooth(Edge edge)
    {
        if (edge.Triangles.Count != 2) return false;
        int t1 = edge.Triangles[0], t2 = edge.Triangles[1];
        if (_degenerate[t1] || _degenerate[t2]) return true;
        return _normals[t1].DotProduct(_normals[t2]) >= _creaseCos;
    }

    private static (Point3D[] Vertices, int[] Remap) Weld(TriangleMesh mesh)
    {
        var ids = new Dictionary<(long, long, long), int>();
        var vertices = new List<Point3D>();
        var remap = new int[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = new Point3D(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2]);
            var key = ((long)Math.Round(p.X * WeldScale), (long)Math.Round(p.Y * WeldScale), (long)Math.Round(p.Z * WeldScale));
            if (!ids.TryGetValue(key, out var id))
            {
                id = vertices.Count;
                vertices.Add(p);
                ids[key] = id;
            }
            remap[i] = id;
        }
        return (vertices.ToArray(), remap);
    }

    private static (List<Edge> Edges, int[] TriangleEdges) BuildEdges(int[] triangleVertices, int triCount)
    {
        var users = new Dictionary<(int, int), List<int>>();
        var order = new List<(int, int)>();
        var triangleEdgeKeys = new (int, int)[triCount * 3];

        for (int t = 0; t < triCount; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = triangleVertices[t * 3 + k], b = triangleVertices[t * 3 + (k + 1) % 3];
                if (a == b) { triangleEdgeKeys[t * 3 + k] = NoEdgeKey; continue; }
                var key = a < b ? (a, b) : (b, a);
                if (!users.TryGetValue(key, out var list))
                {
                    list = new List<int>(2);
                    users[key] = list;
                    order.Add(key);
                }
                if (!list.Contains(t)) list.Add(t);
                triangleEdgeKeys[t * 3 + k] = key;
            }
        }

        var edges = new List<Edge>(order.Count);
        var indexOf = new Dictionary<(int, int), int>(order.Count);
        foreach (var key in order)
        {
            indexOf[key] = edges.Count;
            edges.Add(new Edge(key.Item1, key.Item2, users[key]));
        }

        var triangleEdges = new int[triCount * 3];
        for (int i = 0; i < triangleEdges.Length; i++)
            triangleEdges[i] = triangleEdgeKeys[i] == NoEdgeKey ? NoEdge : indexOf[triangleEdgeKeys[i]];
        return (edges, triangleEdges);
    }
}
