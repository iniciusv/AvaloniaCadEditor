using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;

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
public sealed record AiContextRequest(
    IReadOnlyList<ScriptDocument> Documents,
    string EntryFile,
    string? ActiveFile,
    IReadOnlyList<IFeature> Selected,
    bool IsProjectOpen = true,
    Func<string, string>? ToRelative = null,
    AiContextAdjustments? Adjustments = null);

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
        string? primarySelection = request.Selected.FirstOrDefault()?.Name;

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
            request.Selected.Select(f => DescribeSelected(f, toRelative)).ToList());

        var partSummaries = index.PartTypes
            .Where(t => !scope.Contains(t.FilePath))
            .Select(FormatPartSignature)
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

        foreach (var hit in SelectionFileResolver.Resolve(request.Selected, index, request.ToRelative ?? DefaultToRelative))
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

    /// <summary>Ex.: "Placa(double Largura, double Espessura = 5)".</summary>
    private static string FormatPartSignature(PartTypeInfo type) =>
        $"{type.Name}({string.Join(", ", type.Parameters.Select(p =>
            p.DefaultValue == null ? $"{p.TypeName} {p.Name}" : $"{p.TypeName} {p.Name} = {p.DefaultValue}"))})";
}
