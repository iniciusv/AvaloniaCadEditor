using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;

namespace AvaloniaCadEditor.Application.Ai.Context;

/// <summary>Um arquivo encontrado para o contexto e o porquê (ex.: o sólido ou o tipo que o trouxe).</summary>
public sealed record ContextFileHit(string FilePath, string Detail);

/// <summary>
/// De onde vêm, no código, os sólidos selecionados: o arquivo da chamada <c>Add</c>/<c>AddFeature</c>
/// (<see cref="FeatureOrigin"/>) e, para uma instância de peça, o arquivo que declara a peça. Desce
/// nos operandos de booleanas, transformações e fillets.
/// </summary>
public static class SelectionFileResolver
{
    /// <param name="toRelative">Normaliza <see cref="SourceOrigin.FilePath"/> para o formato dos caminhos do índice.</param>
    public static IReadOnlyList<ContextFileHit> Resolve(
        IEnumerable<IFeature> selected,
        SceneSyntaxIndex index,
        Func<string, string> toRelative)
    {
        var hits = new List<ContextFileHit>();
        var visited = new HashSet<IFeature>(ReferenceEqualityComparer.Instance);

        void Visit(IFeature? feature, string selectedName)
        {
            if (feature == null || !visited.Add(feature)) return;

            if (FeatureOrigin.Of(feature) is { } origin && !string.IsNullOrEmpty(origin.FilePath))
            {
                hits.Add(new ContextFileHit(toRelative(origin.FilePath), selectedName));
            }

            switch (feature)
            {
                case PartInstance instance:
                    if (index.FileDeclaringPart(instance.PartTypeName) is { } partFile)
                    {
                        hits.Add(new ContextFileHit(partFile, $"{selectedName} ({instance.PartTypeName})"));
                    }
                    break;
                case BooleanFeature boolean:
                    foreach (var operand in boolean.BaseFeatures) Visit(operand, selectedName);
                    foreach (var operand in boolean.ToolFeatures) Visit(operand, selectedName);
                    break;
                case TransformedFeature transformed:
                    Visit(transformed.Source, selectedName);
                    break;
                case FilletFeature fillet:
                    Visit(fillet.BaseFeature, selectedName);
                    break;
            }
        }

        foreach (var feature in selected)
        {
            Visit(feature, feature.Name);
        }
        return hits;
    }
}
