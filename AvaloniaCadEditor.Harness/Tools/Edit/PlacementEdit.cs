using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Harness.Tools.Edit;

/// <summary>
/// O caminho comum de mover/girar: achar a chamada <c>Add(...)</c>/<c>AddFeature(...)</c> da feature,
/// reescrever o posicionamento pelo <see cref="Application.Scenes.PartSourceEditor"/> (o mesmo que a UI usa
/// ao arrastar e girar) e devolver a caixa nova — um retorno geométrico, não só "ok".
/// </summary>
internal static class PlacementEdit
{
    public static async Task<string> ApplyAsync(CadToolContext context, string? target, string verb,
        Func<IFeature, string, int, string> edit, CancellationToken cancellationToken)
    {
        var (feature, error) = await TargetLookup.FindAsync(context, target, cancellationToken);
        if (feature == null) return error!;

        if (context.SourceOf(feature) is not { } source)
            return $"Erro: não sei em que linha '{feature.Name}' foi criada; edite o código com replace_in_file.";
        if (context.EditError(source.File) is { } scopeError)
            return $"Erro: '{feature.Name}' é criada em {source.File}, linha {source.Line}. {scopeError}";

        string? text = context.ReadFile(source.File);
        if (text == null) return $"Erro: o arquivo {source.File} não está disponível.";

        string edited;
        try
        {
            edited = edit(feature, text, source.Line);
        }
        catch (InvalidOperationException ex)
        {
            return $"Erro: {ex.Message} Edite com replace_in_file.";
        }
        if (edited == text) return $"Erro: o código de '{feature.Name}' não mudou ({source.File}, linha {source.Line}). Edite com replace_in_file.";

        context.WriteFile(source.File, edited);

        var (moved, moveError) = await TargetLookup.PrepareAsync(context, feature.Name, cancellationToken);
        if (moved == null) return $"Alterado {source.File} (linha {source.Line}), mas a cena não executa mais: {moveError}";

        string empty = moved.WorldMesh.TriangleCount == 0
            ? "\nAtenção: a malha ficou vazia depois da edição (um sólido com fillet não pode ser girado/espelhado como operando de booleana)."
            : "";
        return $"ok — {feature.Name} {verb} ({source.File}, linha {source.Line}).\n" + context.Inspection.DescribeBoundingBox(moved) + empty;
    }

    public static bool IsInstance(IFeature feature) => feature is PartInstance;
}
