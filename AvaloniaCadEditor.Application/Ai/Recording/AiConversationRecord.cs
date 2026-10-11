using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Application.Ai.Recording;

/// <summary>
/// Uma conversa do chat de IA gravada em disco: o contexto exato de cada round, o texto enviado e a
/// resposta obtida. Serve para reexecutar os mesmos pedidos com outros perfis de pré-prompt ou modelos.
/// Nunca contém a chave de API.
/// </summary>
public sealed class AiConversationRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Modelo usado na gravação (o "model" da API).</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Perfil de pré-prompt usado na gravação.</summary>
    public string ProfileId { get; set; } = string.Empty;

    public List<RecordedRound> Rounds { get; set; } = new();
}

/// <summary>Um turno do chat: pedido + contexto enviado + resposta final (depois do auto-fix, se houve).</summary>
public sealed class RecordedRound
{
    public string Instruction { get; set; } = string.Empty;
    public RecordedContext Context { get; set; } = new();

    /// <summary>A mensagem de usuário exatamente como foi enviada (para auditoria; o replay a remonta do contexto).</summary>
    public string SentUserMessage { get; set; } = string.Empty;

    public RecordedResponse Response { get; set; } = new();
}

public sealed class RecordedResponse
{
    public string RawContent { get; set; } = string.Empty;
    public string ChatText { get; set; } = string.Empty;
    public List<ScriptDocument> Changes { get; set; } = new();
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
}

/// <summary>Espelho serializável do <see cref="CadScriptContext"/> (sem o ErrorFeedback, que só existe no auto-fix).</summary>
public sealed class RecordedContext
{
    public string CurrentCode { get; set; } = string.Empty;
    public string? SelectedFeatureName { get; set; }
    public CadFileKind Kind { get; set; } = CadFileKind.StandaloneScript;
    public string? FileName { get; set; }
    public List<string>? PartSummaries { get; set; }
    public List<ScriptDocument>? ProjectDocuments { get; set; }
    public RecordedScope? Scope { get; set; }

    public static RecordedContext From(CadScriptContext context) => new()
    {
        CurrentCode = context.CurrentCode,
        SelectedFeatureName = context.SelectedFeatureName,
        Kind = context.Kind,
        FileName = context.FileName,
        PartSummaries = context.PartSummaries?.ToList(),
        ProjectDocuments = context.ProjectDocuments?.ToList(),
        Scope = context.Scope == null ? null : new RecordedScope
        {
            Documents = context.Scope.Documents.ToList(),
            Kinds = new Dictionary<string, CadFileKind>(context.Scope.Kinds, StringComparer.OrdinalIgnoreCase),
            SelectedFeatures = context.Scope.SelectedFeatures.ToList()
        }
    };

    public CadScriptContext ToContext() => new(
        CurrentCode,
        SelectedFeatureName,
        ErrorFeedback: null,
        Kind,
        FileName,
        PartSummaries,
        ProjectDocuments,
        Scope == null ? null : new MultiFileScope(
            Scope.Documents,
            new Dictionary<string, CadFileKind>(Scope.Kinds, StringComparer.OrdinalIgnoreCase),
            Scope.SelectedFeatures));
}

public sealed class RecordedScope
{
    public List<ScriptDocument> Documents { get; set; } = new();
    public Dictionary<string, CadFileKind> Kinds { get; set; } = new();
    public List<string> SelectedFeatures { get; set; } = new();
}
