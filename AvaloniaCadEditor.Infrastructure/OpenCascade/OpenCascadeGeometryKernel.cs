using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using Occt;

namespace AvaloniaCadEditor.Infrastructure.OpenCascade;

public class OpenCascadeGeometryKernel : IGeometryKernel
{
    public OpenCascadeGeometryKernel()
    {
        OcctConfiguration.Configure();
    }

    public TriangleMesh BuildFeatureMesh(IFeature feature)
    {
        if (feature is ExtrudeFeature extrude && extrude.IsEnabled)
        {
            var shape = CreateExtrudedShape(extrude);
            if (shape != null)
            {
                return TessellateShape(shape);
            }
        }
        return TriangleMesh.Empty;
    }

    public TriangleMesh BuildSolidMesh(IEnumerable<IFeature> features)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();

        uint vertexOffset = 0;

        foreach (var feature in features)
        {
            var mesh = BuildFeatureMesh(feature);
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

    private TopoDS_Shape? CreateExtrudedShape(ExtrudeFeature extrude)
    {
        var profile = extrude.Profile;
        var dirVec = new gp_Vec(extrude.Direction.X, extrude.Direction.Y, extrude.Direction.Z);

        if (profile is RectangleProfile rect)
        {
            var vertices = rect.GetVertices();
            return MakePrismFromPolygon(vertices, dirVec);
        }
        else if (profile is CircleProfile circle)
        {
            var center = new gp_Pnt(circle.Center.X, circle.Center.Y, circle.Center.Z);
            var normal = new gp_Dir(circle.Normal.X, circle.Normal.Y, circle.Normal.Z);
            var axis = new gp_Ax2(center, normal);
            var occtCircle = new gp_Circ(axis, circle.Radius);

            var edgeMaker = new BRepBuilderAPI_MakeEdge(occtCircle);
            var wireMaker = new BRepBuilderAPI_MakeWire(edgeMaker.Edge);
            var faceMaker = new BRepBuilderAPI_MakeFace(wireMaker.Wire, false);

            var prismMaker = new BRepPrimAPI_MakePrism(faceMaker.Face, dirVec, false, true);
            return prismMaker.Shape;
        }
        else
        {
            var vertices = profile.GetVertices();
            return MakePrismFromPolygon(vertices, dirVec);
        }
    }

    private static TopoDS_Shape? MakePrismFromPolygon(IReadOnlyList<Point3D> vertices, gp_Vec dirVec)
    {
        if (vertices == null || vertices.Count < 3)
            return null;

        var poly = new BRepBuilderAPI_MakePolygon();
        foreach (var v in vertices)
        {
            poly.Add(new gp_Pnt(v.X, v.Y, v.Z));
        }
        poly.Close();

        var faceMaker = new BRepBuilderAPI_MakeFace(poly.Wire, false);
        if (!faceMaker.IsDone)
            return null;

        var prismMaker = new BRepPrimAPI_MakePrism(faceMaker.Face, dirVec, false, true);
        return prismMaker.Shape;
    }

    private static TriangleMesh TessellateShape(TopoDS_Shape shape, double deflection = 0.1)
    {
        // Mesh the shape
        var meshMaker = new BRepMesh_IncrementalMesh(shape, deflection, false, 0.5, true);

        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();

        var explorer = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_FACE, TopAbs_ShapeEnum.TopAbs_SHAPE);
        uint vertexOffset = 0;

        while (explorer.More)
        {
            var currentShape = explorer.Current;
            var face = (TopoDS_Face)currentShape;
            var location = new TopLoc_Location();
            var triangulation = BRep_Tool.Triangulation(face, out location);

            if (triangulation != null)
            {
                var trsf = location.Transformation;
                int nbNodes = triangulation.NbNodes;
                int nbTriangles = triangulation.NbTriangles;

                // Extract Nodes
                for (int i = 1; i <= nbNodes; i++)
                {
                    var pnt = triangulation.Node(i).Transformed(trsf);
                    positions.Add((float)pnt.X);
                    positions.Add((float)pnt.Y);
                    positions.Add((float)pnt.Z);

                    // Default upward/flat normal if face normals not computed
                    normals.Add(0f);
                    normals.Add(0f);
                    normals.Add(1f);
                }

                // Extract Triangles (1-based index in OpenCASCADE)
                for (int i = 1; i <= nbTriangles; i++)
                {
                    var triangle = triangulation.Triangle(i);
                    int n1 = 0, n2 = 0, n3 = 0;
                    triangle.Get(out n1, out n2, out n3);

                    indices.Add((uint)(n1 - 1) + vertexOffset);
                    indices.Add((uint)(n2 - 1) + vertexOffset);
                    indices.Add((uint)(n3 - 1) + vertexOffset);
                }

                vertexOffset += (uint)nbNodes;
            }

            explorer.Next();
        }

        return new TriangleMesh(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }
}
