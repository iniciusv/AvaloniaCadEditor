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

public class BoundingBoxHeuristicAlgorithm : IReconstructionAlgorithm
{
    public string Name => "Bounding Box (Fallback)";

    public Task<ReconstructionResult?> ReconstructAsync(TriangleMesh mesh, double tolerance = 1.0)
    {
        if (mesh == null || mesh.TriangleCount == 0) return Task.FromResult<ReconstructionResult?>(null);

        // Find global extremes
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        for (int i = 0; i < mesh.Positions.Length; i += 3)
        {
            float x = mesh.Positions[i];
            float y = mesh.Positions[i + 1];
            float z = mesh.Positions[i + 2];

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }

        // Create base rectangular profile
        var pts = new List<Point3D>
        {
            new Point3D(minX, minY, minZ),
            new Point3D(maxX, minY, minZ),
            new Point3D(maxX, maxY, minZ),
            new Point3D(minX, maxY, minZ)
        };
        var profile = new PolygonProfile(pts);

        double height = maxZ - minZ;
        if (height < 0.001) height = 0.001; // Prevent zero-thickness

        var extrude = new ExtrudeFeature(profile, height, new Vector3D(0, 0, height), "Bounding Box Global");

        return Task.FromResult<ReconstructionResult?>(new ReconstructionResult
        {
            RootFeature = extrude,
            ErrorMargin = 1.0, 
            OperationCount = 1
        });
    }
}
