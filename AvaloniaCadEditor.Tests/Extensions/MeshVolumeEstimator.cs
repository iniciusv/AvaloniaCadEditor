using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Tests.Extensions;

public static class MeshVolumeEstimator
{
    public static double EstimatePrismaticVolume(TriangleMesh mesh)
    {
        if (mesh == null || mesh.TriangleCount == 0) return 0;

        double minZ = double.MaxValue;
        double maxZ = double.MinValue;
        for (int i = 0; i < mesh.Positions.Length; i += 3)
        {
            double z = mesh.Positions[i + 2];
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }

        double height = maxZ - minZ;
        if (height <= 0.001) return 0;

        // Collect all edges that lie entirely on the maxZ plane (the top profile)
        var topEdges = new List<(Point3D p1, Point3D p2)>();

        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var v0 = GetVertex(mesh, mesh.Indices[i]);
            var v1 = GetVertex(mesh, mesh.Indices[i + 1]);
            var v2 = GetVertex(mesh, mesh.Indices[i + 2]);

            TryAddTopEdge(topEdges, v0, v1, maxZ);
            TryAddTopEdge(topEdges, v1, v2, maxZ);
            TryAddTopEdge(topEdges, v2, v0, maxZ);
        }

        // Se houver duplicação de arestas por causa da tesselação (por exemplo, a mesma aresta em triângulos co-planares ou desconectados)
        // O Teorema de Green lida naturalmente com isso SE soubermos a direção, mas como não sabemos a direção exata da borda "externa", 
        // ou se os triângulos estão apenas na vertical, a aresta no topo pertencerá a UM triângulo vertical.
        // Arestas de triângulos verticais no plano maxZ formarão o contorno.
        
        // Na verdade, uma forma incrivelmente robusta para malhas convexas ou cortadas é:
        // Shoelace na projeção de todas as arestas de topo no centroide:
        // Contudo, se pegarmos todos os triângulos, e calcularmos o Teorema da Divergência em 2D usando APENAS as arestas superiores.
        // Area += 0.5 * (p1.X * p2.Y - p2.X * p1.Y)
        double area = 0;
        foreach (var edge in topEdges)
        {
            // O sinal pode estar invertido se não soubermos a ordem do winding CCW vs CW.
            // Para resolver, basta somar todas as contribuições, o valor absoluto final será a área 
            // assumindo que os triângulos verticais do cilindro têm um winding consistente.
            area += (edge.p1.X * edge.p2.Y - edge.p2.X * edge.p1.Y) / 2.0;
        }
        
        return Math.Abs(area) * height;
    }

    private static void TryAddTopEdge(List<(Point3D, Point3D)> edges, Point3D a, Point3D b, double topZ)
    {
        // Se ambos os pontos estão no plano do topo
        if (Math.Abs(a.Z - topZ) < 1e-4 && Math.Abs(b.Z - topZ) < 1e-4)
        {
            // Para triângulos verticais formados por quadriculados, um lado da parede toca o teto.
            edges.Add((a, b));
        }
    }

    private static Point3D GetVertex(TriangleMesh mesh, uint index)
    {
        return new Point3D(
            mesh.Positions[index * 3],
            mesh.Positions[index * 3 + 1],
            mesh.Positions[index * 3 + 2]
        );
    }

    private struct Point3D
    {
        public double X, Y, Z;
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public static (double Min, double Max) GetBoundsX(TriangleMesh mesh)
    {
        if (mesh == null || mesh.TriangleCount == 0) return (0, 0);

        double minX = double.MaxValue;
        double maxX = double.MinValue;
        for (int i = 0; i < mesh.Positions.Length; i += 3)
        {
            double x = mesh.Positions[i];
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
        }
        return (minX, maxX);
    }
}
