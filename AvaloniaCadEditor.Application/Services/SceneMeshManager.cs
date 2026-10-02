using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class SceneMeshManager : ISceneMeshManager
{
    private readonly IGeometryKernel _geometryKernel;
    private readonly ITessellationSettingsProvider _settingsProvider;
    private readonly Dictionary<Guid, TriangleMesh> _featureMeshCache = new();

    public SceneMeshManager(IGeometryKernel geometryKernel, ITessellationSettingsProvider settingsProvider)
    {
        _geometryKernel = geometryKernel;
        _settingsProvider = settingsProvider;
    }

    public void ClearCache()
    {
        _featureMeshCache.Clear();
        _geometryKernel.InvalidateAll();
    }

    public void ResetForNewScene()
    {
        _featureMeshCache.Clear();
        _geometryKernel.BeginSceneRebuild();
    }

    public TriangleMesh GetFeatureMesh(Sketch sketch, Guid featureId, IFeature? previewFeature, bool forceRebuild = false)
    {
        if (forceRebuild)
        {
            _geometryKernel.InvalidateShape(featureId);
            _featureMeshCache.Remove(featureId);
        }

        if (!forceRebuild && _featureMeshCache.TryGetValue(featureId, out var mesh))
        {
            return mesh;
        }

        IFeature? FindFeature(System.Collections.Generic.IEnumerable<IFeature> features)
        {
            foreach (var f in features)
            {
                if (f.Id == featureId) return f;
                if (f is AvaloniaCadEditor.Domain.Features.BooleanFeature bf)
                {
                    var foundBase = FindFeature(bf.BaseFeatures);
                    if (foundBase != null) return foundBase;
                    var foundTool = FindFeature(bf.ToolFeatures);
                    if (foundTool != null) return foundTool;
                }
            }
            return null;
        }

        var feature = FindFeature(sketch.Features);
        
        if (feature == null && previewFeature != null && previewFeature.Id == featureId)
        {
            feature = previewFeature;
        }

        if (feature == null) return TriangleMesh.Empty;

        mesh = _geometryKernel.BuildSolidMesh(new[] { feature }, _settingsProvider.GetSettings());
        if (mesh != null && !forceRebuild)
        {
            _featureMeshCache[featureId] = mesh;
        }
        return mesh ?? TriangleMesh.Empty;
    }

    public void InvalidateFeatureAndDependents(Sketch sketch, Guid featureId)
    {
        _featureMeshCache.Remove(featureId);
        _geometryKernel.InvalidateShape(featureId);

        foreach (var feature in sketch.Features)
        {
            foreach (var dep in feature.Dependencies)
            {
                if (dep.Id == featureId)
                {
                    InvalidateFeatureAndDependents(sketch, feature.Id);
                    break;
                }
            }
        }
    }

    public TriangleMesh GetMeshFor(IFeature feature) => GetOrBuildMesh(feature) ?? TriangleMesh.Empty;

    private TriangleMesh? GetOrBuildMesh(IFeature feature)
    {
        if (!_featureMeshCache.TryGetValue(feature.Id, out var mesh))
        {
            mesh = _geometryKernel.BuildSolidMesh(new[] { feature }, _settingsProvider.GetSettings());
            if (mesh != null)
            {
                _featureMeshCache[feature.Id] = mesh;
            }
        }
        return mesh;
    }

    public RaycastHit? Raycast3D(Sketch sketch, Ray ray, Func<IFeature, bool>? includeFilter = null, IEnumerable<IFeature>? extraFeatures = null)
    {
        RaycastHit? bestHit = null;
        foreach (var hit in RaycastCandidates(sketch, ray, includeFilter, extraFeatures))
        {
            if (bestHit == null || hit.Distance < bestHit.Value.Distance)
            {
                bestHit = hit;
            }
        }
        return bestHit;
    }

    public IReadOnlyList<RaycastHit> RaycastAll3D(Sketch sketch, Ray ray, Func<IFeature, bool>? includeFilter = null, IEnumerable<IFeature>? extraFeatures = null)
    {
        var hits = RaycastCandidates(sketch, ray, includeFilter, extraFeatures).ToList();
        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return hits;
    }

    /// <summary>
    /// Um acerto por sólido atingido: as features visíveis do sketch (ou aceitas por <paramref name="includeFilter"/>)
    /// e, sempre, as <paramref name="extraFeatures"/> — sólidos que não estão no sketch, como as ferramentas de
    /// uma peça já posicionadas no mundo (modo de edição de grupo).
    /// </summary>
    private IEnumerable<RaycastHit> RaycastCandidates(Sketch sketch, Ray ray, Func<IFeature, bool>? includeFilter, IEnumerable<IFeature>? extraFeatures)
    {
        var candidates = sketch.Features.Where(f => f.IsVisible || (includeFilter != null && includeFilter(f)));
        if (extraFeatures != null)
        {
            candidates = candidates.Concat(extraFeatures);
        }

        foreach (var feature in candidates)
        {
            var mesh = GetOrBuildMesh(feature);
            if (mesh == null) continue;

            var hit = ray.IntersectMesh(mesh);
            if (hit.HasValue)
            {
                yield return new RaycastHit(hit.Value.Distance, hit.Value.Point, hit.Value.Normal, feature.Id);
            }
        }
    }

    public TriangleMesh BuildCompositeMesh(Sketch sketch, IFeature? previewFeature)
    {
        var currentIds = new HashSet<Guid>(sketch.Features.Select(f => f.Id));
        if (previewFeature != null)
        {
            currentIds.Add(previewFeature.Id);
        }

        var keysToRemove = _featureMeshCache.Keys.Where(k => !currentIds.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            _featureMeshCache.Remove(key);
        }

        var featuresToRender = new List<IFeature>(sketch.Features.Where(f => f.IsVisible));
        if (previewFeature != null)
        {
            featuresToRender.Add(previewFeature);
        }

        var meshes = new List<TriangleMesh>(featuresToRender.Count);
        foreach (var f in featuresToRender)
        {
            if (!_featureMeshCache.TryGetValue(f.Id, out var mesh))
            {
                mesh = _geometryKernel.BuildSolidMesh(new[] { f }, _settingsProvider.GetSettings());
                if (mesh != null)
                {
                    _featureMeshCache[f.Id] = mesh;
                }
            }

            if (mesh != null) meshes.Add(mesh);
        }

        return TriangleMesh.Combine(meshes, includeFeatureNormals: false);
    }
}
