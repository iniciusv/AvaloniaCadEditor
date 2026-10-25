using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

/// <inheritdoc cref="ISketchProjectionService"/>
/// <remarks>
/// O resultado de cada fonte fica em cache junto da malha (por instância), do plano e do modo: o arrasto da
/// extrusão reconstrói a cena a cada passo, mas a malha das outras features continua a mesma instância. O
/// grafo de arestas de cada malha também é guardado, preso à vida dela.
/// </remarks>
public sealed class SketchProjectionService : ISketchProjectionService
{
    private sealed record CachedProjection(TriangleMesh Mesh, WorkPlane Plane, VolumeProjectionMode Mode, List<PlaneReference> References);

    private readonly ISceneMeshManager _meshes;
    private readonly IPolygonUnion _union;
    private readonly Dictionary<WorkPlane, List<ProjectionSource>> _sources = new();
    private readonly Dictionary<ProjectionSource, CachedProjection> _cache = new();
    private readonly ConditionalWeakTable<TriangleMesh, MeshEdgeGraph> _graphs = new();

    public SketchProjectionService(ISceneMeshManager meshes, IPolygonUnion union)
    {
        _meshes = meshes;
        _union = union;
    }

    public VolumeProjectionMode VolumeMode { get; set; } = VolumeProjectionMode.Outline;

    public void Add(Sketch sketch, WorkPlane plane, ProjectionSource source)
    {
        if (Contains(sketch, plane, source)) return;
        if (!_sources.TryGetValue(plane, out var list))
        {
            list = new List<ProjectionSource>();
            _sources[plane] = list;
        }
        list.Add(source);
    }

    public void Remove(Sketch sketch, WorkPlane plane, ProjectionSource source)
    {
        if (!_sources.TryGetValue(plane, out var list)) return;
        foreach (var removed in list.Where(s => SameTarget(sketch, s, source)).ToList())
        {
            list.Remove(removed);
            _cache.Remove(removed);
        }
    }

    public void Clear(WorkPlane plane)
    {
        if (!_sources.Remove(plane, out var list)) return;
        foreach (var source in list) _cache.Remove(source);
    }

    public bool Contains(Sketch sketch, WorkPlane plane, ProjectionSource source) =>
        _sources.TryGetValue(plane, out var list) && list.Any(s => SameTarget(sketch, s, source));

    public bool HasAny(WorkPlane plane) => _sources.TryGetValue(plane, out var list) && list.Count > 0;

    public IReadOnlyList<PlaneReference> ReferencesFor(Sketch sketch, WorkPlane plane)
    {
        if (!_sources.TryGetValue(plane, out var list)) return Array.Empty<PlaneReference>();

        Rebind(sketch, list);
        var result = new List<PlaneReference>();
        foreach (var source in list)
            result.AddRange(Project(sketch, plane, source));
        return result;
    }

    /// <summary>
    /// Religa pelo nome as fontes cuja feature foi recriada com outro Id e tira as que sumiram da cena.
    /// </summary>
    private void Rebind(Sketch sketch, List<ProjectionSource> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var source = list[i];
            if (sketch.Features.Any(f => f.Id == source.FeatureId)) continue;

            _cache.Remove(source);
            var recreated = sketch.Features.FirstOrDefault(f => f.Name == source.FeatureName && f is not WorkPlaneFeature);
            if (recreated != null) list[i] = source.Rebind(recreated.Id);
            else list.RemoveAt(i);
        }
    }

    private List<PlaneReference> Project(Sketch sketch, WorkPlane plane, ProjectionSource source)
    {
        var mesh = _meshes.GetFeatureMesh(sketch, source.FeatureId, null);
        if (mesh == null || mesh.TriangleCount == 0) return new List<PlaneReference>();

        if (_cache.TryGetValue(source, out var cached)
            && ReferenceEquals(cached.Mesh, mesh) && Equals(cached.Plane, plane) && cached.Mode == VolumeMode)
            return cached.References;

        var references = Compute(Graph(mesh), plane, source);
        _cache[source] = new CachedProjection(mesh, plane, VolumeMode, references);
        return references;
    }

    private List<PlaneReference> Compute(MeshEdgeGraph graph, WorkPlane plane, ProjectionSource source)
    {
        if (source.Target == ProjectionTarget.Body)
        {
            return VolumeMode == VolumeProjectionMode.Outline
                ? PlaneProjection.Outline(graph, plane, _union)
                : PlaneProjection.Edges(graph, null, plane);
        }

        int seed = MeshFaces.FindSeed(graph, source.Seed, source.SeedNormal);
        return seed == MeshFaces.NotFound
            ? new List<PlaneReference>()
            : PlaneProjection.Edges(graph, MeshFaces.Region(graph, seed), plane);
    }

    /// <summary>
    /// Duas fontes apontam para a mesma coisa: o mesmo sólido, ou — para face — pontos que caem na mesma
    /// região da malha atual (clicar de novo na face, em outro lugar, é a mesma face).
    /// </summary>
    private bool SameTarget(Sketch sketch, ProjectionSource a, ProjectionSource b)
    {
        if (a.Target != b.Target || a.FeatureId != b.FeatureId) return false;
        if (a.Target == ProjectionTarget.Body) return true;

        var mesh = _meshes.GetFeatureMesh(sketch, a.FeatureId, null);
        if (mesh == null || mesh.TriangleCount == 0) return a == b;

        var graph = Graph(mesh);
        int seedA = MeshFaces.FindSeed(graph, a.Seed, a.SeedNormal);
        int seedB = MeshFaces.FindSeed(graph, b.Seed, b.SeedNormal);
        if (seedA == MeshFaces.NotFound || seedB == MeshFaces.NotFound) return a == b;
        return MeshFaces.Region(graph, seedA).Contains(seedB);
    }

    private MeshEdgeGraph Graph(TriangleMesh mesh) => _graphs.GetValue(mesh, m => MeshEdgeGraph.Build(m));
}
