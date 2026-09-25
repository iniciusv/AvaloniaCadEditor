using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Infrastructure.Engines;

public class BaseFaceExtrusionAlgorithm : IReconstructionAlgorithm
{
    public string Name => "Extrusão da Face Inferior (Topológica)";

    public async Task<ReconstructionResult?> ReconstructAsync(TriangleMesh mesh, double toleranceMultiplier = 1.0)
    {
        return await Task.Run(() =>
        {
            if (mesh == null || mesh.VertexCount == 0) return null;

            // 1. Encontra minZ e maxZ
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < mesh.Positions.Length; i += 3)
            {
                float z = mesh.Positions[i + 2];
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            double height = maxZ - minZ;
            if (height <= 0) return null;

            double zTolerance = 0.05 * toleranceMultiplier;

            // 2. Coleta todos os triângulos que formam a face inferior plana
            var bottomEdges = new Dictionary<(float, float, float, float), int>(); // Guarda arestas normalizadas
            var edgeNodes = new Dictionary<(float, float, float, float), ((float X, float Y) A, (float X, float Y) B)>();

            for (int i = 0; i < mesh.Indices.Length; i += 3)
            {
                int i1 = (int)mesh.Indices[i] * 3;
                int i2 = (int)mesh.Indices[i + 1] * 3;
                int i3 = (int)mesh.Indices[i + 2] * 3;

                float z1 = mesh.Positions[i1 + 2];
                float z2 = mesh.Positions[i2 + 2];
                float z3 = mesh.Positions[i3 + 2];

                // Se o triângulo inteiro estiver colado no fundo
                if (Math.Abs(z1 - minZ) <= zTolerance && Math.Abs(z2 - minZ) <= zTolerance && Math.Abs(z3 - minZ) <= zTolerance)
                {
                    var p1 = (X: mesh.Positions[i1], Y: mesh.Positions[i1 + 1]);
                    var p2 = (X: mesh.Positions[i2], Y: mesh.Positions[i2 + 1]);
                    var p3 = (X: mesh.Positions[i3], Y: mesh.Positions[i3 + 1]);

                    AddEdge(bottomEdges, edgeNodes, p1, p2);
                    AddEdge(bottomEdges, edgeNodes, p2, p3);
                    AddEdge(bottomEdges, edgeNodes, p3, p1);
                }
            }

            // 3. Filtra apenas as arestas de borda (aquelas que pertencem a apenas 1 triângulo)
            var boundaryEdges = new List<((float X, float Y) A, (float X, float Y) B)>();
            foreach (var kvp in bottomEdges)
            {
                if (kvp.Value == 1) // Aresta de borda (não compartilhada)
                {
                    boundaryEdges.Add(edgeNodes[kvp.Key]);
                }
            }

            if (boundaryEdges.Count == 0) return null;

            // 4. Conecta as arestas para formar o loop contínuo
            var loops = new List<List<Point3D>>();
            var used = new bool[boundaryEdges.Count];
            double epsilonSq = 1e-4;

            for (int i = 0; i < boundaryEdges.Count; i++)
            {
                if (used[i]) continue;
                
                var loop = new List<Point3D> { new Point3D(boundaryEdges[i].A.X, boundaryEdges[i].A.Y, minZ) };
                var currentPt = boundaryEdges[i].B;
                used[i] = true;
                bool closed = false;

                while (!closed)
                {
                    loop.Add(new Point3D(currentPt.X, currentPt.Y, minZ));
                    bool foundNext = false;

                    for (int j = 0; j < boundaryEdges.Count; j++)
                    {
                        if (used[j]) continue;

                        double d1 = DistSq(boundaryEdges[j].A, currentPt);
                        double d2 = DistSq(boundaryEdges[j].B, currentPt);

                        if (d1 < epsilonSq)
                        {
                            currentPt = boundaryEdges[j].B;
                            used[j] = true;
                            foundNext = true;
                            break;
                        }
                        else if (d2 < epsilonSq)
                        {
                            currentPt = boundaryEdges[j].A;
                            used[j] = true;
                            foundNext = true;
                            break;
                        }
                    }

                    if (!foundNext) break;
                    
                    if (DistSq(((float)loop[0].X, (float)loop[0].Y), currentPt) < epsilonSq)
                    {
                        closed = true;
                    }
                }
                
                if (loop.Count >= 3) loops.Add(loop);
            }

            if (loops.Count == 0) return null;

            // 5. Escolhe o loop principal (o de maior área/bounding box) como sendo a Borda Externa
            List<Point3D>? bestLoop = null;
            double maxArea = -1;
            foreach (var loop in loops)
            {
                double lMinX = loop.Min(p => p.X), lMaxX = loop.Max(p => p.X);
                double lMinY = loop.Min(p => p.Y), lMaxY = loop.Max(p => p.Y);
                double area = (lMaxX - lMinX) * (lMaxY - lMinY);
                if (area > maxArea) { maxArea = area; bestLoop = loop; }
            }

            if (bestLoop == null) return null;

            // 6. Cria a Feature de Extrusão da Borda Externa
            var simplifiedLoop = SimplifyLoop(bestLoop, minZ);
            var profile = new AvaloniaCadEditor.Domain.Profiles.PolygonProfile(simplifiedLoop);
            IFeature rootFeature = new ExtrudeFeature(profile, height, new Vector3D(0, 0, height), "Extrusão do Contorno Base");

            int opCount = 1;

            // 7. Todos os OUTROS loops topológicos encontrados na base plana são furos (Buracos)
            var toolFeatures = new List<IFeature>();
            
            foreach (var loop in loops)
            {
                if (ReferenceEquals(loop, bestLoop)) continue; 
                
                var simplifiedHole = SimplifyLoop(loop, minZ - 1.0f); 
                if (simplifiedHole.Count < 3) continue;

                // Tenta fazer o Ajuste Matemático de Círculo (Least-Squares Circle Fit)
                var circleFit = FitCircle(simplifiedHole);
                if (circleFit.Radius > 0 && circleFit.Error < circleFit.Radius * 0.05) // Margem de erro de 5%
                {
                    var holeExtrusion = new CylinderFeature(
                        new Point3D(circleFit.CenterX, circleFit.CenterY, minZ - 1.0),
                        new Vector3D(0, 0, 1),
                        circleFit.Radius,
                        height + 2.0,
                        $"Furo Cilíndrico Deduzido {toolFeatures.Count + 1}");
                    toolFeatures.Add(holeExtrusion);
                }
                else
                {
                    var holeProfile = new AvaloniaCadEditor.Domain.Profiles.PolygonProfile(simplifiedHole);
                    var holeExtrusion = new ExtrudeFeature(holeProfile, height + 2.0, new Vector3D(0, 0, height + 2.0), $"Furo Poligonal {toolFeatures.Count + 1}");
                    toolFeatures.Add(holeExtrusion);
                }
            }

            if (toolFeatures.Count > 0)
            {
                opCount += toolFeatures.Count;
                rootFeature = BooleanFeature.CreateDifference("Base Extrudada com Furos", rootFeature, toolFeatures.ToArray());
            }

            return new ReconstructionResult { RootFeature = rootFeature, OperationCount = opCount, ErrorMargin = 0.0001 };
        });
    }

