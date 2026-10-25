using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Application.Export;

/// <summary>
/// Exporta a cena num formato de troca: coleta na thread de quem chama (as malhas já estão em cache) e
/// grava fora dela. Recusa enquanto a cena ainda constrói, em vez de gravar um arquivo faltando sólidos.
/// </summary>
public sealed class SceneExportService
{
    private readonly ISceneMeshManager _meshes;
    private readonly SceneExportCollector _collector;
    private readonly ISceneFileWriter _writer;

    public SceneExportService(ISceneMeshManager meshes, ISceneFileWriter writer)
    {
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _collector = new SceneExportCollector(meshes);
    }

    public async Task<SceneExportResult> ExportAsync(IReadOnlyList<IFeature> features, string path)
    {
        if (_meshes.PendingCount > 0)
            return SceneExportResult.Refused("a cena ainda está construindo; espere terminar e exporte de novo.");

        var scene = _collector.Collect(features);
        if (scene.Nodes.Count == 0)
            return SceneExportResult.Refused("nada para exportar (nenhum sólido visível ou selecionado tem geometria).");

        await Task.Run(() => _writer.WriteAsync(scene, path));
        return SceneExportResult.Written(scene);
    }
}

/// <summary>Resultado de <see cref="SceneExportService.ExportAsync"/>, com o texto para a barra de status.</summary>
public sealed record SceneExportResult(bool Succeeded, string Message)
{
    public static SceneExportResult Refused(string reason) => new(false, $"✗ Exportar: {reason}");

    public static SceneExportResult Written(ExportScene scene)
    {
        int instances = Count(scene.Nodes, n => n.ComponentId != null);
        int groups = Count(scene.Nodes, n => n.Mesh != null);
        var message = $"✓ Exportado: {instances} peça(s) em {scene.Components.Count} componente(s) e {groups} grupo(s).";
        if (scene.FilletsSuppressed) message += " Chanfros desligados: saíram sem chanfro.";
        if (scene.Skipped.Count > 0) message += $" Sem geometria, ficaram de fora: {string.Join(", ", scene.Skipped)}.";
        return new SceneExportResult(true, message);
    }

    private static int Count(IEnumerable<ExportNode> nodes, Func<ExportNode, bool> predicate) =>
        nodes.Sum(n => (predicate(n) ? 1 : 0) + Count(n.Children, predicate));
}
