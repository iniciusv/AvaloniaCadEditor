using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Infrastructure.Engines;

public class RansacReconstructionAlgorithm : IReconstructionAlgorithm
{
    public string Name => "RANSAC / Amostragem Robusta (Fallback)";

    public Task<ReconstructionResult?> ReconstructAsync(TriangleMesh mesh, double tolerance = 1.0)
    {
        if (mesh == null || mesh.TriangleCount == 0) return Task.FromResult<ReconstructionResult?>(null);

        // Simulando um RANSAC para ignorar outliers topológicos (rebarbas, ruídos finos)
        var points = new List<Point3D>();
        for (int i = 0; i < mesh.Positions.Length; i += 3)
        {
            points.Add(new Point3D(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2]));
        }

        // Ordenando para encontrar percentis em X e Y, removendo 5% de outliers (ruído nas bordas)
        var orderedX = points.Select(p => p.X).OrderBy(x => x).ToList();
        var orderedY = points.Select(p => p.Y).OrderBy(y => y).ToList();
        var orderedZ = points.Select(p => p.Z).OrderBy(z => z).ToList();

        // 2% cutoff on each side for robust RANSAC-like bounds
        int cutoff = (int)(points.Count * 0.02 * tolerance); 
        
        double minX = orderedX[cutoff];
        double maxX = orderedX[orderedX.Count - 1 - cutoff];
        
        double minY = orderedY[cutoff];
        double maxY = orderedY[orderedY.Count - 1 - cutoff];
        
        double minZ = orderedZ[cutoff];
        double maxZ = orderedZ[orderedZ.Count - 1 - cutoff];

        var pts = new List<Point3D>
        {
            new Point3D(minX, minY, minZ),
            new Point3D(maxX, minY, minZ),
            new Point3D(maxX, maxY, minZ),
            new Point3D(minX, maxY, minZ)
        };
        
        var profile = new PolygonProfile(pts);
        double height = maxZ - minZ;
        if (height < 0.001) height = 0.001;

        var extrude = new ExtrudeFeature(profile, height, new Vector3D(0, 0, height), "Sólido Robusto (RANSAC)");

        return Task.FromResult<ReconstructionResult?>(new ReconstructionResult
        {
            RootFeature = extrude,
            ErrorMargin = 1.0, 
            OperationCount = 1
        });
    }
}
