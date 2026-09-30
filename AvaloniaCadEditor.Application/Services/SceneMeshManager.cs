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

    public RaycastHit? Raycast3D(Sketch sketch, Ray ray, Func<IFeature, bool>? includeFilter = null)
    {
        if (sketch.Features.Count == 0) return null;

        float bestDist = float.MaxValue;
        RaycastHit? bestHit = null;

        foreach (var feature in sketch.Features)
        {
            bool shouldProcess = feature.IsVisible || (includeFilter != null && includeFilter(feature));
            if (!shouldProcess) continue;

            if (!_featureMeshCache.TryGetValue(feature.Id, out var mesh))
            {
                mesh = _geometryKernel.BuildSolidMesh(new[] { feature }, _settingsProvider.GetSettings());
                if (mesh != null)
                {
                    _featureMeshCache[feature.Id] = mesh;
                }
            }

            if (mesh != null)
            {
                var hit = ray.IntersectMesh(mesh);
                if (hit.HasValue && hit.Value.Distance < bestDist)
                {
                    bestDist = hit.Value.Distance;
                    bestHit = new RaycastHit(
                        hit.Value.Distance,
                        hit.Value.Point,
                        hit.Value.Normal,
                        feature.Id);
                }
            }
        }

        return bestHit;
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

        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();
        var featureEdges = new List<uint>();
        var internalEdges = new List<uint>();
        uint vertexOffset = 0;

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

            if (mesh == null || mesh.VertexCount == 0) continue;

            positions.AddRange(mesh.Positions);
            normals.AddRange(mesh.Normals);

            foreach (var idx in mesh.Indices) indices.Add(idx + vertexOffset);
            foreach (var fe in mesh.FeatureEdges) featureEdges.Add(fe + vertexOffset);
            foreach (var ie in mesh.InternalEdges) internalEdges.Add(ie + vertexOffset);

            vertexOffset += (uint)mesh.VertexCount;
        }

        return new TriangleMesh(positions.ToArray(), normals.ToArray(), indices.ToArray(), featureEdges.ToArray(), internalEdges.ToArray());
    }
}
