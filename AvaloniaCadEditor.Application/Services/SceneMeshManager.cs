using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

/// <summary>
/// Malhas da cena, por Id de feature, construídas pelo <see cref="IGeometryKernel"/>.
/// <para>
/// Duas threads usam isto: a da UI (desenho, pick, árvore) e a de <see cref="PrepareSceneAsync"/>, que
/// constrói a cena nova enquanto a antiga continua na tela. O kernel não é thread-safe, então toda
/// chamada a ele passa por <see cref="_kernelGate"/>; os caches de malha são concorrentes para que um
/// acerto de cache na UI nunca espere por uma construção longa (um chanfro por malha pode levar minutos).
/// </para>
/// </summary>
public class SceneMeshManager : ISceneMeshManager
{
    private readonly IGeometryKernel _geometryKernel;
    private readonly ITessellationSettingsProvider _settingsProvider;
    private readonly ConcurrentDictionary<Guid, TriangleMesh> _meshCache = new();
    private readonly ConcurrentDictionary<Guid, TriangleMesh> _filletsSuppressedMeshCache = new();
    private readonly object _kernelGate = new();
    private IReadOnlyDictionary<Guid, string> _buildProblemsSnapshot = new Dictionary<Guid, string>();

    /// <summary>O cache da versão da cena que está valendo (com ou sem chanfros/fillets).</summary>
    private ConcurrentDictionary<Guid, TriangleMesh> ActiveMeshCache => SuppressFillets ? _filletsSuppressedMeshCache : _meshCache;

    private readonly Func<Action, CancellationToken, Task> _runInBackground;

    /// <param name="runInBackground">Onde <see cref="PrepareSceneAsync"/> roda;
    /// padrão: <see cref="Task.Run(Action, CancellationToken)"/>. Testes passam um executor em linha para que
    /// uma execução da cena termine sem esperar outra thread.</param>
    public SceneMeshManager(IGeometryKernel geometryKernel, ITessellationSettingsProvider settingsProvider,
        Func<Action, CancellationToken, Task>? runInBackground = null)
    {
        _geometryKernel = geometryKernel;
        _settingsProvider = settingsProvider;
        _runInBackground = runInBackground ?? ((work, cancellation) => Task.Run(work, cancellation));
    }

    public bool SuppressFillets
    {
        get => _geometryKernel.SuppressFillets;
        set
        {
            lock (_kernelGate) _geometryKernel.SuppressFillets = value;
        }
    }

    public void ClearCache()
    {
        ClearMeshCaches();
        lock (_kernelGate) _geometryKernel.InvalidateAll();
    }

    public void ResetForNewScene()
    {
        ClearMeshCaches();
        lock (_kernelGate) _geometryKernel.BeginSceneRebuild();
    }

    private void ClearMeshCaches()
    {
        _meshCache.Clear();
        _filletsSuppressedMeshCache.Clear();
    }

    /// <summary>
    /// Features que <see cref="PrepareSceneAsync"/> ainda vai construir. Quem pede a malha de uma delas na
    /// UI recebe "nada ainda" em vez de esperar pelo kernel: a cena aparece sem ela e ela entra quando
    /// ficar pronta. Trocado por inteiro a cada cena; nunca esvaziado por cancelamento (ver
    /// <see cref="ISceneMeshManager.PendingCount"/>).
    /// </summary>
    private ConcurrentDictionary<Guid, byte> _pending = new();

    /// <inheritdoc />
    public int PendingCount => _pending.Count;

    private bool IsPending(Guid featureId) => _pending.ContainsKey(featureId);

    public Task PrepareSceneAsync(IReadOnlyList<IFeature> features, CancellationToken cancellation, IReadOnlyList<IFeature>? hidden = null)
    {
        // Antes de sair para a outra thread: a cena pode ir para a tela antes de a construção começar.
        var pending = new ConcurrentDictionary<Guid, byte>(features.Select(f => new KeyValuePair<Guid, byte>(f.Id, 0)));
        _pending = pending;

        return _runInBackground(() =>
        {
            var settings = _settingsProvider.GetSettings();
            // Aqui e não na UI: com uma construção cancelada ainda terminando a etapa em curso, esperar
            // pelo kernel na thread da UI a travaria.
            lock (_kernelGate)
            {
                _geometryKernel.BeginSceneRebuild();
                if (hidden != null) _geometryKernel.RetainGeometry(hidden, settings);
            }
            // A versão sem fillets não está na tela: não há o que manter dela.
            InactiveMeshCache.Clear();

            BuildPending(features, pending, settings, cancellation);
        }, cancellation);
    }

    public Task PrepareFeaturesAsync(IReadOnlyList<IFeature> features, CancellationToken cancellation)
    {
        var missing = features.Where(f => !ActiveMeshCache.ContainsKey(f.Id)).ToList();
        if (missing.Count == 0) return Task.CompletedTask;

        // No conjunto da cena que está valendo: uma cena nova o troca, e esta construção já não vale para ela.
        var pending = _pending;
        foreach (var feature in missing) pending[feature.Id] = 0;

        return _runInBackground(() => BuildPending(missing, pending, _settingsProvider.GetSettings(), cancellation), cancellation);
    }

