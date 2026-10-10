using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Scenes;

/// <summary>
/// Confere, antes de gravar, que a cena convertida em peça (<see cref="IPartConverter"/>) produz o
/// mesmo sólido no mesmo lugar. A comparação é pelo <see cref="FeatureFingerprint"/>, que descreve a
/// geometria sem Id nem nome, então não precisa do kernel.
/// </summary>
public static class PartConversionCheck
{
    /// <param name="Problem">Por que a conversão não deve ser aplicada; null se pode.</param>
    /// <param name="GeometryVerified">
    /// False quando a geometria não tem fingerprint (fillet, extrusão de perfil): a cena executou,
    /// mas não dá para garantir que o sólido é idêntico.
    /// </param>
    public sealed record Verdict(string? Problem, bool GeometryVerified);

    /// <param name="before">O sólido solto convertido, como estava na última execução.</param>
    /// <param name="featuresBefore">As features de topo da última execução.</param>
    /// <param name="after">A execução da cena com a conversão aplicada.</param>
    /// <param name="typeName">O record criado.</param>
    public static Verdict Verify(IFeature before, IReadOnlyList<IFeature> featuresBefore, SceneEvaluationResult after, string typeName)
    {
        if (!after.Success)
        {
            var error = after.Diagnostics.FirstOrDefault(d => d.Severity == ScriptDiagnosticSeverity.Error);
            return new Verdict($"A cena convertida não executa: {error?.ToString() ?? after.ErrorMessage}", false);
        }

        var instances = after.Features.OfType<PartInstance>().Where(i => i.PartTypeName == typeName).ToList();
        if (instances.Count != 1)
            return new Verdict($"A cena convertida devia ter uma instância de {typeName} e tem {instances.Count}.", false);

        var namesBefore = featuresBefore.Select(f => f.Name).ToHashSet();
        var unexpected = after.Features.FirstOrDefault(f => !namesBefore.Contains(f.Name));
        if (unexpected != null)
            return new Verdict($"A conversão criaria um elemento que não existia: {unexpected.Name}.", false);

        string? expected = WorldKey(before);
        string? actual = FeatureFingerprint.Compute(new TransformedFeature(instances[0].Definition, instances[0].Placement));
        if (expected == null || actual == null) return new Verdict(null, false);
        return expected == actual
            ? new Verdict(null, true)
            : new Verdict("O sólido convertido não seria idêntico ao original; nada foi alterado.", false);
    }

    /// <summary>Solta ou já deslocada, a feature vista como "geometria + placement", no mesmo formato da instância.</summary>
    private static string? WorldKey(IFeature feature) =>
        FeatureFingerprint.Compute(feature as TransformedFeature ?? new TransformedFeature(feature, Placement.Identity));
}
