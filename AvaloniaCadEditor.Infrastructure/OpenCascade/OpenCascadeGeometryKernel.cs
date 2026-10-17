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
    /// <summary>
    /// Chave dos caches por Id. Com os fillets suprimidos a mesma feature tem outro sólido, e as duas versões
    /// ficam guardadas: religar os fillets não refaz as booleanas de chanfro de nenhuma peça.
    /// </summary>
    private readonly record struct CacheKey(Guid Id, bool FilletsSuppressed);

    private readonly Dictionary<CacheKey, OcctShape> _shapeCache = new();

    // Malha local de cada definição de peça (compartilhada por instâncias de peças iguais).
    private readonly Dictionary<CacheKey, TriangleMesh> _definitionMeshCache = new();

    // Malhas por geometria (FeatureFingerprint + tesselação). Diferente dos caches por Id, sobrevivem
    // a uma reexecução da cena: mover uma peça não refaz as booleanas/tesselação de nenhuma definição.
    private readonly Dictionary<string, TriangleMesh> _meshByGeometry = new();
    private readonly HashSet<string> _geometryKeysInUse = new();

    // Teto de segurança: ferramentas interativas (preview) podem gerar uma chave nova por frame.
    private const int MaxMeshesByGeometry = 512;

    // Por que a geometria de cada feature não saiu. Vive junto com _shapeCache: uma feature que
    // falha nunca é cacheada, então o problema é recalculado a cada tentativa; mas o cache de
    // malhas da CENA guarda a malha vazia, e por isso a mensagem precisa ficar guardada aqui em
    // vez de valer só para a última chamada.
    private readonly Dictionary<Guid, string> _buildProblems = new();

    /// <inheritdoc />
    public IReadOnlyDictionary<Guid, string> BuildProblems => _buildProblems;

    /// <summary>Registra (ou limpa, com message null) por que a feature não gerou geometria.</summary>
    private void ReportProblem(IFeature feature, string? message)
    {
        if (message == null) _buildProblems.Remove(feature.Id);
        else _buildProblems[feature.Id] = $"{feature.Name}: {message}";
    }

    /// <summary>Quantas malhas foram de fato tesseladas (cache miss). Usado por testes.</summary>
    public int MeshBuildCount { get; private set; }

    /// <inheritdoc />
    public bool SuppressFillets { get; set; }

    private CacheKey KeyOf(Guid featureId) => new(featureId, SuppressFillets);

    public void InvalidateShape(Guid featureId)
    {
        foreach (bool suppressed in new[] { false, true })
        {
            var key = new CacheKey(featureId, suppressed);
            if (_shapeCache.Remove(key, out var cached)) cached.Dispose();
            _definitionMeshCache.Remove(key);
        }
        _buildProblems.Remove(featureId);
    }

    public void InvalidateAll()
    {
        foreach (var shape in _shapeCache.Values)
        {
            shape?.Dispose();
        }
        _shapeCache.Clear();
        _definitionMeshCache.Clear();
        _meshByGeometry.Clear();
        _geometryKeysInUse.Clear();
        _buildProblems.Clear();
    }

    /// <summary>
    /// Nova execução da cena: tudo que é por Id é descartado (os Ids mudaram), mas as malhas por
    /// geometria usadas desde a execução anterior continuam disponíveis; as demais saem.
    /// </summary>
    public void BeginSceneRebuild()
    {
        foreach (var shape in _shapeCache.Values)
        {
            shape?.Dispose();
        }
        _shapeCache.Clear();
        _definitionMeshCache.Clear();
        _buildProblems.Clear();
        RemoveMeshesNotInUse();
        _geometryKeysInUse.Clear();
    }

    private void RemoveMeshesNotInUse()
    {
        foreach (var key in _meshByGeometry.Keys.Where(k => !_geometryKeysInUse.Contains(k)).ToList())
        {
            _meshByGeometry.Remove(key);
        }
    }

    private string? GeometryKey(IFeature feature, TessellationSettings settings)
    {
        var fingerprint = FeatureFingerprint.Compute(feature);
        if (fingerprint == null) return null;

        // Tudo que pode mudar a malha gerada entra na chave (cores não, mas não custa nada).
        return string.Join("|",
            BitConverter.DoubleToInt64Bits(settings.LinearDeflection),
            BitConverter.DoubleToInt64Bits(settings.AngularDeflection),
            settings.ProfileSegments,
            settings.FaceSegments,
            settings.ShowInternalEdges,
            BitConverter.SingleToInt32Bits(settings.CreaseAngle),
            settings.FeatureEdgeColor,
            settings.InternalEdgeColor,
            SuppressFillets,
            fingerprint);
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
            if (!_definitionMeshCache.TryGetValue(KeyOf(instance.Definition.Id), out var local))
            {
                local = BuildFeatureMesh(instance.Definition, settings);
                _definitionMeshCache[KeyOf(instance.Definition.Id)] = local;
            }
            return local.Transform(instance.Placement);
        }

        settings ??= new TessellationSettings();

        string? key = GeometryKey(feature, settings);
        if (key != null && _meshByGeometry.TryGetValue(key, out var reused))
        {
            _geometryKeysInUse.Add(key);
            return reused;
        }

        try
        {
            var shape = BuildShape(feature, settings);
            if (shape == null || shape.IsNull)
                return TriangleMesh.Empty;

            var mesh = OcctTessellator.Tessellate(shape, settings);
            MeshBuildCount++;
            // shape.Dispose(); // Shape is now cached, do not dispose here

            // Malha vazia pode ser falha do OCCT: não fica guardada, tenta de novo na próxima vez.
            if (key != null && mesh.VertexCount > 0)
            {
                if (_meshByGeometry.Count >= MaxMeshesByGeometry)
                {
                    RemoveMeshesNotInUse();
                    if (_meshByGeometry.Count >= MaxMeshesByGeometry)
                    {
                        _meshByGeometry.Clear();
                        _geometryKeysInUse.Clear();
                    }
                }
                _meshByGeometry[key] = mesh;
                _geometryKeysInUse.Add(key);
            }
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

        var meshes = features.Select(f => BuildFeatureMesh(f, settings)).ToList();
        return TriangleMesh.Combine(meshes, includeFeatureNormals: true);
    }

    /// <summary>
    /// Constrói (ou reaproveita do cache) o sólido de uma feature. <paramref name="transform"/> é
    /// usado só para reconstruir uma instância de peça girada (veja <see cref="BuildInstanceShape"/>):
    /// nesse caso o sólido é único daquela instância, então não entra no cache por
    /// <see cref="IFeature.Id"/> (que é compartilhado pela definição da peça entre instâncias iguais).
    /// </summary>
    private OcctShape BuildShape(IFeature feature, TessellationSettings settings, Placement? transform = null)
    {
        // Fillet suprimido vale o sólido base. Não entra no cache: guardaria o mesmo OcctShape em duas chaves,
        // e invalidar uma delas daria Dispose num sólido que a outra ainda usa.
        if (SuppressFillets && feature is FilletFeature suppressed && suppressed.IsEnabled)
            return BuildShape(suppressed.BaseFeature, settings, transform);

        bool useCache = transform == null || transform.IsIdentity;

        if (useCache && _shapeCache.TryGetValue(KeyOf(feature.Id), out var cached) && cached != null && !cached.IsNull)
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
            else if (feature is ConeFeature cone && cone.IsEnabled)
            {
                result = BuildCone(cone, settings, transform);
            }
            else if (feature is BoxFeature box && box.IsEnabled)
            {
                result = BuildBox(box, settings, transform);
            }
            else if (feature is PrismFeature prism && prism.IsEnabled)
            {
                result = BuildPrism(prism, settings, transform);
            }
            else if (feature is SweepFeature sweep && sweep.IsEnabled)
            {
                result = BuildSweep(sweep, settings, transform);
            }
            else if (feature is BooleanFeature boolean && boolean.IsEnabled)
            {
                result = BuildBoolean(boolean, settings, transform);
            }
            else if (feature is FilletFeature fillet && fillet.IsEnabled)
            {
                result = BuildFillet(fillet, settings, transform);
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
            ReportProblem(feature, ex.Message);
            Console.WriteLine($"[BuildShape Error] {ex}");
        }

        if (result != null && !result.IsNull)
        {
            // Deu certo agora: o problema anterior (se havia) está resolvido.
            ReportProblem(feature, null);
            if (useCache) _shapeCache[KeyOf(feature.Id)] = result;
        }

        return result;
    }

    private OcctShape BuildFillet(FilletFeature fillet, TessellationSettings settings, Placement? transform)
    {
        if (transform != null && transform.HasRotation)
        {
            // O fillet depende das arestas/malha do sólido base já construído; não dá para
            // "girar" essa receita sem refazer o corte em cima de arestas que não existem
            // nessa orientação. EnsureCanBeBooleanOperand já barra isso mais cedo; isto é
            // uma segunda trava, para qualquer outro caminho que chegue aqui girado.
            throw new NotSupportedException(
                $"O fillet de '{fillet.Name}' não pode ser reconstruído girado nem espelhado (depende das arestas do sólido base).");
        }
        // O builder recebe o sólido base pronto: ele corta arestas, não constrói
        // features, e assim não precisa do dispatch nem do cache.
        var filletBase = BuildShape(fillet.BaseFeature, settings);
        return OcctFilletBuilder.Build(fillet, filletBase, settings);
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

    private OcctShape BuildCylinder(CylinderFeature cyl, TessellationSettings settings, Placement? transform = null)
    {
        var plane = new WorkPlane(cyl.GlobalDirection, cyl.GlobalBaseCenter);
        var profile = new AvaloniaCadEditor.Domain.Profiles.CircleProfile(Point3D.Origin, cyl.Radius, plane: plane);
        var extrude = new ExtrudeFeature(profile, cyl.Height, cyl.GlobalDirection * cyl.Height);
        return BuildExtrude(extrude, settings, transform);
    }

    /// <summary>
    /// Tronco de cone EXATO: revolução de 360° do trapézio (0,0) → (R,0) → (r,h) → (0,h) em torno do
    /// próprio eixo. O lado que fecha corre SOBRE o eixo e é invariante pela rotação, então não vira
    /// face: o sólido sai com tampa de baixo, lateral cônica exata e tampa de cima — esta última
    /// ausente quando TopRadius é 0 (o trapézio vira o triângulo (0,0) → (R,0) → (0,h) e o topo é o
    /// ápice).
    /// <para>
    /// Diferente de <see cref="BuildCylinder"/>, que ainda baixa para a extrusão de um polígono de
    /// ProfileSegments lados: aqui a lateral é uma superfície cônica de verdade e a aproximação
    /// existe só na malha. Quem decide a finura dela é a tesselação
    /// (<see cref="OcctTessellator.Tessellate"/>), que usa ProfileSegments como piso de facetas.
    /// </para>
    /// </summary>
    private OcctShape BuildCone(ConeFeature cone, TessellationSettings settings, Placement? transform = null)
    {
        double bottomRadius = cone.BottomRadius;
        double topRadius = cone.TopRadius;
        double height = cone.Height;

        // Degenerado: sem sólido, do mesmo jeito que BuildExtrude devolve null para um perfil com
        // menos de 3 pontos. BuildFeatureMesh transforma isso em TriangleMesh.Empty, sem exceção.
        // Altura NEGATIVA é válida (cone para trás): o trapézio espelha e o Newell corrige o sentido.
        if (bottomRadius < 0 || topRadius < 0) return null;
        if (Math.Abs(height) < 1e-9) return null;
        if (bottomRadius <= ConeFeature.ApexTolerance && topRadius <= ConeFeature.ApexTolerance) return null;

        // Instância de peça girada: o EIXO INTEIRO (origem + direção) vai para o mundo antes de montar
        // o perfil. Não dá para transformar só os pontos, como BuildExtrude faz — o perfil giraria em
        // torno do eixo antigo e sairia um sólido plausível na orientação errada, sem erro nenhum.
        // Com o eixo já no mundo, o 'u' que RevolveProfile deriva dele também nasce certo.
        var origin = cone.GlobalBaseCenter;
        var dir = cone.GlobalDirection;
        if (transform != null && !transform.IsIdentity)
        {
            origin = transform.Apply(origin);
            dir = transform.ApplyVector(dir);
        }
        if (dir.Length < 1e-9) return null;
        dir = dir.Normalize();

        // O perfil fechado, encostando no eixo nas duas pontas. Um raio nulo entra como UM ponto só:
        // repetir (0, h) criaria uma aresta de comprimento zero.
        var points2D = new System.Collections.Generic.List<(double r, double z)> { (0, 0) };
        if (bottomRadius > ConeFeature.ApexTolerance) points2D.Add((bottomRadius, 0));
        if (topRadius > ConeFeature.ApexTolerance) points2D.Add((topRadius, height));
        points2D.Add((0, height));

        return OcctProfileGeometry.RevolveProfile(points2D, origin, dir);
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

        var face = BuildProfileFace(vertices, dir);
        if (face == null) return null;

        if (Math.Abs(extrude.Height) < 1e-6) return face;

        return face.Extrude(vec);
    }

    /// <summary>
    /// Face planar fechada a partir dos vértices do perfil JÁ EM COORDENADAS DO MUNDO, orientada
    /// para que a normal dela aponte no sentido de <paramref name="sweepDirection"/> — o sentido em
    /// que o perfil vai ser varrido (translação, em <see cref="BuildExtrude"/>; rotação, em
    /// <see cref="BuildSweep"/>). Devolve null para um perfil degenerado (menos de 3 pontos
    /// distintos). A lista é modificada no lugar.
    /// </summary>
    private static OcctFace BuildProfileFace(List<Point3D> vertices, Vector3D sweepDirection)
    {
        if (vertices.Count < 3) return null;

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
        if (nx * sweepDirection.X + ny * sweepDirection.Y + nz * sweepDirection.Z < 0)
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
        return new OcctFace(wire);
    }

    /// <summary>
    /// Varredura: um sólido por trecho do caminho, fundidos num só.
    /// <list type="bullet">
    /// <item>reta → <c>Extrude</c> da seção pelo deslocamento do trecho;</item>
    /// <item>arco → <c>Revolve</c> da seção em torno do eixo do arco. É EXATO porque o plano da
    /// seção contém o eixo por construção (o eixo passa pelo centro do arco, que está no plano da
    /// seção, na direção do próprio eixo) — a superfície do cotovelo é uma superfície de revolução
    /// de verdade, não uma sequência de prismas facetados.</item>
    /// </list>
    /// <para>
    /// Os trechos encostam face com face (o transporte do referencial em
    /// <see cref="SweepFeature.GetStations"/> é a MESMA rotação que o <c>Revolve</c> aplica), então
    /// o <see cref="FuseBalanced"/> elimina as faces coincidentes e devolve um sólido só. O wrapper
    /// não expõe contagem de sólidos, então aqui não dá para conferir que a fusão aconteceu — quem
    /// garante isso são os testes, que medem a ÁREA da malha (um compound guardaria as tampas).
    /// </para>
    /// <para>
    /// Igual a <see cref="BuildCone"/>, transforma o referencial inteiro (origem e eixos), não só
    /// os vértices: revolver em torno de um eixo não transformado daria o volume certo no lugar errado.
    /// </para>
    /// </summary>
    private OcctShape BuildSweep(SweepFeature sweep, TessellationSettings settings, Placement? transform = null)
    {
        if (sweep.Section == null || sweep.Path == null) return null;
        if (sweep.Path.Error != null)
        {
            ReportProblem(sweep, sweep.Path.Error);
            return null;
        }

        var outline = sweep.Section.GetPoints(settings.ProfileSegments);
        if (outline == null || outline.Count < 3) return null;

        var stations = sweep.GetStations();
        var segments = sweep.Path.Segments;
        if (stations.Count <= segments.Count) return null;

        var pieces = new List<OcctShape>(segments.Count);

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var station = stations[i];

            var vertices = new List<Point3D>(outline.Count);
            foreach (var (u, v) in outline) vertices.Add(station.ToWorldSpace(new Point3D(u, v, 0)));

            var origin = station.Origin;
            var tangent = station.Normal;
            var lineVector = segment.LineVector;
            var arcCenter = segment.ArcCenter;
            var arcAxis = segment.ArcAxis;

            if (transform != null && !transform.IsIdentity)
            {
                for (int k = 0; k < vertices.Count; k++) vertices[k] = transform.Apply(vertices[k]);
                origin = transform.Apply(origin);
                tangent = transform.ApplyVector(tangent);
                lineVector = transform.ApplyVector(lineVector);
                arcCenter = transform.Apply(arcCenter);
                // O eixo de revolução é um pseudovetor: o espelho inverte o sentido do giro, então o
                // eixo espelhado troca de sinal para o cotovelo continuar indo para o lado espelhado.
                arcAxis = transform.ApplyVector(arcAxis);
                if (transform.IsMirrored) arcAxis = arcAxis * -1;
            }

            OcctShape piece;

            if (segment.IsArc)
            {
                if (segment.ArcAngle <= SweepFeature.DegenerateTolerance) continue;
                if (arcAxis.Length < SweepFeature.DegenerateTolerance) return null;

                // A seção não pode cruzar o eixo: um ponto a distância 'd' do eixo, do lado de
                // dentro, acaba em raio negativo e o sólido dobra para dentro de si mesmo. É o raio
                // mínimo de curvatura de uma dobra real, e o erro que mais aparece na prática.
                var toCenter = arcCenter - origin;
                double bendRadius = toCenter.Length;
                if (bendRadius < SweepFeature.DegenerateTolerance) return null;
                var bendDir = toCenter.Normalize();

                foreach (var v in vertices)
                {
                    if (bendRadius - (v - origin).DotProduct(bendDir) < -SweepFeature.DegenerateTolerance)
                    {
                        ReportProblem(sweep,
                            $"a seção não cabe no raio de curvatura {bendRadius:0.###} do trecho {i}.");
                        return null;
                    }
                }

                var face = BuildProfileFace(vertices, tangent);
                if (face == null) return null;

                var axis = new OcctAxis1d(
                    new OcctPointCoordinates(arcCenter.X, arcCenter.Y, arcCenter.Z),
                    new OcctVector3d(arcAxis.X, arcAxis.Y, arcAxis.Z)
                );
                piece = face.Revolve(axis, segment.ArcAngle);
            }
            else
            {
                if (lineVector.Length < SweepFeature.DegenerateTolerance) continue;

                var face = BuildProfileFace(vertices, tangent);
                if (face == null) return null;

                piece = face.Extrude(new OcctVector3d(lineVector.X, lineVector.Y, lineVector.Z));
            }

            if (piece == null || piece.IsNull) return null;
            pieces.Add(piece);
        }

        if (pieces.Count == 0) return null;
        return FuseBalanced(pieces);
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
}
