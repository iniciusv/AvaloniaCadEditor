using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaCadEditor.Application.EditContext;

/// <summary>
/// Resultado de <see cref="PartSolidInserter.InsertAsync"/>: o arquivo da peça alterado, ou o motivo da
/// recusa, nunca os dois. Os documentos da cena (inclusive o arquivo de entrada) não mudam.
/// </summary>
/// <param name="GeometryVerified">False quando a geometria não pôde ser comparada (ver <see cref="PartMergeCheck"/>).</param>
public sealed record PartSolidInsertion(
    IReadOnlyList<ScriptDocument> Changes, string? Refusal, IReadOnlyList<string> Warnings, string? InsertedName, bool GeometryVerified)
{
    public static PartSolidInsertion Refuse(string reason) =>
        new(Array.Empty<ScriptDocument>(), reason, Array.Empty<string>(), null, false);
}

/// <summary>
/// Grava um sólido recém-gerado por uma ferramenta (em coordenadas do MUNDO, só com literais: ver
/// <c>ExtrusionTarget.Standalone</c>) direto no <c>Build()</c> da peça aberta, em coordenadas LOCAIS.
/// <para>
/// Não reescreve nada sozinho: compõe o trecho com a cena só em memória e usa o mesmo caminho do
/// "Incorporar à peça" (<see cref="IPartMerger"/> para a reescrita mundo → local e a inserção,
/// <see cref="PartMergeCheck"/> para conferir que o sólido ficou no mesmo lugar do mundo). Da mudança
/// devolvida pelo merger só vale o arquivo da peça: o trecho nunca chegou a entrar na cena de verdade.
/// </para>
/// Não grava nem aplica: quem chama aplica <see cref="PartSolidInsertion.Changes"/>.
/// </summary>
public sealed class PartSolidInserter
{
    private readonly ISceneEvaluator _evaluator;
    private readonly IPartMerger _merger;

    public PartSolidInserter(ISceneEvaluator evaluator, IPartMerger merger)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _merger = merger ?? throw new ArgumentNullException(nameof(merger));
    }

    /// <param name="documents">Os documentos atuais da cena.</param>
    /// <param name="entryFile">O arquivo de entrada (o que tem os top-level statements).</param>
    /// <param name="solidCode">Trecho de cena que declara o sólido e termina num <c>AddFeature(x)</c>.</param>
    /// <param name="context">A peça aberta; a instância é achada de novo pelo nome.</param>
    public async Task<PartSolidInsertion> InsertAsync(
        IReadOnlyList<ScriptDocument> documents, string entryFile, string solidCode, PartEditContext context,
        PartMergeMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(context);

        var entry = documents.FirstOrDefault(d => SamePath(d.FilePath, entryFile));
        if (entry == null) return PartSolidInsertion.Refuse($"O arquivo de entrada {entryFile} não faz parte da cena.");

        // 1. A cena com o trecho, só em memória: depois da última instrução de nível superior, que é
        //    onde C# aceita instruções (o arquivo pode terminar em declarações de tipo).
        if (FindRegistration(solidCode, variable: null) is not { } generated)
            return PartSolidInsertion.Refuse("O trecho gerado não registra um sólido com AddFeature(variavel).");
        string composedText = PartSourceEditor.InsertTopLevelStatement(entry.Text, solidCode.TrimEnd());
        if (FindRegistration(composedText, generated.Variable) is not { } registration)
            return PartSolidInsertion.Refuse($"Não achei o AddFeature({generated.Variable}) na cena composta.");
        int line = SourceText.From(composedText).Lines.GetLineFromPosition(registration.Position).LineNumber + 1;
        var composed = Replace(documents, entry.FilePath, composedText);

        // 2. O sólido como a ferramenta o fez, e a instância como está agora.
        var before = await _evaluator.EvaluateAsync(composed, cancellationToken);
        if (!before.Success) return PartSolidInsertion.Refuse($"O trecho gerado não executa: {FirstError(before)}");
        var solid = before.Features.FirstOrDefault(f => f.Name == registration.Variable);
        if (solid == null) return PartSolidInsertion.Refuse($"O trecho gerado não produziu '{registration.Variable}'.");
        var instances = before.Features.OfType<PartInstance>().Where(i => i.Name == context.InstanceName).ToList();
        if (instances.Count != 1)
            return PartSolidInsertion.Refuse($"A peça aberta '{context.InstanceName}' não está mais na cena.");
        var instance = instances[0];

        // 3. Reescrita mundo → local e inserção no Build().
        var merge = _merger.Merge(composed, new PartMergeRequest(
            entry.FilePath, line, instance.PartTypeName, instance.Placement, (solid as TransformedFeature)?.Placement, mode));
        if (merge.Refusal != null) return PartSolidInsertion.Refuse(merge.Refusal);

        // 4. Só o arquivo da peça muda; a cena volta a ser a original.
        var changes = merge.Changes.Where(c => !SamePath(c.FilePath, entry.FilePath)).ToList();
        if (changes.Count == 0) return PartSolidInsertion.Refuse("O merger não alterou o arquivo da peça.");
        var applied = documents.Select(d => changes.FirstOrDefault(c => SamePath(c.FilePath, d.FilePath)) ?? d).ToList();

        var after = await _evaluator.EvaluateAsync(applied, cancellationToken);
        var verdict = PartMergeCheck.Verify(solid, before.Features, after, instance.Name);
        if (verdict.Problem != null) return PartSolidInsertion.Refuse(verdict.Problem);

        var warnings = merge.Warnings.Concat(PartMergeWarnings.For(instance, before.Features)).ToList();
        return new PartSolidInsertion(changes, null, warnings, merge.InsertedName, verdict.GeometryVerified);
    }

    internal sealed record Registration(string Variable, int Position);

    /// <summary>
    /// O último <c>AddFeature(x)</c> (ou <c>AddFeature(x.Transformed(...))</c>) do código, ou o último de uma
    /// variável dada.
    /// </summary>
    internal static Registration? FindRegistration(string code, string? variable)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>().Reverse())
        {
            if (call.Expression is not IdentifierNameSyntax { Identifier.ValueText: "AddFeature" } || call.ArgumentList.Arguments.Count != 1)
                continue;
            string? name = call.ArgumentList.Arguments[0].Expression switch
            {
                IdentifierNameSyntax argument => argument.Identifier.ValueText,
                InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transformed", Expression: IdentifierNameSyntax moved }
                } => moved.Identifier.ValueText,
                _ => null
            };
            if (name != null && (variable == null || name == variable)) return new Registration(name, call.SpanStart);
        }
        return null;
    }

    /// <summary>Linha (a partir de 1) do registro de <paramref name="variable"/> no código; 0 se não houver.</summary>
    internal static int RegistrationLine(string code, string variable) =>
        FindRegistration(code, variable) is { } registration
            ? SourceText.From(code).Lines.GetLineFromPosition(registration.Position).LineNumber + 1
            : 0;

    private static IReadOnlyList<ScriptDocument> Replace(IReadOnlyList<ScriptDocument> documents, string file, string text) =>
        documents.Select(d => SamePath(d.FilePath, file) ? d with { Text = text } : d).ToList();

    internal static string FirstError(SceneEvaluationResult result) =>
        result.Diagnostics.FirstOrDefault(d => d.Severity == ScriptDiagnosticSeverity.Error)?.ToString()
        ?? result.ErrorMessage ?? "erro desconhecido";

    internal static bool SamePath(string a, string b) =>
        string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
