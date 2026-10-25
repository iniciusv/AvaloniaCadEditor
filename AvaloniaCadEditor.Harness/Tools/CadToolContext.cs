using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Context;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Harness.Inspection;

namespace AvaloniaCadEditor.Harness.Tools;

/// <summary>
/// O estado de um turno do harness: a cópia de trabalho dos arquivos que as ferramentas editam. Os
/// documentos reais não mudam — no fim do turno, <see cref="Changes"/> vira o
/// <see cref="CadScriptTurnResult.Changes"/>, e a UI aplica como sempre (um diff só, undo igual).
/// Cada edição sobe a <see cref="Revision"/>, o que invalida a avaliação e a inspeção em cache.
/// </summary>
public sealed class CadToolContext
{
    private readonly WorkingCopyEvaluator _evaluator;
    private readonly List<ScriptDocument> _changes = new();
    private (int Revision, SceneEvaluationResult Result)? _evaluation;

    public CadToolContext(CadScriptContext context, WorkingCopyEvaluator evaluator, SceneInspectionService inspection)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        Inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
        Inspection.Clear();
    }

    /// <summary>O contexto que a UI montou para o turno (arquivos, escopo, seleção).</summary>
    public CadScriptContext Context { get; }

    public SceneInspectionService Inspection { get; }

    /// <summary>Sobe a cada edição da cópia de trabalho.</summary>
    public int Revision { get; private set; }

    /// <summary>Texto completo de cada arquivo alterado no turno.</summary>
    public IReadOnlyList<ScriptDocument> Changes => _changes;

    public string ActiveFile => Context.FileName ?? ScriptDocument.DefaultSceneFile;

    /// <summary>O texto atual de um arquivo (com as edições do turno), ou null se ele não existe.</summary>
    public string? ReadFile(string path)
    {
        var changed = _changes.FirstOrDefault(d => Same(d.FilePath, path));
        if (changed != null) return changed.Text;
        if (Same(path, ActiveFile)) return Context.CurrentCode;

        return (Context.ProjectDocuments ?? Array.Empty<ScriptDocument>())
            .Concat(Context.Scope?.Documents ?? Array.Empty<ScriptDocument>())
            .FirstOrDefault(d => Same(d.FilePath, path))?.Text;
    }

    /// <summary>Por que o arquivo não pode ser alterado, ou null se pode (as mesmas regras do <see cref="AiEditScopePolicy"/>).</summary>
    public string? EditError(string path)
    {
        if (Context.Scope == null)
            return Same(path, ActiveFile) ? null : $"Só o arquivo ativo ({ActiveFile}) pode ser alterado.";

        return Context.Scope.Contains(path) || AiEditScopePolicy.IsNewPartFile(path, Context.ProjectDocuments)
            ? null
            : $"O arquivo {path} não está no contexto e não pode ser alterado.";
    }

    public void WriteFile(string path, string text)
    {
        if (EditError(path) is { } error) throw new InvalidOperationException(error);
        if (Context.Scope == null) path = ActiveFile;

        int index = _changes.FindIndex(d => Same(d.FilePath, path));
        var document = new ScriptDocument(index >= 0 ? _changes[index].FilePath : path, text);
        if (index >= 0) _changes[index] = document; else _changes.Add(document);

        Revision++;
        _evaluation = null;
        Inspection.Clear();
    }

    /// <summary>Arquivos completos devolvidos em blocos ```cadscript no fim do turno.</summary>
    public void ApplyChanges(IEnumerable<ScriptDocument> documents)
    {
        foreach (var document in documents) WriteFile(document.FilePath, document.Text);
    }

    /// <summary>A cena avaliada com a cópia de trabalho (em cache por revisão).</summary>
    public async Task<SceneEvaluationResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        if (_evaluation is { } cached && cached.Revision == Revision) return cached.Result;

        var result = await _evaluator.EvaluateAsync(ChangesForEvaluation(), Context, cancellationToken);
        _evaluation = (Revision, result);
        return result;
    }

    /// <summary>Validação (regras do CadScript) e avaliação da cópia de trabalho.</summary>
    public Task<WorkingCopyCheck> CheckAsync(CancellationToken cancellationToken = default) =>
        _evaluator.CheckAsync(ChangesForEvaluation(), Context, cancellationToken);

    /// <summary>
    /// Arquivo e linha onde a feature foi registrada, já como arquivo da cópia de trabalho. A
    /// avaliação de arquivo único chama o script de <see cref="ScriptDocument.DefaultSceneFile"/>.
    /// </summary>
    public (string File, int Line)? SourceOf(IFeature feature)
    {
        var origin = FeatureOrigin.Of(feature);
        if (origin == null) return null;
        string file = Context.Scope == null && Same(origin.FilePath, ScriptDocument.DefaultSceneFile) ? ActiveFile : origin.FilePath;
        return (file, origin.Line);
    }

    /// <summary>O arquivo de arquivo único precisa ir como primeiro documento, mesmo sem edição.</summary>
    private IReadOnlyList<ScriptDocument> ChangesForEvaluation() =>
        Context.Scope == null && _changes.Count == 0
            ? new[] { new ScriptDocument(ActiveFile, Context.CurrentCode) }
            : _changes;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
