using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Application.Services;

public class ShapeReconstructionService : IShapeReconstructionService
{
    private readonly IEnumerable<IReconstructionAlgorithm> _algorithms;
    private readonly IGeometryKernel _geometryKernel;

    public ShapeReconstructionService(IEnumerable<IReconstructionAlgorithm> algorithms, IGeometryKernel geometryKernel)
    {
        _algorithms = algorithms ?? Enumerable.Empty<IReconstructionAlgorithm>();
        _geometryKernel = geometryKernel;
    }

    public async Task<IFeature?> ReconstructMeshAsync(TriangleMesh mesh, string? preferredAlgorithmName = null)
    {
        if (mesh == null || mesh.TriangleCount == 0) return null;

        if (!string.IsNullOrEmpty(preferredAlgorithmName))
        {
            var algo = _algorithms.FirstOrDefault(a => a.Name == preferredAlgorithmName);
            if (algo != null)
            {
                var result = await algo.ReconstructAsync(mesh);
                return result?.RootFeature;
            }
        }

        double[] tolerances = new[] { 1.0, 0.5, 0.25 };
        ReconstructionResult? globalBestResult = null;

        foreach (var tolerance in tolerances)
        {
            // Roda todos os algoritmos em paralelo com a tolerância atual.
            // Algoritmos "multi-candidato" (ex.: varredura por eixo) contribuem com várias
            // hipóteses de uma só vez; os demais contribuem com uma única hipótese.
            var singleTasks = _algorithms
                .Where(a => a is not IMultiCandidateReconstructionAlgorithm)
                .Select(a => a.ReconstructAsync(mesh, tolerance))
                .ToList();
            var multiTasks = _algorithms
                .OfType<IMultiCandidateReconstructionAlgorithm>()
                .Select(a => a.ReconstructCandidatesAsync(mesh, tolerance))
                .ToList();

            await Task.WhenAll(singleTasks.Cast<Task>().Concat(multiTasks));

            var validResults = singleTasks.Select(t => t.Result)
                .Where(r => r != null)
                .Concat(multiTasks.SelectMany(t => t.Result).Where(r => r != null))
                .ToList();

            // Pós-processamento: Volumetria com Voxels (IoU - Intersection over Union)
            foreach (var result in validResults)
            {
                try
                {
                    var generatedMesh = _geometryKernel.BuildFeatureMesh(result!.RootFeature);
                    
                    if (generatedMesh != null && generatedMesh.Positions.Length > 0)
                    {
                        double iou = CalculateVolumetricIoU(mesh, generatedMesh);
                        result.ErrorMargin = 1.0 - iou; // Quanto menor o erro, maior o IoU
                    }
                    else
                    {
                        result.ErrorMargin = double.MaxValue;
                    }
                }
                catch
                {
                    result!.ErrorMargin = double.MaxValue;
                }
            }

            // Escolhe o melhor deste round
            var roundBest = validResults
                .Where(r => r!.ErrorMargin < 0.5) // Aceita no mínimo 50% de match volumétrico para não quebrar
                .OrderBy(r => r!.ErrorMargin)     // Prioridade 1: Maior IoU
                .ThenBy(r => r!.OperationCount)   // Desempate: Menos CSGs
                .FirstOrDefault();

            if (roundBest != null)
            {
                if (globalBestResult == null || roundBest.ErrorMargin < globalBestResult.ErrorMargin)
                {
                    globalBestResult = roundBest;
                }

                // Se alcançamos 99.9% de acurácia volumétrica (Erro <= 0.001), paramos de tentar!
                if (globalBestResult.ErrorMargin <= 0.001)
                {
                    break;
                }
            }
        }

        return globalBestResult?.RootFeature;
    }

    // Calcula a Acurácia Volumétrica por Intersection-Over-Union (Voxels)
    private double CalculateVolumetricIoU(TriangleMesh original, TriangleMesh generated)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < original.Positions.Length; i += 3)
        {
            float x = original.Positions[i], y = original.Positions[i+1], z = original.Positions[i+2];
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
        }
        
        float margin = System.Math.Max(maxX - minX, System.Math.Max(maxY - minY, maxZ - minZ)) * 0.05f;
        minX -= margin; maxX += margin;
        minY -= margin; maxY += margin;
        minZ -= margin; maxZ += margin;

        int resolution = 20; // 8000 Voxels
        float stepX = (maxX - minX) / resolution;
        float stepY = (maxY - minY) / resolution;
        float stepZ = (maxZ - minZ) / resolution;

        int intersection = 0;
        int union = 0;

        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int z = 0; z < resolution; z++)
                {
                    float px = minX + stepX * (x + 0.5f);
                    float py = minY + stepY * (y + 0.5f);
                    float pz = minZ + stepZ * (z + 0.5f);

                    bool inOriginal = IsPointInside(px, py, pz, original);
                    bool inGenerated = IsPointInside(px, py, pz, generated);

                    if (inOriginal || inGenerated) union++;
                    if (inOriginal && inGenerated) intersection++;
                }
            }
        }

        return union > 0 ? (double)intersection / union : 0.0;
    }

    // Raycasting algoritm (Möller-Trumbore) para Point-in-Mesh
    private bool IsPointInside(float px, float py, float pz, TriangleMesh mesh)
    {
        int intersections = 0;
        
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            int i0 = (int)mesh.Indices[i] * 3;
            int i1 = (int)mesh.Indices[i+1] * 3;
            int i2 = (int)mesh.Indices[i+2] * 3;
            
            float v0x = mesh.Positions[i0], v0y = mesh.Positions[i0+1], v0z = mesh.Positions[i0+2];
            float v1x = mesh.Positions[i1], v1y = mesh.Positions[i1+1], v1z = mesh.Positions[i1+2];
            float v2x = mesh.Positions[i2], v2y = mesh.Positions[i2+1], v2z = mesh.Positions[i2+2];
            
            float e1x = v1x - v0x, e1y = v1y - v0y, e1z = v1z - v0z;
            float e2x = v2x - v0x, e2y = v2y - v0y, e2z = v2z - v0z;
            
            // Ray D = (1, 0, 0)
            float hx = 0;
            float hy = -e2z;
            float hz = e2y;
            
            float a = e1x * hx + e1y * hy + e1z * hz;
            if (a > -1e-6f && a < 1e-6f) continue;
            
            float f = 1.0f / a;
            float sx = px - v0x;
            float sy = py - v0y;
            float sz = pz - v0z;
            
            float u = f * (sx * hx + sy * hy + sz * hz);
            if (u < 0.0f || u > 1.0f) continue;
            
            float qx = sy * e1z - sz * e1y;
            float qy = sz * e1x - sx * e1z;
            float qz = sx * e1y - sy * e1x;
            
            float v = f * qx;
            if (v < 0.0f || u + v > 1.0f) continue;
            
            float t = f * (e2x * qx + e2y * qy + e2z * qz);
            if (t > 1e-5f) intersections++;
        }
        
        return (intersections % 2) != 0;
    }
}