    private void BuildPending(IReadOnlyList<IFeature> features, ConcurrentDictionary<Guid, byte> pending,
        Domain.Settings.TessellationSettings settings, CancellationToken cancellation)
    {
        foreach (var feature in features)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!ActiveMeshCache.ContainsKey(feature.Id))
            {
                // Uma feature por vez: entre uma e outra a UI consegue o kernel se precisar dele.
                var mesh = BuildMesh(feature, settings, cancellation);
                if (mesh != null) ActiveMeshCache[feature.Id] = mesh;
            }
            // Depois do cache: quem vê a feature fora da lista já acha a malha.
            pending.TryRemove(feature.Id, out _);
        }
    }

    private ConcurrentDictionary<Guid, TriangleMesh> InactiveMeshCache => SuppressFillets ? _meshCache : _filletsSuppressedMeshCache;

    private TriangleMesh? BuildMesh(IFeature feature, Domain.Settings.TessellationSettings settings, CancellationToken cancellation = default)
    {
        lock (_kernelGate)
        {
            _geometryKernel.BuildCancellation = cancellation;
            try
            {
                return _geometryKernel.BuildSolidMesh(new[] { feature }, settings);
            }
            finally
            {
                _geometryKernel.BuildCancellation = default;
            }
        }
    }

    public TriangleMesh GetFeatureMesh(Sketch sketch, Guid featureId, IFeature? previewFeature, bool forceRebuild = false)
    {
        if (forceRebuild)
        {
            lock (_kernelGate) _geometryKernel.InvalidateShape(featureId);
            ActiveMeshCache.TryRemove(featureId, out _);
        }

        if (!forceRebuild && ActiveMeshCache.TryGetValue(featureId, out var mesh))
        {
            return mesh;
        }

        if (IsPending(featureId)) return TriangleMesh.Empty;

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

        mesh = BuildMesh(feature, _settingsProvider.GetSettings());
        if (mesh != null && !forceRebuild)
        {
            ActiveMeshCache[featureId] = mesh;
        }
        return mesh ?? TriangleMesh.Empty;
    }

    public void InvalidateFeatureAndDependents(Sketch sketch, Guid featureId)
    {
        _meshCache.TryRemove(featureId, out _);
        _filletsSuppressedMeshCache.TryRemove(featureId, out _);
        lock (_kernelGate) _geometryKernel.InvalidateShape(featureId);

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
        if (!ActiveMeshCache.TryGetValue(feature.Id, out var mesh))
        {
            if (IsPending(feature.Id)) return null;
            mesh = BuildMesh(feature, _settingsProvider.GetSettings());
            if (mesh != null)
            {
                ActiveMeshCache[feature.Id] = mesh;
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
                yield return new RaycastHit(hit.Value.Distance, hit.Value.Point, hit.Value.Normal, feature.Id, hit.Value.TriangleIndex);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uma cópia, tirada só se o kernel estiver livre: o dicionário dele muda durante uma construção em
    /// segundo plano, e esperar por ela travaria a UI. Ocupado, vale a última cópia.
    /// </remarks>
    public IReadOnlyDictionary<Guid, string> BuildProblems
    {
        get
        {
            if (Monitor.TryEnter(_kernelGate))
            {
                try
                {
                    _buildProblemsSnapshot = new Dictionary<Guid, string>(_geometryKernel.BuildProblems);
                }
                finally
                {
                    Monitor.Exit(_kernelGate);
                }
            }
            return _buildProblemsSnapshot;
        }
    }

    public TriangleMesh BuildCompositeMesh(Sketch sketch, IFeature? previewFeature)
    {
        var currentIds = new HashSet<Guid>(sketch.Features.Select(f => f.Id));
        if (previewFeature != null)
        {
            currentIds.Add(previewFeature.Id);
        }

        var keysToRemove = ActiveMeshCache.Keys.Where(k => !currentIds.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            ActiveMeshCache.TryRemove(key, out _);
        }

        var featuresToRender = new List<IFeature>(sketch.Features.Where(f => f.IsVisible));
        if (previewFeature != null)
        {
            featuresToRender.Add(previewFeature);
        }

        var settings = _settingsProvider.GetSettings();
        var meshes = new List<TriangleMesh>(featuresToRender.Count);
        foreach (var f in featuresToRender)
        {
            if (!ActiveMeshCache.TryGetValue(f.Id, out var mesh))
            {
                if (IsPending(f.Id)) continue; // entra quando PrepareSceneAsync terminar de construí-la
                mesh = BuildMesh(f, settings);
                if (mesh != null)
                {
                    ActiveMeshCache[f.Id] = mesh;
                }
            }

            if (mesh != null) meshes.Add(mesh);
        }

        return TriangleMesh.Combine(meshes, includeFeatureNormals: false);
    }
}
