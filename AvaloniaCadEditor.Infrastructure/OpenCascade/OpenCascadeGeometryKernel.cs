using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;
using OcctNet.Wrapper;

namespace AvaloniaCadEditor.Infrastructure.OpenCascade;

public class OpenCascadeGeometryKernel : IGeometryKernel
{
    private readonly Dictionary<Guid, OcctShape> _shapeCache = new();

    public void InvalidateShape(Guid featureId)
    {
        if (_shapeCache.TryGetValue(featureId, out var cached))
        {
            cached.Dispose();
            _shapeCache.Remove(featureId);
        }
    }

    public void InvalidateAll()
    {
        foreach (var shape in _shapeCache.Values)
        {
            shape?.Dispose();
        }
        _shapeCache.Clear();
    }

    public TriangleMesh BuildFeatureMesh(IFeature feature, TessellationSettings? settings = null)
    {
        if (feature is ImportedMeshFeature imported)
        {
            return imported.Mesh;
        }

        settings ??= new TessellationSettings();
        try 
        {
            var shape = BuildShape(feature, settings);
            if (shape == null || shape.IsNull)
                return TriangleMesh.Empty;

            var mesh = TessellateShape(shape, settings);
            // shape.Dispose(); // Shape is now cached, do not dispose here
            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BuildFeatureMesh Error] {ex}");
            return TriangleMesh.Empty;
        }
    }

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

        foreach (var f in features)
        {
            var mesh = BuildFeatureMesh(f, settings);
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

    private OcctShape BuildShape(IFeature feature, TessellationSettings settings)
    {
        if (_shapeCache.TryGetValue(feature.Id, out var cached) && cached != null && !cached.IsNull)
        {
            return cached;
        }

        OcctShape result = null;
        try {
            if (feature is ExtrudeFeature extrude && extrude.IsEnabled)
            {
                result = BuildExtrude(extrude, settings);
            }
            else if (feature is CylinderFeature cyl && cyl.IsEnabled)
            {
                result = BuildCylinder(cyl, settings);
            }
            else if (feature is BoxFeature box && box.IsEnabled)
            {
                result = BuildBox(box, settings);
            }
            else if (feature is PrismFeature prism && prism.IsEnabled)
            {
                result = BuildPrism(prism, settings);
            }
            else if (feature is BooleanFeature boolean && boolean.IsEnabled)
            {
                result = BuildBoolean(boolean, settings);
            }
            else if (feature is FilletFeature fillet && fillet.IsEnabled)
            {
                result = BuildFillet(fillet, settings);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BuildShape Error] {ex}");
        }

        if (result != null && !result.IsNull)
        {
            _shapeCache[feature.Id] = result;
        }

        return result;
    }

    private OcctShape CreateCylinderCutter(CylinderFeature cyl, double filletRadius, bool isTop, bool isRound, TessellationSettings settings)
    {
        var origin = cyl.GlobalBaseCenter;
        var dir = cyl.GlobalDirection;
        
        Vector3D u;
        if (Math.Abs(dir.X) > 0.1 || Math.Abs(dir.Y) > 0.1)
            u = new Vector3D(-dir.Y, dir.X, 0);
        else
            u = new Vector3D(1, 0, 0);
            
        double uLen = Math.Sqrt(u.X * u.X + u.Y * u.Y + u.Z * u.Z);
        u = new Vector3D(u.X / uLen, u.Y / uLen, u.Z / uLen);
        
        var points2D = new System.Collections.Generic.List<(double r, double z)>();
        double R = cyl.Radius;
        double H = cyl.Height;
        double r = filletRadius;
        
        if (isTop)
        {
            points2D.Add((R - r, H + 0.1));
            points2D.Add((R + 0.1, H + 0.1));
            points2D.Add((R + 0.1, H - r));
            
            if (isRound)
            {
                int segments = settings.ProfileSegments > 0 ? settings.ProfileSegments / 4 : 8;
                if (segments < 2) segments = 2;
                for (int i = 0; i <= segments; i++)
                {
                    double theta = (Math.PI / 2.0) * (1.0 - (double)i / segments);
                    double pr = (R - r) + r * Math.Cos(theta);
                    double pz = (H - r) + r * Math.Sin(theta);
                    points2D.Add((pr, pz));
                }
            }
            else
            {
                points2D.Add((R, H - r));
                points2D.Add((R - r, H));
            }
        }
        else
        {
            points2D.Add((R - r, -0.1));
            points2D.Add((R + 0.1, -0.1));
            points2D.Add((R + 0.1, r));
            
            if (isRound)
            {
                int segments = settings.ProfileSegments > 0 ? settings.ProfileSegments / 4 : 8;
                if (segments < 2) segments = 2;
                for (int i = 0; i <= segments; i++)
                {
                    double theta = (Math.PI / 2.0) * ((double)i / segments);
                    double pr = (R - r) + r * Math.Cos(theta);
                    double pz = r - r * Math.Sin(theta);
                    points2D.Add((pr, pz));
                }
            }
            else
            {
                points2D.Add((R, r));
                points2D.Add((R - r, 0));
            }
        }
        
        var vertices = new System.Collections.Generic.List<Point3D>();
        foreach (var p in points2D)
        {
            vertices.Add(new Point3D(
                origin.X + u.X * p.r + dir.X * p.z,
                origin.Y + u.Y * p.r + dir.Y * p.z,
                origin.Z + u.Z * p.r + dir.Z * p.z
            ));
        }
        
        // Calculate polygon normal using Newell's method to check winding order
        double nx = 0, ny = 0, nz = 0;
        int n = vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var p1 = vertices[i];
            var p2 = vertices[(i + 1) % n];
            nx += (p1.Y - p2.Y) * (p1.Z + p2.Z);
            ny += (p1.Z - p2.Z) * (p1.X + p2.X);
            nz += (p1.X - p2.X) * (p1.Y + p2.Y);
        }
        
        // Cross product of u and dir gives the normal of the plane
        var expectedNormal = new Vector3D(
            u.Y * dir.Z - u.Z * dir.Y,
            u.Z * dir.X - u.X * dir.Z,
            u.X * dir.Y - u.Y * dir.X
        );
        
        if (nx * expectedNormal.X + ny * expectedNormal.Y + nz * expectedNormal.Z < 0)
        {
            vertices.Reverse();
        }
        
        var edges = new System.Collections.Generic.List<OcctEdge>();
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
        
        var axis = new OcctAxis1d(
            new OcctPointCoordinates(origin.X, origin.Y, origin.Z),
            new OcctVector3d(dir.X, dir.Y, dir.Z)
        );
        
        return face.Revolve(axis, 2.0 * Math.PI);
    }

    private OcctShape BuildFillet(FilletFeature fillet, TessellationSettings settings)
    {
        var currentShape = BuildShape(fillet.BaseFeature, settings);
        if (currentShape == null || currentShape.IsNull) return null;

        if (fillet.BaseFeature is CylinderFeature cyl)
        {
            var rad = fillet.Radius;
            if (rad <= 0.001) return currentShape;
            
            bool doTop = fillet.ApplyToAllEdges || fillet.Faces.Any(f => f.FaceId == (int)FaceType.Top);
            bool doBottom = fillet.ApplyToAllEdges || fillet.Faces.Any(f => f.FaceId == (int)FaceType.Bottom);
            
            bool isRound = fillet.Mode == FilletMode.Round;
            var resultShape = currentShape;
            
            if (doTop)
            {
                var cutter = CreateCylinderCutter(cyl, rad, true, isRound, settings);
                if (cutter != null && !cutter.IsNull) resultShape = resultShape.Cut(cutter);
            }
            if (doBottom)
            {
                var cutter = CreateCylinderCutter(cyl, rad, false, isRound, settings);
                if (cutter != null && !cutter.IsNull) resultShape = resultShape.Cut(cutter);
            }
            
            return resultShape;
        }

        var allEdges = new HashSet<EdgeReference>();
        var r = fillet.Radius;
        if (r <= 0.001) return currentShape;
        int segments = settings.ProfileSegments > 0 ? settings.ProfileSegments / 4 : 8;
        if (segments < 2) segments = 2;
        bool isIntermediate = false;

        // --- MESH-BASED GENERALIZED FILLET FOR ANY FEATURE (ESPECIALLY BOOLEANS) ---
        if (!(fillet.BaseFeature is BoxFeature))
        {
            var mesh = TessellateShape(currentShape, settings);
            if (mesh != null && mesh.FeatureEdges.Length > 0)
            {
                var targetNormals = fillet.ApplyToAllEdges ? new List<Vector3D>() : fillet.Faces.Select(f => f.NormalHint).ToList();
                for (int i = 0; i < mesh.FeatureEdges.Length; i += 2)
                {
                    uint idx1 = mesh.FeatureEdges[i];
                    uint idx2 = mesh.FeatureEdges[i + 1];
                    var start = new Point3D(mesh.Positions[idx1 * 3], mesh.Positions[idx1 * 3 + 1], mesh.Positions[idx1 * 3 + 2]);
                    var end = new Point3D(mesh.Positions[idx2 * 3], mesh.Positions[idx2 * 3 + 1], mesh.Positions[idx2 * 3 + 2]);
                    
                    var n1 = new Vector3D(mesh.FeatureNormals1[(i/2)*3], mesh.FeatureNormals1[(i/2)*3+1], mesh.FeatureNormals1[(i/2)*3+2]);
                    var n2 = new Vector3D(mesh.FeatureNormals2[(i/2)*3], mesh.FeatureNormals2[(i/2)*3+1], mesh.FeatureNormals2[(i/2)*3+2]);

                    // Filter by selected faces if applicable
                    if (!fillet.ApplyToAllEdges && targetNormals.Count > 0)
                    {
                        bool match = false;
                        foreach (var tn in targetNormals)
                        {
                            if (tn.DotProduct(n1) > 0.99 || tn.DotProduct(n2) > 0.99)
                            {
                                match = true;
                                break;
                            }
                        }
                        if (!match) continue;
                    }

                    var vEdge = new Vector3D(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
                    double len = Math.Sqrt(vEdge.X * vEdge.X + vEdge.Y * vEdge.Y + vEdge.Z * vEdge.Z);
                    if (len < 1e-5) continue;
                    var dEdge = new Vector3D(vEdge.X / len, vEdge.Y / len, vEdge.Z / len);
                    var extStart = new Point3D(start.X - dEdge.X * 0.1 * r, start.Y - dEdge.Y * 0.1 * r, start.Z - dEdge.Z * 0.1 * r);
                    double extLen = len + 0.2 * r;

                    var profilePoints = new List<Point3D>();
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0.1 * r, 0.1 * r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, -r, 0.1 * r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, -r, 0));

                    if (fillet.Mode == FilletMode.Round)
                    {
                        for (int k = 1; k < segments; k++)
                        {
                            double theta = (Math.PI / 2.0) * (1.0 - (double)k / segments);
                            double x = -r + r * Math.Cos(theta);
                            double y = -r + r * Math.Sin(theta);
                            profilePoints.Add(Get3DPoint(extStart, n1, n2, x, y));
                        }
                    }

                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0, -r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0.1 * r, -r));

                    var edges = new List<OcctEdge>();
                    for (int k = 0; k < profilePoints.Count; k++)
                    {
                        var p1 = profilePoints[k];
                        var p2 = profilePoints[(k + 1) % profilePoints.Count];
                        edges.Add(new OcctEdge(
                            new OcctPointCoordinates(p1.X, p1.Y, p1.Z),
                            new OcctPointCoordinates(p2.X, p2.Y, p2.Z)
                        ));
                    }

                    var wire = new OcctWire(edges.ToArray());
                    var face = new OcctFace(wire);
                    var cutter = face.Extrude(new OcctVector3d(dEdge.X * extLen, dEdge.Y * extLen, dEdge.Z * extLen));

                    if (cutter != null && !cutter.IsNull)
                    {
                        var cutShape = currentShape.Cut(cutter);
                        if (cutShape != null && !cutShape.IsNull)
                        {
                            if (isIntermediate) currentShape.Dispose();
                            cutter.Dispose();
                            currentShape = cutShape;
                            isIntermediate = true;
                        }
                    }
                }
            }
        }
        else
        {
            // --- BOX FEATURE EXACT FILLET ---
            if (fillet.ApplyToAllEdges)
            {
                if (fillet.BaseFeature is BoxFeature box)
                {
                    foreach (var eType in box.GetAllEdges()) allEdges.Add(new EdgeReference(box, eType));
                }
            }
            else
            {
                foreach (var edge in fillet.Edges) allEdges.Add(edge);
                foreach (var face in fillet.Faces)
                {
                    if (face.ParentFeature is BoxFeature box && Enum.IsDefined(typeof(FaceType), face.FaceId))
                    {
                        var fType = (FaceType)face.FaceId;
                        foreach (var eType in box.GetEdgesForFace(fType)) allEdges.Add(new EdgeReference(box, eType));
                    }
                }
            }

            foreach (var edgeRef in allEdges)
            {
                if (edgeRef.ParentFeature is BoxFeature box)
                {
                    var geom = box.GetEdgeGeometry(edgeRef.EdgeId);
                    var start = geom.Start;
                    var end = geom.End;
                    var n1 = geom.Normal1;
                    var n2 = geom.Normal2;

                    var vEdge = new Vector3D(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
                    double len = Math.Sqrt(vEdge.X * vEdge.X + vEdge.Y * vEdge.Y + vEdge.Z * vEdge.Z);
                    if (len < 1e-5) continue;
                    var dEdge = new Vector3D(vEdge.X / len, vEdge.Y / len, vEdge.Z / len);
                    var extStart = new Point3D(start.X - dEdge.X * 0.1 * r, start.Y - dEdge.Y * 0.1 * r, start.Z - dEdge.Z * 0.1 * r);
                    double extLen = len + 0.2 * r;

                    var profilePoints = new List<Point3D>();
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0.1 * r, 0.1 * r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, -r, 0.1 * r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, -r, 0));

                    if (fillet.Mode == FilletMode.Round)
                    {
                        for (int i = 1; i < segments; i++)
                        {
                            double theta = (Math.PI / 2.0) * (1.0 - (double)i / segments);
                            double x = -r + r * Math.Cos(theta);
                            double y = -r + r * Math.Sin(theta);
                            profilePoints.Add(Get3DPoint(extStart, n1, n2, x, y));
                        }
                    }

                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0, -r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0.1 * r, -r));

                    var edges = new List<OcctEdge>();
                    for (int i = 0; i < profilePoints.Count; i++)
                    {
                        var p1 = profilePoints[i];
                        var p2 = profilePoints[(i + 1) % profilePoints.Count];
                        edges.Add(new OcctEdge(
                            new OcctPointCoordinates(p1.X, p1.Y, p1.Z),
                            new OcctPointCoordinates(p2.X, p2.Y, p2.Z)
                        ));
                    }

                    var wire = new OcctWire(edges.ToArray());
                    var face = new OcctFace(wire);
                    var cutter = face.Extrude(new OcctVector3d(dEdge.X * extLen, dEdge.Y * extLen, dEdge.Z * extLen));

                    if (cutter != null && !cutter.IsNull)
                    {
                        var cutShape = currentShape.Cut(cutter);
                        if (cutShape != null && !cutShape.IsNull)
                        {
                            if (isIntermediate) currentShape.Dispose();
                            cutter.Dispose();
                            currentShape = cutShape;
                            isIntermediate = true;
                        }
                    }
                }
            }
        }

        return currentShape;
    }

    private Point3D Get3DPoint(Point3D origin, Vector3D n1, Vector3D n2, double x, double y)
    {
        return new Point3D(
            origin.X + x * n1.X + y * n2.X,
            origin.Y + x * n1.Y + y * n2.Y,
            origin.Z + x * n1.Z + y * n2.Z
        );
    }

    private OcctShape BuildCylinder(CylinderFeature cyl, TessellationSettings settings)
    {
        var plane = new WorkPlane(cyl.GlobalDirection, cyl.GlobalBaseCenter);
        var profile = new AvaloniaCadEditor.Domain.Profiles.CircleProfile(Point3D.Origin, cyl.Radius, plane: plane);
        var extrude = new ExtrudeFeature(profile, cyl.Height, cyl.GlobalDirection * cyl.Height);
        return BuildExtrude(extrude, settings);
    }

    private OcctShape BuildBox(BoxFeature box, TessellationSettings settings)
    {
        Point3D bottomCenter = new Point3D(
            box.BaseCorner.X + box.DirectionX.X * (box.Width / 2.0) + box.DirectionY.X * (box.Length / 2.0),
            box.BaseCorner.Y + box.DirectionX.Y * (box.Width / 2.0) + box.DirectionY.Y * (box.Length / 2.0),
            box.BaseCorner.Z + box.DirectionX.Z * (box.Width / 2.0) + box.DirectionY.Z * (box.Length / 2.0)
        );
        var plane = new WorkPlane(bottomCenter, box.DirectionZ, box.DirectionX);
        var profile = new AvaloniaCadEditor.Domain.Profiles.RectangleProfile(Point3D.Origin, box.Width, box.Length, 0.0, box.DirectionZ, null, plane);
        var extrude = new ExtrudeFeature(profile, box.Height, box.DirectionZ * box.Height);
        return BuildExtrude(extrude, settings);
    }

    private OcctShape BuildPrism(PrismFeature prism, TessellationSettings settings)
    {
        var profile = new AvaloniaCadEditor.Domain.Profiles.PolygonProfile(prism.Vertices, plane: prism.Plane);
        var extrude = new ExtrudeFeature(profile, prism.Height, prism.Plane.Normal * prism.Height);
        return BuildExtrude(extrude, settings);
    }

    private OcctShape BuildExtrude(ExtrudeFeature extrude, TessellationSettings settings)
    {
        var verticesReadOnly = extrude.Profile.GetVertices(settings.ProfileSegments);
        if (verticesReadOnly == null || verticesReadOnly.Count < 3) return null;

        var vertices = new List<Point3D>(verticesReadOnly);

        var dir = extrude.Direction;
        var vec = new OcctVector3d(dir.X, dir.Y, dir.Z);

        // Calculate polygon normal using Newell's method to check winding order
        double nx = 0, ny = 0, nz = 0;
        int n = vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var p1 = vertices[i];
            var p2 = vertices[(i + 1) % n];
            nx += (p1.Y - p2.Y) * (p1.Z + p2.Z);
            ny += (p1.Z - p2.Z) * (p1.X + p2.X);
            nz += (p1.X - p2.X) * (p1.Y + p2.Y);
        }
        
        // If Dot(polygonNormal, direction) < 0, vertices are clockwise relative to direction.
        if (nx * dir.X + ny * dir.Y + nz * dir.Z < 0)
        {
            vertices.Reverse();
        }

        // Remove consecutive duplicates just in case
        for (int i = vertices.Count - 1; i > 0; i--)
        {
            if (vertices[i].DistanceTo(vertices[i - 1]) < 1e-5)
            {
                vertices.RemoveAt(i);
            }
        }
        if (vertices.Count > 1 && vertices[0].DistanceTo(vertices.Last()) < 1e-5)
        {
            vertices.RemoveAt(vertices.Count - 1);
        }

        if (vertices.Count < 3) return null;

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

        if (Math.Abs(extrude.Height) < 1e-6) return face;

        return face.Extrude(vec);
    }
    
    private OcctShape BuildBoolean(BooleanFeature boolean, TessellationSettings settings)
    {
        var baseShape = BuildShape(boolean.BaseFeature, settings);
        if (baseShape == null || baseShape.IsNull) return null;

        var toolShapes = boolean.ToolFeatures
                               .Select(f => BuildShape(f, settings))
                               .Where(s => s != null && !s.IsNull)
                               .ToList();

        if (toolShapes.Count == 0) return baseShape;

        var currentShape = baseShape;
        bool isIntermediate = false;

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
                if (isIntermediate)
                {
                    currentShape.Dispose();
                }
                currentShape = newShape;
                isIntermediate = true;
            }
        }

        return currentShape;
    }

    private TriangleMesh TessellateShape(OcctShape shape, TessellationSettings settings)
    {
        var occtMesh = shape.Triangulate(settings.LinearDeflection, settings.AngularDeflection);
        if (occtMesh == null || occtMesh.TriangleCount == 0)
            return TriangleMesh.Empty;

        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();

        var verts = occtMesh.Vertices;
        var triIndices = occtMesh.TriangleIndices;

        var normalArrays = new float[verts.Count * 3];

        for (int i = 0; i < verts.Count; i++)
        {
            positions.Add((float)verts[i].X);
            positions.Add((float)verts[i].Y);
            positions.Add((float)verts[i].Z);
        }

        double cx = 0, cy = 0, cz = 0;
        if (verts.Count > 0)
        {
            for (int i = 0; i < verts.Count; i++)
            {
                cx += verts[i].X;
                cy += verts[i].Y;
                cz += verts[i].Z;
            }
            cx /= verts.Count;
            cy /= verts.Count;
            cz /= verts.Count;
        }

        // Calculate flat normals since OcctMeshVertex doesn't provide them
        for (int i = 0; i < triIndices.Count; i += 3)
        {
            uint i1 = (uint)triIndices[i];
            uint i2 = (uint)triIndices[i + 1];
            uint i3 = (uint)triIndices[i + 2];

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

            // Centroid heuristic: normal should point away from the solid's center
            var tcx = (p1.X + p2.X + p3.X) / 3.0 - cx;
            var tcy = (p1.Y + p2.Y + p3.Y) / 3.0 - cy;
            var tcz = (p1.Z + p2.Z + p3.Z) / 3.0 - cz;
            
            if (nx * tcx + ny * tcy + nz * tcz < 0)
            {
                nx = -nx;
                ny = -ny;
                nz = -nz;
                // Swap i2 and i3 to fix winding order
                uint temp = i2;
                i2 = i3;
                i3 = temp;
            }

            indices.Add(i1);
            indices.Add(i2);
            indices.Add(i3);

            // Normalize face normal
            var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0)
            {
                nx /= len;
                ny /= len;
                nz /= len;
            }

            // Accumulate flat normals
            for (int j = 0; j < 3; j++)
            {
                int idx = (int)(j == 0 ? i1 : (j == 1 ? i2 : i3)) * 3;
                normalArrays[idx] += (float)nx;
                normalArrays[idx + 1] += (float)ny;
                normalArrays[idx + 2] += (float)nz;
            }
        }

        // Pre-normalize the accumulated normals so we have pure directions for comparison
        for (int i = 0; i < normalArrays.Length; i += 3)
        {
            var len = (float)Math.Sqrt(normalArrays[i]*normalArrays[i] + normalArrays[i+1]*normalArrays[i+1] + normalArrays[i+2]*normalArrays[i+2]);
            if (len > 0)
            {
                normalArrays[i] /= len;
                normalArrays[i+1] /= len;
                normalArrays[i+2] /= len;
            }
        }

        // Group coincident vertices to average normals across faces
        var posMap = new System.Collections.Generic.Dictionary<System.Numerics.Vector3, System.Collections.Generic.List<int>>();
        for (int i = 0; i < verts.Count; i++)
        {
            var p = new System.Numerics.Vector3(
                (float)Math.Round(positions[i * 3], 4),
                (float)Math.Round(positions[i * 3 + 1], 4),
                (float)Math.Round(positions[i * 3 + 2], 4));

            if (!posMap.TryGetValue(p, out var list))
            {
                list = new System.Collections.Generic.List<int>();
                posMap[p] = list;
            }
            list.Add(i);
        }

        float thresholdCos = (float)Math.Cos(settings.CreaseAngle * Math.PI / 180.0);
        var smoothedNormals = new float[normalArrays.Length];

        foreach (var group in posMap.Values)
        {
            foreach (var vIdx in group)
            {
                var vNx = normalArrays[vIdx * 3];
                var vNy = normalArrays[vIdx * 3 + 1];
                var vNz = normalArrays[vIdx * 3 + 2];

                float sumNx = 0, sumNy = 0, sumNz = 0;

                foreach (var uIdx in group)
                {
                    var uNx = normalArrays[uIdx * 3];
                    var uNy = normalArrays[uIdx * 3 + 1];
                    var uNz = normalArrays[uIdx * 3 + 2];

                    float dot = vNx * uNx + vNy * uNy + vNz * uNz;
                    if (dot > thresholdCos - 1e-4f) // Smooth if angle is less than crease angle
                    {
                        sumNx += uNx;
                        sumNy += uNy;
                        sumNz += uNz;
                    }
                }

                // Normalize final smoothed normal
                float len = (float)Math.Sqrt(sumNx * sumNx + sumNy * sumNy + sumNz * sumNz);
                if (len > 0)
                {
                    smoothedNormals[vIdx * 3] = sumNx / len;
                    smoothedNormals[vIdx * 3 + 1] = sumNy / len;
                    smoothedNormals[vIdx * 3 + 2] = sumNz / len;
                }
                else
                {
                    smoothedNormals[vIdx * 3] = vNx;
                    smoothedNormals[vIdx * 3 + 1] = vNy;
                    smoothedNormals[vIdx * 3 + 2] = vNz;
                }
            }
        }

        normals.AddRange(smoothedNormals);

        var posArr = positions.ToArray();
        var idxArr = indices.ToArray();
        var (featureEdges, internalEdges, n1, n2) = EdgeDetector.ClassifyEdges(posArr, idxArr, settings.CreaseAngle);
        var featureNormals1 = new float[n1.Length * 3];
        var featureNormals2 = new float[n2.Length * 3];
        for (int i = 0; i < n1.Length; i++) { featureNormals1[i*3] = n1[i].X; featureNormals1[i*3+1] = n1[i].Y; featureNormals1[i*3+2] = n1[i].Z; featureNormals2[i*3] = n2[i].X; featureNormals2[i*3+1] = n2[i].Y; featureNormals2[i*3+2] = n2[i].Z; }

        return new TriangleMesh(posArr, normals.ToArray(), idxArr, featureEdges, internalEdges, featureNormals1, featureNormals2);
    }
}