    // Método de Kåsa para Least-Squares Circle Fit (https://en.wikipedia.org/wiki/Circle_fitting)
    private (double CenterX, double CenterY, double Radius, double Error) FitCircle(List<Point3D> points)
    {
        int n = points.Count;
        if (n < 3) return (0, 0, 0, double.MaxValue);

        double sx = 0.0, sy = 0.0;
        double sxx = 0.0, syy = 0.0, sxy = 0.0;
        double sxxx = 0.0, syyy = 0.0, sxxy = 0.0, sxyy = 0.0;

        foreach (var p in points)
        {
            double x = p.X;
            double y = p.Y;
            sx += x; sy += y;
            sxx += x * x; syy += y * y; sxy += x * y;
            sxxx += x * x * x; syyy += y * y * y;
            sxxy += x * x * y; sxyy += x * y * y;
        }

        double c1 = n * sxx - sx * sx;
        double c2 = n * sxy - sx * sy;
        double c3 = n * syy - sy * sy;
        double d1 = 0.5 * (n * sxxx + n * sxyy - sxx * sx - syy * sx);
        double d2 = 0.5 * (n * syyy + n * sxxy - syy * sy - sxx * sy);

        double det = c1 * c3 - c2 * c2;
        if (Math.Abs(det) < 1e-10) return (0, 0, 0, double.MaxValue); // Colineares (Não é círculo)

        double a = (d1 * c3 - d2 * c2) / det;
        double b = (c1 * d2 - c2 * d1) / det;
        double rSq = (sxx - 2 * a * sx + n * a * a + syy - 2 * b * sy + n * b * b) / n;
        double radius = Math.Sqrt(rSq);

        // Calcula o erro médio quadrático em relação ao raio deduzido
        double errorSqSum = 0;
        foreach (var p in points)
        {
            double d = Math.Sqrt((p.X - a) * (p.X - a) + (p.Y - b) * (p.Y - b));
            errorSqSum += (d - radius) * (d - radius);
        }
        double avgError = Math.Sqrt(errorSqSum / n);

        return (a, b, radius, avgError);
    }

    private List<Point3D> SimplifyLoop(List<Point3D> originalLoop, float targetZ)
    {
        var simplifiedLoop = new List<Point3D> { new Point3D(originalLoop[0].X, originalLoop[0].Y, targetZ) };
        for(int i = 1; i < originalLoop.Count; i++)
        {
             var last = simplifiedLoop.Last();
             if (DistSq(new((float)originalLoop[i].X, (float)originalLoop[i].Y), new((float)last.X, (float)last.Y)) > 0.05)
                simplifiedLoop.Add(new Point3D(originalLoop[i].X, originalLoop[i].Y, targetZ));
        }
        return simplifiedLoop;
    }

    private void AddEdge(Dictionary<(float, float, float, float), int> edgeCounts, 
                         Dictionary<(float, float, float, float), ((float, float), (float, float))> edgeNodes,
                         (float X, float Y) p1, (float X, float Y) p2)
    {
        // Chave única invariante à ordem (xMin, yMin, xMax, yMax arredondados para evitar erro de float)
        float rx1 = (float)Math.Round(p1.X, 3);
        float ry1 = (float)Math.Round(p1.Y, 3);
        float rx2 = (float)Math.Round(p2.X, 3);
        float ry2 = (float)Math.Round(p2.Y, 3);

        (float, float, float, float) key;
        if (rx1 < rx2 || (rx1 == rx2 && ry1 < ry2))
            key = (rx1, ry1, rx2, ry2);
        else
            key = (rx2, ry2, rx1, ry1);

        if (edgeCounts.ContainsKey(key))
            edgeCounts[key]++;
        else
        {
            edgeCounts[key] = 1;
            edgeNodes[key] = (p1, p2);
        }
    }

    private double DistSq((float X, float Y) a, (float X, float Y) b) 
        => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
