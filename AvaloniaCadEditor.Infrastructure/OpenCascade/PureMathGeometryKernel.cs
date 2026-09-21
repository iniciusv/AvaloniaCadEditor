using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Infrastructure.OpenCascade;

public class PureMathGeometryKernel : IGeometryKernel
{
    public TriangleMesh BuildFeatureMesh(IFeature feature, TessellationSettings? settings = null)
    {
        settings ??= new TessellationSettings();
        if (feature is ExtrudeFeature extrude && extrude.IsEnabled)
        {
            return ExtrudeProfile(extrude.Profile, extrude.Direction, settings);
        }
        return TriangleMesh.Empty;
    }

    public TriangleMesh BuildSolidMesh(IEnumerable<IFeature> features, TessellationSettings? settings = null)
    {
        settings ??= new TessellationSettings();
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();
        var featureEdges = new List<uint>();
        var internalEdges = new List<uint>();

        uint vertexOffset = 0;

        foreach (var feature in features)
        {
            var mesh = BuildFeatureMesh(feature, settings);
            if (mesh.VertexCount == 0) continue;

            positions.AddRange(mesh.Positions);
            normals.AddRange(mesh.Normals);

            foreach (var idx in mesh.Indices)
            {
                indices.Add(idx + vertexOffset);
            }
            
            foreach (var fe in mesh.FeatureEdges)
            {
                featureEdges.Add(fe + vertexOffset);
            }
            
            foreach (var ie in mesh.InternalEdges)
            {
                internalEdges.Add(ie + vertexOffset);
            }

            vertexOffset += (uint)mesh.VertexCount;
        }

        return new TriangleMesh(positions.ToArray(), normals.ToArray(), indices.ToArray(), featureEdges.ToArray(), internalEdges.ToArray());
    }

    private static TriangleMesh ExtrudeProfile(IProfile profile, Vector3D direction, TessellationSettings settings)
    {
        var baseVertices = profile.GetVertices(settings.ProfileSegments);
        int n = baseVertices.Count;
        if (n < 3) return TriangleMesh.Empty;

        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();

        uint vOffset = 0;

        void AddTriangle(Point3D p1, Point3D p2, Point3D p3, Vector3D normal)
        {
            positions.Add((float)p1.X); positions.Add((float)p1.Y); positions.Add((float)p1.Z);
            positions.Add((float)p2.X); positions.Add((float)p2.Y); positions.Add((float)p2.Z);
            positions.Add((float)p3.X); positions.Add((float)p3.Y); positions.Add((float)p3.Z);

            normals.Add((float)normal.X); normals.Add((float)normal.Y); normals.Add((float)normal.Z);
            normals.Add((float)normal.X); normals.Add((float)normal.Y); normals.Add((float)normal.Z);
            normals.Add((float)normal.X); normals.Add((float)normal.Y); normals.Add((float)normal.Z);

            indices.Add(vOffset + 0);
            indices.Add(vOffset + 1);
            indices.Add(vOffset + 2);

            vOffset += 3;
        }

        // Calculate normal of extrusion direction
        var topNormal = direction.Normalize();
        var bottomNormal = new Vector3D(-topNormal.X, -topNormal.Y, -topNormal.Z);

        // 1. Bottom Cap (Facing downward / -Z)
        for (int i = 1; i < n - 1; i++)
        {
            AddTriangle(baseVertices[0], baseVertices[i + 1], baseVertices[i], bottomNormal);
        }

        // 2. Top Cap (Facing upward / +Z)
        for (int i = 1; i < n - 1; i++)
        {
            var p0 = baseVertices[0] + direction;
            var p1 = baseVertices[i] + direction;
            var p2 = baseVertices[i + 1] + direction;
            AddTriangle(p0, p1, p2, topNormal);
        }

        // 3. Side Walls (Quads split into 2 triangles)
        for (int i = 0; i < n; i++)
        {
            var b1 = baseVertices[i];
            var b2 = baseVertices[(i + 1) % n];
            var t1 = b1 + direction;
            var t2 = b2 + direction;

            var edgeVec = b2 - b1;
            var sideNormal = CrossProduct(edgeVec, direction).Normalize();

            // Triangle 1: b1 -> b2 -> t2
            AddTriangle(b1, b2, t2, sideNormal);
            // Triangle 2: b1 -> t2 -> t1
            AddTriangle(b1, t2, t1, sideNormal);
        }

        var posArr = positions.ToArray();
        var idxArr = indices.ToArray();
        var (featureEdges, internalEdges) = EdgeDetector.ClassifyEdges(posArr, idxArr, settings.CreaseAngle);

        return new TriangleMesh(posArr, normals.ToArray(), idxArr, featureEdges, internalEdges);
    }

    private static Vector3D CrossProduct(Vector3D a, Vector3D b)
    {
        return new Vector3D(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );
    }
}
