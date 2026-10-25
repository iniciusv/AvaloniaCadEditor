using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Export;

/// <summary>
/// Decide como a cena chega ao programa de destino: cada <see cref="PartInstance"/> vira instância de
/// um componente (peças iguais compartilham a geometria), dentro de uma pasta pelo tipo da peça; cada
/// sólido solto vira um grupo na raiz. Só lê malhas já construídas, não grava nada.
/// </summary>
public sealed class SceneExportCollector
{
    /// <summary>Cor do sólido sem <see cref="IFeature.Color"/> na viewport.</summary>
    private const string DefaultColor = "#EBEBF2";

    private readonly ISceneMeshManager _meshes;

    public SceneExportCollector(ISceneMeshManager meshes)
    {
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
    }

    /// <summary>
    /// Com algo selecionado, só os selecionados; senão, tudo o que está visível (a mesma regra do STL).
    /// </summary>
    public ExportScene Collect(IReadOnlyList<IFeature> features)
    {
        var selected = features.Where(f => f.IsSelected).ToList();
        var chosen = selected.Count > 0 ? selected : features.Where(f => f.IsVisible).ToList();

        var build = new Build();
        foreach (var feature in chosen)
        {
            if (feature is PartInstance instance)
                AddInstance(build, instance);
            else
                AddLooseSolid(build, feature);
        }

        var nodes = new List<ExportNode>();
        foreach (var entry in build.Roots)
        {
            if (entry.Folder is { } folder)
            {
                // Todas as instâncias do tipo falharam: a pasta vazia só confundiria.
                if (folder.Children.Count > 0) nodes.Add(ExportNode.Folder(folder.Name, folder.Children));
            }
            else
            {
                nodes.Add(entry.Node!);
            }
        }

        return new ExportScene(nodes, build.Components, build.Materials.Values.ToList(), build.Skipped, _meshes.SuppressFillets);
    }

    private void AddInstance(Build build, PartInstance instance)
    {
        // A pasta primeiro: numa colisão, ela fica com o nome do tipo e a instância ganha o sufixo.
        var folder = build.FolderFor(instance.PartTypeName);
        string name = build.NodeNames.Take(instance.Name, "peca");
        string materialId = build.MaterialFor(instance.Color);

        // Um componente espelhado teria escala negativa, e cada programa a interpreta de um jeito
        // (faces do avesso). A malha do mundo já sai com a ordem dos vértices corrigida.
        if (instance.Placement.IsMirrored)
        {
            var world = _meshes.GetMeshFor(instance);
            if (world.TriangleCount == 0) { build.Skipped.Add(instance.Name); return; }
            folder.Children.Add(ExportNode.Group(name, world, materialId));
            return;
        }

        var key = (instance.Definition.Id, materialId);
        if (!build.ComponentByKey.TryGetValue(key, out var componentId))
        {
            var local = _meshes.GetMeshFor(instance.Definition);
            if (local.TriangleCount == 0) { build.Skipped.Add(instance.Name); return; }

            componentId = $"componente_{build.Components.Count}";
            build.Components.Add(new ExportComponent(componentId, build.ComponentNames.Take(instance.PartTypeName, "peca"), local, materialId));
            build.ComponentByKey[key] = componentId;
        }

        folder.Children.Add(ExportNode.Instance(name, MatrixOf(instance.Placement), componentId));
    }

    private void AddLooseSolid(Build build, IFeature feature)
    {
        var mesh = _meshes.GetMeshFor(feature);
        if (mesh.TriangleCount == 0)
        {
            // Plano de trabalho e esboço não têm malha por natureza: só avisa de quem falhou ao construir.
            if (_meshes.BuildProblems.ContainsKey(feature.Id)) build.Skipped.Add(feature.Name);
            return;
        }

        build.Roots.Add(new RootEntry(ExportNode.Group(build.NodeNames.Take(feature.Name, "solido"), mesh, build.MaterialFor(feature.Color)), null));
    }

    /// <summary>Matriz 4x4 linha a linha: colunas = imagens dos eixos locais, última coluna = translação.</summary>
    internal static double[] MatrixOf(Placement placement)
    {
        var x = placement.ApplyVector(Vector3D.UnitX);
        var y = placement.ApplyVector(Vector3D.UnitY);
        var z = placement.ApplyVector(Vector3D.UnitZ);
        var t = placement.Translation;
        return
        [
            x.X, y.X, z.X, t.X,
            x.Y, y.Y, z.Y, t.Y,
            x.Z, y.Z, z.Z, t.Z,
            0, 0, 0, 1,
        ];
    }

    private sealed record RootEntry(ExportNode? Node, FolderEntry? Folder);

    private sealed class FolderEntry(string name)
    {
        public string Name { get; } = name;
        public List<ExportNode> Children { get; } = [];
    }

    /// <summary>Estado de uma coleta: nada aqui sobrevive à chamada.</summary>
    private sealed class Build
    {
        public List<RootEntry> Roots { get; } = [];
        public List<ExportComponent> Components { get; } = [];
        public Dictionary<(Guid Definition, string Material), string> ComponentByKey { get; } = [];
        public Dictionary<string, ExportMaterial> Materials { get; } = new(StringComparer.Ordinal);
        public List<string> Skipped { get; } = [];
        public UniqueNames NodeNames { get; } = new();
        public UniqueNames ComponentNames { get; } = new();
        private readonly Dictionary<string, FolderEntry> _folders = new(StringComparer.Ordinal);

        public FolderEntry FolderFor(string partType)
        {
            if (!_folders.TryGetValue(partType, out var folder))
            {
                folder = new FolderEntry(NodeNames.Take(partType, "pecas"));
                _folders[partType] = folder;
                Roots.Add(new RootEntry(null, folder));
            }
            return folder;
        }

        /// <summary>Um material por cor distinta; cor ausente ou inválida usa a padrão da viewport.</summary>
        public string MaterialFor(string? color)
        {
            var (hex, r, g, b) = ParseColor(color) ?? ParseColor(DefaultColor)!.Value;
            if (!Materials.TryGetValue(hex, out var material))
            {
                material = new ExportMaterial($"material_{Materials.Count}", hex, r, g, b);
                Materials[hex] = material;
            }
            return material.Id;
        }
    }

    /// <summary>Aceita #RRGGBB e #AARRGGBB, como a viewport; o alfa é descartado.</summary>
    private static (string Hex, byte R, byte G, byte B)? ParseColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color) || color[0] != '#' || (color.Length != 7 && color.Length != 9)) return null;
        var rgb = color.Substring(color.Length - 6);
        if (!byte.TryParse(rgb.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(rgb.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(rgb.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return null;
        return ("#" + rgb.ToUpperInvariant(), r, g, b);
    }

    /// <summary>Nomes únicos: o segundo "x" vira "x_2", o terceiro "x_3".</summary>
    private sealed class UniqueNames
    {
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        private int _unnamed;

        public string Take(string? name, string fallbackPrefix)
        {
            var baseName = string.IsNullOrWhiteSpace(name) ? $"{fallbackPrefix}_{++_unnamed}" : name.Trim();
            var candidate = baseName;
            for (int n = 2; !_used.Add(candidate); n++) candidate = $"{baseName}_{n}";
            return candidate;
        }
    }
}
