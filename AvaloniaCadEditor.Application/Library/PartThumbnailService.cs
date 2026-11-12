using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Application.Library;

/// <summary>
/// Miniatura de cada peça do catálogo, com os parâmetros padrão: uma cena sintética de uma linha
/// (<c>Add(new Tipo());</c>) mais o arquivo da peça e suas dependências, avaliada e construída em segundo
/// plano, rasterizada em CPU (<see cref="ThumbnailRasterizer"/>) e guardada em cache pelo hash do conteúdo.
/// <para>
/// Usa um kernel próprio, criado na primeira miniatura: o da UI não é thread-safe e o cache dele é por
/// execução da cena. Uma miniatura por vez. Peça que não compila ou não constrói devolve null (a tela fica
/// com o ícone), e a falha é lembrada pela chave para não ser refeita a cada atualização da lista.
/// </para>
/// </summary>
public sealed class PartThumbnailService
{
    /// <summary>Lado da imagem, em pixels.</summary>
    public const int Size = 128;

    private const string EntryFile = "__Miniatura.cs";

    private readonly ISceneEvaluator _evaluator;
    private readonly Func<IGeometryKernel> _kernelFactory;
    private readonly IThumbnailCache _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private IGeometryKernel? _kernel;

    public PartThumbnailService(ISceneEvaluator evaluator, Func<IGeometryKernel> kernelFactory, IThumbnailCache cache)
    {
        _evaluator = evaluator;
        _kernelFactory = kernelFactory;
        _cache = cache;
    }

    /// <summary>Chave do cache: muda quando muda o texto da peça ou de qualquer dependência.</summary>
    public static string KeyOf(string typeName, IReadOnlyList<ScriptDocument> files)
    {
        var text = new StringBuilder("v1\n").Append(typeName).Append('\n');
        foreach (var file in files.OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            text.Append(file.FilePath).Append('\n').Append(file.Text.Replace("\r\n", "\n")).Append('\0');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    /// <summary>A imagem PNG da peça, ou null quando ela não constrói.</summary>
    /// <param name="source">O grafo dos arquivos da origem do item (biblioteca ou cena), para achar as dependências.</param>
    public async Task<byte[]?> GetAsync(PartLibraryItem item, PartDependencyIndex source, CancellationToken cancellation)
    {
        var files = source.Dependencies(item.Name, out var ambiguity);
        if (ambiguity != null || files.Count == 0) return null;

        string key = KeyOf(item.Name, files);
        var cached = _cache.Get(key);
        if (cached != null) return cached;

        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            lock (_failed) { if (_failed.Contains(key)) return null; }
            cached = _cache.Get(key);
            if (cached != null) return cached;

            var png = await Task.Run(() => RenderAsync(item, files, cancellation), cancellation).ConfigureAwait(false);
            if (png == null)
            {
                lock (_failed) _failed.Add(key);
                return null;
            }
            _cache.Put(key, png);
            return png;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]?> RenderAsync(PartLibraryItem item, IReadOnlyList<ScriptDocument> files, CancellationToken cancellation)
    {
        // Um arquivo com instruções de nível superior entre as dependências seria uma segunda entrada.
        var documents = files.Where(f => !string.Equals(f.FilePath, EntryFile, StringComparison.OrdinalIgnoreCase)).ToList();
        documents.Add(new ScriptDocument(EntryFile, PartSourceEditor.BuildAddStatement(item.Type)));

        var result = await _evaluator.EvaluateAsync(documents, cancellation).ConfigureAwait(false);
        if (!result.Success) return null;

        _kernel ??= _kernelFactory();
        var meshes = new List<TriangleMesh>();
        foreach (var feature in result.Features)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!feature.IsVisible || feature.IsTransientTool) continue;
            try
            {
                meshes.Add(_kernel.BuildFeatureMesh(feature));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // O kernel não deveria lançar (registra em BuildProblems); se lançar, a peça fica com o ícone.
                return null;
            }
        }

        var mesh = TriangleMesh.Combine(meshes, includeFeatureNormals: false);
        if (mesh.TriangleCount == 0) return null;
        return ThumbnailRasterizer.EncodePng(ThumbnailRasterizer.Render(mesh, Size), Size, Size);
    }
}
