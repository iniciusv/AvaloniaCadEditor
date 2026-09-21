using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using OcctNet.Wrapper;

namespace AvaloniaCadEditor.Infrastructure.OpenCascade;

public class OpenCascadeGeometryKernel : IGeometryKernel
{
    public TriangleMesh BuildFeatureMesh(IFeature feature)
    {
        try 
        {
            var shape = BuildShape(feature);
            if (shape == null || shape.IsNull)
                return TriangleMesh.Empty;

            var mesh = TessellateShape(shape);
            shape.Dispose();
            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BuildFeatureMesh Error] {ex}");
            return TriangleMesh.Empty;
        }
    }

    public TriangleMesh BuildSolidMesh(IEnumerable<IFeature> features)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();
        uint vertexOffset = 0;

        foreach (var f in features)
        {
            var mesh = BuildFeatureMesh(f);
            if (mesh.VertexCount == 0) continue;

            positions.AddRange(mesh.Positions);
            normals.AddRange(mesh.Normals);

            foreach (var idx in mesh.Indices)
            {
                indices.Add(idx + vertexOffset);
            }
            vertexOffset += (uint)mesh.VertexCount;
        }

        return new TriangleMesh(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    private OcctShape BuildShape(IFeature feature)
    {
        try {
            if (feature is ExtrudeFeature extrude && extrude.IsEnabled)
            {
                return BuildExtrude(extrude);
            }
            else if (feature is BooleanFeature boolean && boolean.IsEnabled)
            {
                return BuildBoolean(boolean);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BuildShape Error] {ex}");
        }
        return null;
    }

    private OcctShape BuildExtrude(ExtrudeFeature extrude)
    {
        var vertices = extrude.Profile.GetVertices();
        if (vertices == null || vertices.Count < 3) return null;

        var edges = new List<OcctEdge>();
        for (int i = 0; i < vertices.Count; i++)
        {
            var p1 = vertices[i];
            var p2 = vertices[(i + 1) % vertices.Count];
            var edge = new OcctEdge(
                new OcctPointCoordinates(p1.X, p1.Y, p1.Z),
                new OcctPointCoordinates(p2.X, p2.Y, p2.Z)
            );
            edges.Add(edge);
        }

        var wire = new OcctWire(edges.ToArray());
        var face = new OcctFace(wire);

        var dir = extrude.Direction;
        var vec = new OcctVector3d(dir.X, dir.Y, dir.Z);

        if (Math.Abs(extrude.Depth) < 1e-6) return face;

        return face.Extrude(vec);
    }
    
    private OcctShape BuildBoolean(BooleanFeature boolean)
    {
        var baseShape = BuildShape(boolean.BaseFeature);
        if (baseShape == null || baseShape.IsNull) return null;

        var toolShapes = boolean.ToolFeatures
                               .Select(BuildShape)
                               .Where(s => s != null && !s.IsNull)
                               .ToList();

        if (toolShapes.Count == 0) return baseShape;

        var currentShape = baseShape;

        foreach (var tool in toolShapes)
        {
            var newShape = boolean.Operation switch
            {
                BooleanOperationType.Union => currentShape.Fuse(tool),
                BooleanOperationType.Subtract => currentShape.Cut(tool),
                BooleanOperationType.Intersect => currentShape.Common(tool),
                _ => currentShape
            };

            if (newShape != null && !newShape.IsNull)
            {
                currentShape.Dispose();
                tool.Dispose();
                currentShape = newShape;
            }
        }

        return currentShape;
    }

    private TriangleMesh TessellateShape(OcctShape shape)
    {
        var occtMesh = shape.Triangulate(0.1, 0.5);
        if (occtMesh == null || occtMesh.TriangleCount == 0)
            return TriangleMesh.Empty;

        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();

        var verts = occtMesh.Vertices;
        var triIndices = occtMesh.TriangleIndices;

        // Initialize empty normal arrays
        var normalArrays = new float[verts.Count * 3];

        for (int i = 0; i < verts.Count; i++)
        {
            positions.Add((float)verts[i].X);
            positions.Add((float)verts[i].Y);
            positions.Add((float)verts[i].Z);
        }

        // Calculate flat normals since OcctMeshVertex doesn't provide them
        for (int i = 0; i < triIndices.Count; i += 3)
        {
            uint i1 = (uint)triIndices[i];
            uint i2 = (uint)triIndices[i + 1];
            uint i3 = (uint)triIndices[i + 2];

            indices.Add(i1);
            indices.Add(i2);
            indices.Add(i3);

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

            // Normalize
            var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0)
            {
                nx /= len;
                ny /= len;
                nz /= len;
            }

            // Add flat normal to the vertices of this triangle
            for (int j = 0; j < 3; j++)
            {
                int idx = (int)(j == 0 ? i1 : (j == 1 ? i2 : i3)) * 3;
                normalArrays[idx] += (float)nx;
                normalArrays[idx + 1] += (float)ny;
                normalArrays[idx + 2] += (float)nz;
            }
        }

        // Normalize accumulated normals
        for (int i = 0; i < normalArrays.Length; i += 3)
        {
            var nx = normalArrays[i];
            var ny = normalArrays[i + 1];
            var nz = normalArrays[i + 2];
            var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0)
            {
                normals.Add((float)(nx / len));
                normals.Add((float)(ny / len));
                normals.Add((float)(nz / len));
            }
            else
            {
                normals.Add(0f);
                normals.Add(0f);
                normals.Add(1f);
            }
        }

        return new TriangleMesh(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }
}
