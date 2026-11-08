using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Ai.Context;

/// <summary>Por que um arquivo entrou no contexto enviado à IA.</summary>
public enum AiContextReason
{
    /// <summary>Arquivo de entrada da cena (sempre incluído).</summary>
    Entry,

    /// <summary>Arquivo aberto no editor.</summary>
    Active,

    /// <summary>Declara ou instancia um sólido selecionado.</summary>
    Selection,

    /// <summary>Declara um tipo usado por outro arquivo do contexto (ex.: Medidas, uma peça usada no Build()).</summary>
    Dependency,

    /// <summary>Adicionado/fixado manualmente pelo usuário.</summary>
    Pinned
}

/// <summary>Um arquivo do contexto da IA.</summary>
/// <param name="FilePath">Caminho relativo à cena (ex.: "Pecas/TrilhoMgn15.cs").</param>
/// <param name="Reason">Primeiro motivo pelo qual o arquivo entrou.</param>
/// <param name="Detail">Complemento para o usuário (ex.: o sólido selecionado ou o tipo usado).</param>
public sealed record AiContextFile(string FilePath, AiContextReason Reason, string? Detail = null);

/// <summary>Os arquivos incluídos no contexto, em ordem estável.</summary>
public sealed record AiContextScope(IReadOnlyList<AiContextFile> Files)
{
    public static readonly AiContextScope Empty = new(Array.Empty<AiContextFile>());

    public bool Contains(string filePath) =>
        Files.Any(f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
}

/// <summary>O estado do editor de que o contexto da IA depende.</summary>
/// <param name="Documents">Fontes atuais da cena (com o texto ainda não salvo); na cena de arquivo único, só ela.</param>
/// <param name="EntryFile">Arquivo de entrada (statements top-level).</param>
/// <param name="ActiveFile">Arquivo aberto no editor; null = o de entrada.</param>
/// <param name="Selected">Sólidos selecionados na viewport.</param>
/// <param name="IsProjectOpen">False = cena de arquivo único: a IA edita só esse arquivo.</param>
/// <param name="ToRelative">Normaliza <see cref="SourceOrigin.FilePath"/> para o formato de <paramref name="Documents"/>.</param>
/// <param name="Adjustments">Arquivos fixados/excluídos pelo usuário.</param>
/// <param name="OpenEntity">
/// A entidade aberta com duplo clique, se houver. Lá dentro o que se seleciona são faces, então sem isto a IA
/// não saberia em que peça o usuário está: ela entra como seleção, com o aviso de que está aberta.
/// </param>
/// <param name="SelectedFace">A face selecionada dentro da entidade aberta, se houver.</param>
public sealed record AiContextRequest(
    IReadOnlyList<ScriptDocument> Documents,
    string EntryFile,
    string? ActiveFile,
    IReadOnlyList<IFeature> Selected,
    bool IsProjectOpen = true,
    Func<string, string>? ToRelative = null,
    AiContextAdjustments? Adjustments = null,
    IFeature? OpenEntity = null,
    AvaloniaCadEditor.Domain.Profiles.IProfile? SelectedFace = null)
{
    /// <summary>A seleção mais a entidade aberta (se não estava selecionada), que é o que a IA trata como selecionado.</summary>
    public IReadOnlyList<IFeature> SelectedWithOpenEntity =>
        OpenEntity == null || Selected.Any(f => f.Name == OpenEntity.Name) ? Selected : Selected.Append(OpenEntity).ToList();
}

/// <summary>Monta o que a IA recebe a partir do estado do editor.</summary>
public interface IAiContextBuilder
{
    /// <summary>Só os arquivos que irão para a IA (para mostrar ao usuário). Vazio sem projeto aberto.</summary>
    AiContextScope ResolveScope(AiContextRequest request);

    /// <summary>O contexto completo de um turno da conversa.</summary>
    CadScriptContext Build(AiContextRequest request);
}

/// <summary>
/// Decide quais arquivos de uma cena com vários arquivos vão, com o código completo, para a IA — o
/// arquivo de entrada, o ativo, os arquivos dos sólidos selecionados (<see cref="SelectionFileResolver"/>),
/// os fixados e as dependências desses (<see cref="TypeDependencyResolver"/>) — e monta o
/// <see cref="CadScriptContext"/>. As peças que ficam de fora vão só como assinatura.
/// </summary>
public sealed class AiContextBuilder : IAiContextBuilder
{
    public AiContextScope ResolveScope(AiContextRequest request) =>
        request.IsProjectOpen ? ResolveScope(new SceneSyntaxIndex(request.Documents), request) : AiContextScope.Empty;

    public CadScriptContext Build(AiContextRequest request)
    {
        string activeFile = request.ActiveFile ?? request.EntryFile;
        string? primarySelection = request.SelectedWithOpenEntity.FirstOrDefault()?.Name;

        if (!request.IsProjectOpen)
        {
            string code = request.Documents.FirstOrDefault()?.Text ?? string.Empty;
            return new CadScriptContext(code, primarySelection, Kind: CadFileKind.StandaloneScript, FileName: ScriptDocument.DefaultSceneFile);
        }

        var index = new SceneSyntaxIndex(request.Documents);
        var scope = ResolveScope(index, request);
        var toRelative = request.ToRelative ?? DefaultToRelative;

        var kinds = request.Documents.ToDictionary(
            d => d.FilePath,
            d => AiEditScopePolicy.KindOf(d.FilePath, request.EntryFile, index),
            StringComparer.OrdinalIgnoreCase);

        var multiFile = new MultiFileScope(
            scope.Files.Select(f => index.Find(f.FilePath)!).ToList(),
            kinds,
            DescribeSelection(request, toRelative));

        var partSummaries = index.PartTypes
            .Where(t => !scope.Contains(t.FilePath))
            .Select(t => FormatPartSignature(t, index.Find(t.FilePath)?.Text))
            .ToList();

        return new CadScriptContext(
            index.Find(activeFile)?.Text ?? string.Empty,
            primarySelection,
            Kind: kinds.TryGetValue(activeFile, out var kind) ? kind : CadFileKind.PartFile,
            FileName: activeFile,
            PartSummaries: partSummaries,
            ProjectDocuments: request.Documents,
            Scope: multiFile);
    }

