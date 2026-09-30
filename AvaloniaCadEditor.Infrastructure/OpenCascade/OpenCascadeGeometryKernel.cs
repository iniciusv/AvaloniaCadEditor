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

    // Malha local de cada definição de peça (compartilhada por instâncias de peças iguais).
    private readonly Dictionary<Guid, TriangleMesh> _definitionMeshCache = new();

    public void InvalidateShape(Guid featureId)
    {
        if (_shapeCache.TryGetValue(featureId, out var cached))
        {
            cached.Dispose();
            _shapeCache.Remove(featureId);
        }
        _definitionMeshCache.Remove(featureId);
    }

    public void InvalidateAll()
    {
        foreach (var shape in _shapeCache.Values)
        {
            shape?.Dispose();
        }
        _shapeCache.Clear();
        _definitionMeshCache.Clear();
    }

    public TriangleMesh BuildFeatureMesh(IFeature feature, TessellationSettings? settings = null)
    {
        if (feature is ImportedMeshFeature imported)
        {
            return imported.Mesh;
        }

        if (feature is PartInstance instance)
        {
            // A geometria da peça é construída uma vez por definição; cada instância só transforma a malha.
            if (!_definitionMeshCache.TryGetValue(instance.Definition.Id, out var local))
            {
                local = BuildFeatureMesh(instance.Definition, settings);
                _definitionMeshCache[instance.Definition.Id] = local;
            }
            return local.Transform(instance.Placement);
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

    /// <summary>
    /// Constrói (ou reaproveita do cache) o sólido de uma feature. <paramref name="transform"/> é
    /// usado só para reconstruir uma instância de peça girada (veja <see cref="BuildInstanceShape"/>):
    /// nesse caso o sólido é único daquela instância, então não entra no cache por
    /// <see cref="IFeature.Id"/> (que é compartilhado pela definição da peça entre instâncias iguais).
    /// </summary>
    private OcctShape BuildShape(IFeature feature, TessellationSettings settings, Placement? transform = null)
    {
        bool useCache = transform == null || transform.IsIdentity;

        if (useCache && _shapeCache.TryGetValue(feature.Id, out var cached) && cached != null && !cached.IsNull)
        {
            return cached;
        }

        OcctShape result = null;
        try {
            if (feature is ExtrudeFeature extrude && extrude.IsEnabled)
            {
                result = BuildExtrude(extrude, settings, transform);
            }
            else if (feature is CylinderFeature cyl && cyl.IsEnabled)
            {
                result = BuildCylinder(cyl, settings, transform);
            }
            else if (feature is BoxFeature box && box.IsEnabled)
            {
                result = BuildBox(box, settings, transform);
            }
            else if (feature is PrismFeature prism && prism.IsEnabled)
            {
                result = BuildPrism(prism, settings, transform);
            }
            else if (feature is BooleanFeature boolean && boolean.IsEnabled)
            {
                result = BuildBoolean(boolean, settings, transform);
            }
            else if (feature is FilletFeature fillet && fillet.IsEnabled)
            {
                if (transform != null && transform.HasRotation)
                {
                    // O fillet depende das arestas/malha do sólido base já construído; não dá para
                    // "girar" essa receita sem refazer o corte em cima de arestas que não existem
                    // nessa orientação. EnsureCanBeBooleanOperand já barra isso mais cedo; isto é
                    // uma segunda trava, para qualquer outro caminho que chegue aqui girado.
                    throw new NotSupportedException(
                        $"O fillet de '{feature.Name}' não pode ser reconstruído girado (depende das arestas do sólido base).");
                }
                result = BuildFillet(fillet, settings);
            }
            else if (feature is PartInstance instance && instance.IsEnabled)
            {
                result = BuildInstanceShape(instance, settings, transform);
            }
            else if (feature is TransformedFeature transformedFeature && transformedFeature.IsEnabled)
            {
                result = BuildTransformedShape(transformedFeature, settings, transform);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BuildShape Error] {ex}");
        }

        if (useCache && result != null && !result.IsNull)
        {
            _shapeCache[feature.Id] = result;
        }

        return result;
    }

    /// <summary>
    /// Sólido de uma instância de peça no mundo, para uso em operações booleanas.
    /// <list type="bullet">
    /// <item>só translação: a definição é construída uma vez (cache por Id, compartilhado entre
    /// instâncias iguais) e a instância recebe uma cópia transladada pelo OcctNet.Wrapper;</item>
    /// <item>com rotação: o wrapper só sabe transladar sólidos, então o sólido é reconstruído do
    /// zero já em coordenadas do mundo (vértices e direções passam pelo <see cref="Placement"/>
    /// antes de virar B-Rep). Não entra no cache da definição - cada instância girada tem sua
    /// própria forma - mas o chamador (<see cref="BuildShape"/>) ainda cacheia o resultado final
    /// por Id da instância.</item>
    /// </list>
    /// </summary>
    private OcctShape BuildInstanceShape(PartInstance instance, TessellationSettings settings, Placement? outer = null)
    {
        var effective = outer == null ? instance.Placement : instance.Placement.Then(outer);

        if (effective.HasRotation)
        {
            return BuildShape(instance.Definition, settings, effective);
        }

        var local = BuildShape(instance.Definition, settings);
        if (local == null || local.IsNull) return null;

        var t = effective.Translation;
        // Sempre uma cópia: o sólido local continua no cache da definição, e cada entrada é descartada uma vez.
        return local.Translate(new OcctVector3d(t.X, t.Y, t.Z));
    }

    /// <summary>
    /// Sólido de uma feature qualquer já transformada (<see cref="TransformedFeature"/>) — usada para
    /// copiar/girar/mover uma feature solta do script sem reescrever suas coordenadas. Mesmo padrão de
    /// <see cref="BuildInstanceShape"/>: só translação reaproveita o sólido de <c>Source</c> (cache) e
    /// translada uma cópia; com rotação, reconstrói já em coordenadas do mundo.
    /// </summary>
    private OcctShape BuildTransformedShape(TransformedFeature transformed, TessellationSettings settings, Placement? outer = null)
    {
        var effective = outer == null ? transformed.Placement : transformed.Placement.Then(outer);

        if (effective.HasRotation)
        {
            return BuildShape(transformed.Source, settings, effective);
        }

        var local = BuildShape(transformed.Source, settings);
        if (local == null || local.IsNull) return null;

        var t = effective.Translation;
        return local.Translate(new OcctVector3d(t.X, t.Y, t.Z));
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

    private OcctShape BuildCylinder(CylinderFeature cyl, TessellationSettings settings, Placement? transform = null)
    {
        var plane = new WorkPlane(cyl.GlobalDirection, cyl.GlobalBaseCenter);
        var profile = new AvaloniaCadEditor.Domain.Profiles.CircleProfile(Point3D.Origin, cyl.Radius, plane: plane);
        var extrude = new ExtrudeFeature(profile, cyl.Height, cyl.GlobalDirection * cyl.Height);
        return BuildExtrude(extrude, settings, transform);
    }

    private OcctShape BuildBox(BoxFeature box, TessellationSettings settings, Placement? transform = null)
    {
        Point3D bottomCenter = new Point3D(
            box.BaseCorner.X + box.DirectionX.X * (box.Width / 2.0) + box.DirectionY.X * (box.Length / 2.0),
            box.BaseCorner.Y + box.DirectionX.Y * (box.Width / 2.0) + box.DirectionY.Y * (box.Length / 2.0),
            box.BaseCorner.Z + box.DirectionX.Z * (box.Width / 2.0) + box.DirectionY.Z * (box.Length / 2.0)
        );
        var plane = new WorkPlane(bottomCenter, box.DirectionZ, box.DirectionX);
        var profile = new AvaloniaCadEditor.Domain.Profiles.RectangleProfile(Point3D.Origin, box.Width, box.Length, 0.0, box.DirectionZ, null, plane);
        var extrude = new ExtrudeFeature(profile, box.Height, box.DirectionZ * box.Height);
        return BuildExtrude(extrude, settings, transform);
    }

    private OcctShape BuildPrism(PrismFeature prism, TessellationSettings settings, Placement? transform = null)
    {
        var profile = new AvaloniaCadEditor.Domain.Profiles.PolygonProfile(prism.Vertices, plane: prism.Plane);
        var extrude = new ExtrudeFeature(profile, prism.Height, prism.Plane.Normal * prism.Height);
        return BuildExtrude(extrude, settings, transform);
    }

    private OcctShape BuildExtrude(ExtrudeFeature extrude, TessellationSettings settings, Placement? transform = null)
    {
        var verticesReadOnly = extrude.Profile.GetVertices(settings.ProfileSegments);
        if (verticesReadOnly == null || verticesReadOnly.Count < 3) return null;

        var vertices = new List<Point3D>(verticesReadOnly);
        var dir = extrude.Direction;

        if (transform != null && !transform.IsIdentity)
        {
            // Instância de peça girada: leva o perfil local e a direção da extrusão para o mundo
            // antes de montar o B-Rep, já que o OcctNet.Wrapper só sabe transladar sólidos prontos.
            for (int i = 0; i < vertices.Count; i++) vertices[i] = transform.Apply(vertices[i]);
            dir = transform.ApplyVector(dir);
        }

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
    
    /// <summary>
    /// Constrói o resultado de uma booleana com várias bases e várias ferramentas como uma única
    /// operação: as bases são unidas entre si (<see cref="FuseBalanced"/>), e depois o corte/interseção/
    /// união é aplicado de uma vez contra o conjunto de ferramentas (também unidas quando fizer sentido),
    /// em vez de um Cut/Fuse por ferramenta encadeado.
    /// </summary>
    private OcctShape BuildBoolean(BooleanFeature boolean, TessellationSettings settings, Placement? transform = null)
    {
        var baseShapes = boolean.BaseFeatures
                               .Select(f => BuildShape(f, settings, transform))
                               .Where(s => s != null && !s.IsNull)
                               .ToList();

        if (baseShapes.Count == 0) return null;

        var baseShape = FuseBalanced(baseShapes);
        if (baseShape == null || baseShape.IsNull) return null;

        var toolShapes = boolean.ToolFeatures
                               .Select(f => BuildShape(f, settings, transform))
                               .Where(s => s != null && !s.IsNull)
                               .ToList();

        if (toolShapes.Count == 0) return baseShape;

        switch (boolean.Operation)
        {
            case BooleanOperationType.Union:
            {
                var all = new List<OcctShape> { baseShape };
                all.AddRange(toolShapes);
                return FuseBalanced(all);
            }
            case BooleanOperationType.Subtract:
            {
                // Uma única operação: as ferramentas são unidas entre si primeiro, e a base sofre
                // um Cut só, em vez de um Cut por ferramenta.
                var toolUnion = FuseBalanced(toolShapes);
                if (toolUnion == null || toolUnion.IsNull) return baseShape;
                return baseShape.Cut(toolUnion);
            }
            case BooleanOperationType.Intersect:
            {
                // A∩(B∪C) não é o mesmo que A∩B∩C: cada ferramenta precisa entrar na interseção
                // separadamente, então isso continua sequencial.
                var currentShape = baseShape;
                bool isIntermediate = false;
                foreach (var tool in toolShapes)
                {
                    var newShape = currentShape.Common(tool);
                    if (newShape != null && !newShape.IsNull)
                    {
                        if (isIntermediate) currentShape.Dispose();
                        currentShape = newShape;
                        isIntermediate = true;
                    }
                }
                return currentShape;
            }
            default:
                return baseShape;
        }
    }

    /// <summary>
    /// Une uma lista de sólidos em árvore balanceada (fuse aos pares, nível a nível) em vez de uma
    /// cadeia sequencial: mesmo resultado, com menos profundidade de operações booleanas. Nunca
    /// descarta os shapes de entrada (podem estar no <see cref="_shapeCache"/> de outra feature) -
    /// só os resultados intermediários que este método cria. Um par que falhe a fusão (raro) é
    /// descartado silenciosamente, do jeito que o código sequencial anterior já tolerava.
    /// </summary>
    private OcctShape FuseBalanced(List<OcctShape> shapes)
    {
        if (shapes.Count == 0) return null;
        if (shapes.Count == 1) return shapes[0];

        var current = new List<OcctShape>(shapes);
        var owned = new HashSet<OcctShape>();

        while (current.Count > 1)
        {
            var next = new List<OcctShape>();
            for (int i = 0; i < current.Count; i += 2)
            {
                if (i + 1 >= current.Count)
                {
                    next.Add(current[i]);
                    continue;
                }

                var a = current[i];
                var b = current[i + 1];
                var fused = a.Fuse(b);

                if (fused == null || fused.IsNull)
                {
                    if (owned.Remove(b)) b.Dispose();
                    next.Add(a);
                    continue;
                }

                if (owned.Remove(a)) a.Dispose();
                if (owned.Remove(b)) b.Dispose();
                next.Add(fused);
                owned.Add(fused);
            }
            current = next;
        }

        owned.Remove(current[0]);
        return current[0];
    }

    private TriangleMesh TessellateShape(OcctShape shape, TessellationSettings settings)
    {
        var occtMesh = shape.Triangulate(settings.LinearDeflection, settings.AngularDeflection);
        if (occtMesh == null || occtMesh.TriangleCount == 0)
            return TriangleMesh.Empty;

        var positions = new List<float>();
        var normals = new List<float>();

        var verts = occtMesh.Vertices;

        // Corrige a orientação dos triângulos ANTES de calcular normais/volume. Uma heurística
        // "a normal deve apontar para longe do centróide de TODOS os vértices" (usada antes
        // aqui) só funciona para sólidos convexos e de um corpo só — ela inverte triângulos
        // silenciosamente em qualquer peça côncava, com furo, ou resultado de boolean com mais
        // de um sólido (o centro médio dos vértices não é "dentro" do sólido nesses casos). O
        // jeito robusto é propagar a orientação pelas arestas compartilhadas (vizinhos devem
        // percorrer a aresta em sentidos opostos) e, por componente conexo, corrigir com o
        // sinal do volume (teorema da divergência) qual das duas orientações relativas é a de
        // fora.
        var triIndices = FixTriangleWinding(occtMesh);

        var normalArrays = new float[verts.Count * 3];

        for (int i = 0; i < verts.Count; i++)
        {
            positions.Add((float)verts[i].X);
            positions.Add((float)verts[i].Y);
            positions.Add((float)verts[i].Z);
        }

        // Calculate flat normals since OcctMeshVertex doesn't provide them
        // (a orientação já foi corrigida por FixTriangleWinding, então basta o produto vetorial)
        for (int i = 0; i < triIndices.Length; i += 3)
        {
            uint i1 = triIndices[i];
            uint i2 = triIndices[i + 1];
            uint i3 = triIndices[i + 2];

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

        var indices = new List<uint>(triIndices);

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

    /// <summary>
    /// Corrige a orientação (sentido de giro) de cada triângulo tesselado pelo OCCT, garantindo
    /// que todas as normais de um mesmo sólido apontem consistentemente para fora — mesmo em
    /// peças côncavas, com furos, ou resultantes de um boolean com vários corpos.
    ///
    /// Funciona em duas etapas por componente conexo (grupo de triângulos ligados por arestas
    /// compartilhadas — cada "casca" de um sólido, mesmo que a malha tenha vários sólidos
    /// desconexos de uma vez):
    ///  1. Propaga a orientação por BFS: dois triângulos vizinhos que compartilham uma aresta
    ///     (exatamente 2 usuários — arestas de borda ou não-manifold ficam de fora) devem
    ///     percorrê-la em sentidos opostos entre si. Isso já deixa a casca inteira consistente
    ///     entre si, mas ainda pode estar "de dentro para fora" como um todo.
    ///  2. Calcula o volume com sinal (teorema da divergência) da casca já consistente; se vier
    ///     negativo, inverte todos os triângulos do componente de uma vez.
    /// </summary>
    private static uint[] FixTriangleWinding(OcctMesh occtMesh)
    {
        var verts = occtMesh.Vertices;
        var rawIndices = occtMesh.TriangleIndices;
        int triCount = rawIndices.Count / 3;

        // 1. Agrupa vértices coincidentes (a tesselação do OCCT duplica vértices entre faces
        //    adjacentes) num id canônico, para achar vizinhança por aresta.
        var canonicalId = new int[verts.Count];
        var canonicalMap = new Dictionary<(long, long, long), int>();
        const double weldScale = 10000.0; // 4 casas decimais
        for (int i = 0; i < verts.Count; i++)
        {
            var key = (
                (long)Math.Round(verts[i].X * weldScale),
                (long)Math.Round(verts[i].Y * weldScale),
                (long)Math.Round(verts[i].Z * weldScale));
            if (!canonicalMap.TryGetValue(key, out var id))
            {
                id = canonicalMap.Count;
                canonicalMap[key] = id;
            }
            canonicalId[i] = id;
        }

        var triCanon = new (int A, int B, int C)[triCount];
        for (int t = 0; t < triCount; t++)
        {
            triCanon[t] = (
                canonicalId[rawIndices[t * 3]],
                canonicalId[rawIndices[t * 3 + 1]],
                canonicalId[rawIndices[t * 3 + 2]]);
        }

        // 2. Para cada aresta (não direcionada, em ids canônicos), guarda quem a usa e em que
        //    sentido (a direção original, ainda sem nenhuma correção aplicada).
        var edgeUsers = new Dictionary<(int, int), List<(int Triangle, int From, int To)>>();
        void AddEdge(int t, int u, int v)
        {
            var key = u < v ? (u, v) : (v, u);
            if (!edgeUsers.TryGetValue(key, out var list))
            {
                list = new List<(int, int, int)>();
                edgeUsers[key] = list;
            }
            list.Add((t, u, v));
        }
        for (int t = 0; t < triCount; t++)
        {
            var (a, b, c) = triCanon[t];
            AddEdge(t, a, b);
            AddEdge(t, b, c);
            AddEdge(t, c, a);
        }

        var flipped = new bool[triCount];
        var visited = new bool[triCount];
        var queue = new Queue<int>();

        for (int seed = 0; seed < triCount; seed++)
        {
            if (visited[seed]) continue;
            visited[seed] = true;
            var component = new List<int> { seed };
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                var (a, b, c) = triCanon[t];

                foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                {
                    var key = u < v ? (u, v) : (v, u);
                    var users = edgeUsers[key];
                    if (users.Count != 2) continue; // borda ou não-manifold: não propaga por aqui

                    // Direção efetiva de t nessa aresta, já considerando se t foi invertido.
                    bool effectiveFromIsU = !flipped[t];
                    int effectiveFrom = effectiveFromIsU ? u : v;
                    int effectiveTo = effectiveFromIsU ? v : u;

                    foreach (var (otherT, origFrom, origTo) in users)
                    {
                        if (otherT == t || visited[otherT]) continue;

                        // Quer que a direção efetiva de otherT seja a OPOSTA (effectiveTo -> effectiveFrom).
                        // origFrom/origTo é a direção original (sem flip) de otherT nessa mesma aresta.
                        flipped[otherT] = origFrom == effectiveFrom;
                        visited[otherT] = true;
                        component.Add(otherT);
                        queue.Enqueue(otherT);
                    }
                }
            }

            // 3. Corrige a orientação global do componente pelo sinal do volume.
            double signedVolume = 0;
            foreach (var t in component)
            {
                signedVolume += SignedTetrahedronVolume(verts, rawIndices, t, flipped[t]);
            }

            if (signedVolume < 0)
            {
                foreach (var t in component) flipped[t] = !flipped[t];
            }
        }

        var result = new uint[rawIndices.Count];
        for (int t = 0; t < triCount; t++)
        {
            uint i1 = (uint)rawIndices[t * 3];
            uint i2 = (uint)rawIndices[t * 3 + 1];
            uint i3 = (uint)rawIndices[t * 3 + 2];
            if (flipped[t]) { (i2, i3) = (i3, i2); }

            result[t * 3] = i1;
            result[t * 3 + 1] = i2;
            result[t * 3 + 2] = i3;
        }
        return result;
    }

    private static double SignedTetrahedronVolume(IReadOnlyList<OcctMeshVertex> verts, IReadOnlyList<int> rawIndices, int triangle, bool flipped)
    {
        int i1 = rawIndices[triangle * 3];
        int i2 = rawIndices[triangle * 3 + 1];
        int i3 = rawIndices[triangle * 3 + 2];
        if (flipped) { (i2, i3) = (i3, i2); }

        var p1 = verts[i1];
        var p2 = verts[i2];
        var p3 = verts[i3];

        return (p1.X * (p2.Y * p3.Z - p2.Z * p3.Y)
              + p1.Y * (p2.Z * p3.X - p2.X * p3.Z)
              + p1.Z * (p2.X * p3.Y - p2.Y * p3.X)) / 6.0;
    }
}







