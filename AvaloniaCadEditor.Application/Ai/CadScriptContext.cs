using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Qual dos dois formatos de arquivo de uma cena está ativo. Uma cena de arquivo único (ou o
/// arquivo de entrada de um projeto com vários arquivos) usa statements soltos + AddFeature/Add;
/// um arquivo em <c>Pecas/</c> declara um único <c>record</c> que herda de <c>Part</c>.
/// </summary>
public enum CadFileKind
{
    /// <summary>Cena de arquivo único, sem projeto (pasta) aberto.</summary>
    StandaloneScript,

    /// <summary>Arquivo de entrada de um projeto com vários arquivos (ex.: Cena.cs).</summary>
    SceneEntry,

    /// <summary>Arquivo de peça (em Pecas/): um record que herda de Part, sem statements soltos.</summary>
    PartFile,

    /// <summary>Arquivo auxiliar de um projeto (ex.: Pecas/Medidas.cs): declara tipos/constantes, sem statements soltos nem peças.</summary>
    SupportFile
}

/// <summary>
/// Contexto de domínio passado à IA a cada round da conversa: o código C# vigente do arquivo ativo,
/// que formato esse arquivo segue (<see cref="Kind"/>), a feature atualmente selecionada na UI (se
/// houver) e, em rounds de correção automática, o erro retornado pela última tentativa.
/// </summary>
/// <param name="CurrentCode">Código vigente do arquivo ativo (<see cref="FileName"/>).</param>
/// <param name="SelectedFeatureName">Nome da feature selecionada na UI, se houver.</param>
/// <param name="ErrorFeedback">Erro da tentativa anterior, em rounds de correção automática.</param>
/// <param name="Kind">Formato esperado do arquivo ativo.</param>
/// <param name="FileName">Caminho do arquivo ativo, relativo à cena (ex.: "Cena.cs", "Pecas/Nema17.cs").</param>
/// <param name="PartSummaries">Assinaturas das peças já declaradas na cena (ex.: "Placa(double Largura, double Comprimento, double Espessura = 5)"), para a IA reaproveitá-las em vez de redeclará-las.</param>
/// <param name="ProjectDocuments">
/// Demais arquivos do projeto (sem o arquivo ativo), usados só para validar/executar a resposta da IA
/// no contexto completo da cena — não são reenviados à IA como texto (isso é <see cref="PartSummaries"/>).
/// </param>
/// <param name="Scope">
/// Modo multi-arquivo: os arquivos enviados à IA com o texto completo e que ela pode alterar (montado por
/// <see cref="Context.AiContextBuilder"/>). Null = modo de arquivo único (só <see cref="CurrentCode"/>).
/// </param>
public sealed record CadScriptContext(
    string CurrentCode,
    string? SelectedFeatureName = null,
    string? ErrorFeedback = null,
    CadFileKind Kind = CadFileKind.StandaloneScript,
    string? FileName = null,
    IReadOnlyList<string>? PartSummaries = null,
    IReadOnlyList<ScriptDocument>? ProjectDocuments = null,
    MultiFileScope? Scope = null)
{
    /// <summary>True quando a IA recebe e pode devolver vários arquivos (<see cref="Scope"/>).</summary>
    public bool IsMultiFile => Scope != null;

    /// <summary>Formato de um arquivo: no modo de arquivo único, sempre <see cref="Kind"/>.</summary>
    public CadFileKind KindOf(string filePath) => Scope?.KindOf(filePath) ?? Kind;
}

/// <summary>Os arquivos que a IA recebe e pode alterar no modo multi-arquivo.</summary>
/// <param name="Documents">Arquivos enviados com o texto completo (entrada, arquivos da seleção, dependências...).</param>
/// <param name="Kinds">Formato de cada arquivo do projeto, já calculado. Arquivo ausente (novo) = peça.</param>
/// <param name="SelectedFeatures">Sólidos selecionados, já descritos (ex.: "trilho_a (peça TrilhoMgn15) — Cena.cs, linha 23").</param>
public sealed record MultiFileScope(
    IReadOnlyList<ScriptDocument> Documents,
    IReadOnlyDictionary<string, CadFileKind> Kinds,
    IReadOnlyList<string> SelectedFeatures)
{
    public CadFileKind KindOf(string filePath) =>
        Kinds.TryGetValue(filePath, out var kind) ? kind : CadFileKind.PartFile;

    public bool Contains(string filePath) =>
        Documents.Any(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Um arquivo devolvido pela IA. <paramref name="FilePath"/> null = o arquivo ativo (bloco sem <c>file=</c>).</summary>
public sealed record CadScriptFileEdit(string? FilePath, string Code);

/// <summary>Payload de uma resposta da IA: o conteúdo completo de cada arquivo alterado.</summary>
public sealed record CadScriptChanges(IReadOnlyList<CadScriptFileEdit> Files);