    private static AiContextScope ResolveScope(SceneSyntaxIndex index, AiContextRequest request)
    {
        var excluded = new HashSet<string>(request.Adjustments?.Excluded ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
        excluded.Remove(request.EntryFile);

        var files = new List<AiContextFile>();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Include(string? path, AiContextReason reason, string? detail = null)
        {
            if (index.Find(path) is not { } doc || excluded.Contains(doc.FilePath) || !included.Add(doc.FilePath)) return;
            files.Add(new AiContextFile(doc.FilePath, reason, detail));
        }

        Include(request.EntryFile, AiContextReason.Entry);
        Include(request.ActiveFile, AiContextReason.Active);

        foreach (var hit in SelectionFileResolver.Resolve(request.SelectedWithOpenEntity, index, request.ToRelative ?? DefaultToRelative))
        {
            Include(hit.FilePath, AiContextReason.Selection, hit.Detail);
        }

        foreach (var path in request.Adjustments?.Pinned ?? new HashSet<string>())
        {
            Include(path, AiContextReason.Pinned);
        }

        foreach (var hit in TypeDependencyResolver.Resolve(files.Select(f => f.FilePath).ToList(), index, request.EntryFile, excluded))
        {
            Include(hit.FilePath, AiContextReason.Dependency, hit.Detail);
        }

        return new AiContextScope(files);
    }

    private static string DefaultToRelative(string path) => path.Replace('\\', '/');

    /// <summary>
    /// As linhas de "SÓLIDOS SELECIONADOS": cada sólido, a entidade aberta marcada como tal (e, numa peça, que o
    /// Build() é local) e a face selecionada dentro dela, nas coordenadas em que o código está escrito.
    /// </summary>
    private static IReadOnlyList<string> DescribeSelection(AiContextRequest request, Func<string, string> toRelative)
    {
        var lines = new List<string>();
        foreach (var feature in request.SelectedWithOpenEntity)
        {
            string text = DescribeSelected(feature, toRelative);
            if (request.OpenEntity != null && feature.Name == request.OpenEntity.Name)
            {
                text += feature is PartInstance instance
                    ? $" · ABERTA para edição: o usuário está modelando dentro de {instance.PartTypeName}; o que ele pedir vai no Build() dela, em coordenadas locais"
                    : " · ABERTA para edição: o usuário está modelando dentro deste sólido";
            }
            lines.Add(text);
        }

        if (request.SelectedFace is { } face && request.OpenEntity is { } open)
            lines.Add(DescribeFace(face, open));
        return lines;
    }

    /// <summary>
    /// Ex.: "face de bloco: normal (0, 0, 1), centro (20, 15, 10), local de Bloco". Numa peça, nas coordenadas do
    /// Build(); num sólido solto, nas do mundo, que é onde o código dele está.
    /// </summary>
    internal static string DescribeFace(AvaloniaCadEditor.Domain.Profiles.IProfile face, IFeature openEntity)
    {
        var toCode = openEntity is PartInstance instance ? instance.Placement.Inverse() : Placement.Identity;
        var vertices = face.GetVertices(32);
        var center = vertices.Count == 0
            ? face.Plane.Origin
            : new Point3D(vertices.Average(v => v.X), vertices.Average(v => v.Y), vertices.Average(v => v.Z));
        var c = toCode.Apply(center);
        var n = toCode.ApplyVector(face.Plane.Normal);
        string frame = openEntity is PartInstance part ? $"local de {part.PartTypeName}" : "mundo";
        return $"face de {openEntity.Name}: normal ({N(n.X)}, {N(n.Y)}, {N(n.Z)}), centro ({N(c.X)}, {N(c.Y)}, {N(c.Z)}), {frame}";

        static string N(double v) => AvaloniaCadEditor.Application.Scenes.PlacementSource.Number(Math.Round(v, 3));
    }

    /// <summary>Ex.: "trilho_mgn15_y_a (peça TrilhoMgn15) — Cena.cs, linha 23".</summary>
    private static string DescribeSelected(IFeature feature, Func<string, string> toRelative)
    {
        string text = feature is PartInstance instance
            ? $"{feature.Name} (peça {instance.PartTypeName})"
            : $"{feature.Name} ({feature.GetType().Name})";
        if (FeatureOrigin.Of(feature) is { } origin)
        {
            text += $" — {toRelative(origin.FilePath)}, linha {origin.Line}";
        }
        return text;
    }

    /// <summary>
    /// Ex.: "Placa(double Largura, double Espessura = 5)", e na linha de baixo o resumo do comentário
    /// da peça (<see cref="PartDocSummary"/>) — o que ela é e onde fica a origem local dela.
    /// </summary>
    private static string FormatPartSignature(PartTypeInfo type, string? source)
    {
        string signature = $"{type.Name}({string.Join(", ", type.Parameters.Select(p =>
            p.DefaultValue == null ? $"{p.TypeName} {p.Name}" : $"{p.TypeName} {p.Name} = {p.DefaultValue}"))})";
        string? summary = source == null ? null : PartDocSummary.Extract(source, type.Name);
        return summary == null ? signature : $"{signature}\n    — {summary}";
    }
}
