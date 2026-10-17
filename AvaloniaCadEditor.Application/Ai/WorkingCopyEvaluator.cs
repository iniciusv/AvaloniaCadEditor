using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>Resultado da verificação de alterações propostas: o primeiro erro, ou a cena avaliada.</summary>
public sealed record WorkingCopyCheck(string? Error, SceneEvaluationResult? Evaluation)
{
    public bool IsValid => Error == null;
}

/// <summary>
/// Valida e avalia alterações propostas pela IA sobre os arquivos do contexto (a "cópia de
/// trabalho"), sem tocar nos documentos reais. É o mesmo caminho para qualquer sessão de chat.
/// </summary>
public sealed class WorkingCopyEvaluator
{
    private readonly ISceneEvaluator _evaluator;

    public WorkingCopyEvaluator(ISceneEvaluator evaluator)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    /// <summary>
    /// Valida a sintaxe/regras de cada arquivo alterado (<see cref="CadScriptValidator"/>) e,
    /// se tudo passar, avalia a cena com as alterações (<see cref="EvaluateAsync"/>).
    /// </summary>
    public async Task<WorkingCopyCheck> CheckAsync(IReadOnlyList<ScriptDocument> changes, CadScriptContext context, CancellationToken cancellationToken = default)
    {
        string? error = changes
            .Select(c => (c.FilePath, Result: CadScriptValidator.Validate(c.Text, context.KindOf(c.FilePath))))
            .Where(v => !v.Result.IsValid)
            .Select(v => context.IsMultiFile ? $"{v.FilePath}: {v.Result.Error}" : v.Result.Error)
            .FirstOrDefault();
        if (error != null) return new WorkingCopyCheck(error, null);

        var result = await EvaluateAsync(changes, context, cancellationToken);
        return result.Success
            ? new WorkingCopyCheck(null, result)
            : new WorkingCopyCheck(result.ErrorMessage ?? "Erro desconhecido ao executar o script.", result);
    }

    /// <summary>
    /// Avalia as alterações propostas no contexto certo. No modo multi-arquivo, a cena inteira
    /// (<see cref="CadScriptContext.ProjectDocuments"/>, com o arquivo de entrada real) é avaliada com as
    /// alterações sobrepostas. No modo de arquivo único, um script é avaliado sozinho (ou com os demais
    /// arquivos do projeto); um arquivo de peça não tem statements próprios para executar, então ganha um
    /// arquivo de entrada sintético que só instancia a peça (<c>Add(new Peca(...))</c>), forçando o
    /// <c>Build()</c> a rodar.
    /// </summary>
    public Task<SceneEvaluationResult> EvaluateAsync(IReadOnlyList<ScriptDocument> changes, CadScriptContext context, CancellationToken cancellationToken = default)
    {
        if (context.IsMultiFile)
        {
            var project = context.ProjectDocuments ?? context.Scope!.Documents;
            return _evaluator.EvaluateAsync(Overlay(project, changes), cancellationToken);
        }

        string code = changes[0].Text;
        string fileName = context.FileName ?? ScriptDocument.DefaultSceneFile;
        var others = context.ProjectDocuments ?? Array.Empty<ScriptDocument>();

        if (context.Kind != CadFileKind.PartFile)
        {
            if (others.Count == 0) return _evaluator.EvaluateAsync(code);

            var docs = new List<ScriptDocument> { new(fileName, code) };
            docs.AddRange(others.Where(d => !string.Equals(d.FilePath, fileName, StringComparison.OrdinalIgnoreCase)));
            return _evaluator.EvaluateAsync(docs, cancellationToken);
        }

        var partDoc = new ScriptDocument(fileName, code);
        var types = PartSourceEditor.FindPartTypes(new[] { partDoc });
        if (types.Count != 1)
        {
            return Task.FromResult(new SceneEvaluationResult(Array.Empty<IFeature>(), false, types.Count == 0
                ? "O arquivo não declara nenhum record que herda de Part."
                : $"O arquivo declara {types.Count} peças; deveria declarar exatamente uma."));
        }

        var partDocs = new List<ScriptDocument> { partDoc };
        partDocs.AddRange(others.Where(d => !string.Equals(d.FilePath, fileName, StringComparison.OrdinalIgnoreCase)));

        string entryFile = "__CadScriptPartCheck.cs";
        while (partDocs.Any(d => string.Equals(d.FilePath, entryFile, StringComparison.OrdinalIgnoreCase)))
        {
            entryFile = "_" + entryFile;
        }
        partDocs.Add(new ScriptDocument(entryFile, PartSourceEditor.BuildAddStatement(types[0])));

        return _evaluator.EvaluateAsync(partDocs, cancellationToken);
    }

    /// <summary>O contexto com as alterações propostas sobrepostas (para o round de correção).</summary>
    public static CadScriptContext WithProposedChanges(CadScriptContext context, IReadOnlyList<ScriptDocument> changes)
    {
        if (!context.IsMultiFile)
        {
            return context with { CurrentCode = changes.Count > 0 ? changes[0].Text : context.CurrentCode };
        }

        return context with
        {
            Scope = context.Scope! with { Documents = Overlay(context.Scope!.Documents, changes) },
            ProjectDocuments = context.ProjectDocuments == null ? null : Overlay(context.ProjectDocuments, changes)
        };
    }

    /// <summary><paramref name="documents"/> com o texto de <paramref name="changes"/> no lugar (e arquivos novos no fim).</summary>
    public static IReadOnlyList<ScriptDocument> Overlay(IReadOnlyList<ScriptDocument> documents, IReadOnlyList<ScriptDocument> changes)
    {
        var result = documents
            .Select(d => changes.FirstOrDefault(c => string.Equals(c.FilePath, d.FilePath, StringComparison.OrdinalIgnoreCase)) ?? d)
            .ToList();
        result.AddRange(changes.Where(c => !documents.Any(d => string.Equals(d.FilePath, c.FilePath, StringComparison.OrdinalIgnoreCase))));
        return result;
    }
}
