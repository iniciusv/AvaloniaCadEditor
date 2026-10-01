using System.Collections.Generic;
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
    PartFile
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
public sealed record CadScriptContext(
    string CurrentCode,
    string? SelectedFeatureName = null,
    string? ErrorFeedback = null,
    CadFileKind Kind = CadFileKind.StandaloneScript,
    string? FileName = null,
    IReadOnlyList<string>? PartSummaries = null,
    IReadOnlyList<ScriptDocument>? ProjectDocuments = null);
