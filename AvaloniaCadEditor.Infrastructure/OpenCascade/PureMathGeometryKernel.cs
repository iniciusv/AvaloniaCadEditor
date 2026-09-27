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
        else if (feature is PrismFeature prism && prism.IsEnabled)
        {
            var profile = new PolygonProfile(prism.Vertices, plane: prism.Plane);
            return ExtrudeProfile(profile, prism.Plane.Normal * prism.Height, settings);
        }
        return TriangleMesh.Empty;
    }

    public void InvalidateShape(System.Guid featureId) { }
    public void InvalidateAll() { }

    public TriangleMesh BuildSolidMesh(IEnumerable<IFeature> features, TessellationSettings? settings = null)
    {
        settings ??= new TessellationSettings();
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();
        var featureEdges = new List<uint>();
        var internalEdges = new List<uint>();
        var featureNormals1 = new List<float>();
        var featureNormals2 = new List<float>();

        uint vertexOffset = 0;

        foreach (var feature in features)
        {
            var mesh = BuildFeatureMesh(feature, settings);
            if (mesh.VertexCount == 0) continue;

            positions.AddRange(mesh.Positions);
            normals.AddRange(mesh.Normals);
            featureNormals1.AddRange(mesh.FeatureNormals1);
            featureNormals2.AddRange(mesh.FeatureNormals2);

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

        return new TriangleMesh(positions.ToArray(), normals.ToArray(), indices.ToArray(), featureEdges.ToArray(), internalEdges.ToArray(), featureNormals1.ToArray(), featureNormals2.ToArray());
    }

    private static TriangleMesh ExtrudeProfile(IProfile profile, Vector3D direction, TessellationSettings settings)
    {
        var baseVerticesReadOnly = profile.GetVertices(settings.ProfileSegments);
        int n = baseVerticesReadOnly.Count;
        if (n < 3) return TriangleMesh.Empty;

        var baseVertices = new List<Point3D>(baseVerticesReadOnly);

        // Calculate polygon normal using Newell's method to check winding order
        double nx = 0, ny = 0, nz = 0;
        for (int i = 0; i < n; i++)
        {
            var p1 = baseVertices[i];
            var p2 = baseVertices[(i + 1) % n];
            nx += (p1.Y - p2.Y) * (p1.Z + p2.Z);
            ny += (p1.Z - p2.Z) * (p1.X + p2.X);
            nz += (p1.X - p2.X) * (p1.Y + p2.Y);
        }
        
        // If Dot(polygonNormal, direction) < 0, vertices are clockwise relative to direction.
        // Reverse them so they become counter-clockwise, ensuring outward-facing side normals.
        if (nx * direction.X + ny * direction.Y + nz * direction.Z < 0)
        {
            baseVertices.Reverse();
        }

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
        var (featureEdges, internalEdges, n1, n2) = EdgeDetector.ClassifyEdges(posArr, idxArr, settings.CreaseAngle);
        var featureNormals1 = new float[n1.Length * 3];
        var featureNormals2 = new float[n2.Length * 3];
        for (int i = 0; i < n1.Length; i++) { featureNormals1[i*3] = n1[i].X; featureNormals1[i*3+1] = n1[i].Y; featureNormals1[i*3+2] = n1[i].Z; featureNormals2[i*3] = n2[i].X; featureNormals2[i*3+1] = n2[i].Y; featureNormals2[i*3+2] = n2[i].Z; }

        return new TriangleMesh(posArr, normals.ToArray(), idxArr, featureEdges, internalEdges, featureNormals1, featureNormals2);
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


