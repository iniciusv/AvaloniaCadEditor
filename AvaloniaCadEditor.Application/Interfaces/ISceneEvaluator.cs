using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Application.Interfaces;

/// <summary>Um arquivo de código que faz parte da cena.</summary>
/// <param name="FilePath">Caminho usado em diagnósticos e stack traces (ex.: "Cena.cs").</param>
/// <param name="Text">Conteúdo do arquivo.</param>
public sealed record ScriptDocument(string FilePath, string Text)
{
    /// <summary>Nome usado quando a cena é um único texto sem arquivo associado.</summary>
    public const string DefaultSceneFile = "Cena.cs";
}

public enum ScriptDiagnosticSeverity
{
    Error,
    Warning
}

/// <summary>Erro ou aviso localizado em um arquivo da cena (linha e coluna começam em 1).</summary>
public sealed record ScriptDiagnostic(
    string FilePath,
    int Line,
    int Column,
    ScriptDiagnosticSeverity Severity,
    string Id,
    string Message)
{
    public override string ToString()
    {
        string severity = Severity == ScriptDiagnosticSeverity.Error ? "error" : "warning";
        return $"{FilePath}({Line},{Column}): {severity} {Id}: {Message}";
    }
}

public record SceneEvaluationResult(
    IReadOnlyList<IFeature> Features,
    bool Success,
    string? ErrorMessage = null
)
{
    /// <summary>Erros e avisos de compilação ou execução, por arquivo.</summary>
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; } = Array.Empty<ScriptDiagnostic>();
}

public interface ISceneEvaluator
{
    /// <summary>Avalia uma cena de arquivo único (nomeado <see cref="ScriptDocument.DefaultSceneFile"/>).</summary>
    Task<SceneEvaluationResult> EvaluateAsync(string code);

    /// <summary>
    /// Compila todos os documentos juntos e executa a cena. No máximo um documento pode conter
    /// top-level statements (é o ponto de entrada); os demais declaram tipos usados por ele.
    /// </summary>
    Task<SceneEvaluationResult> EvaluateAsync(IReadOnlyList<ScriptDocument> documents, CancellationToken cancellationToken = default);
}
