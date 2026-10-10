using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Scenes;

/// <summary>
/// Confere, antes de gravar, que o sólido incorporado a uma peça (<see cref="IPartMerger"/>) continua
/// exatamente onde estava no mundo. O <see cref="FeatureFingerprint"/> não serve aqui: ele compara os
/// bits dos parâmetros, e a incorporação reescreve as coordenadas (mundo → local) de propósito. Então a
/// comparação é geométrica: cada primitiva é levada para o mundo (pelo placement da instância e pelos
/// <see cref="TransformedFeature"/> do caminho) e comparada com tolerância.
/// </summary>
public static class PartMergeCheck
{
    /// <summary>Tolerância em mm: a reescrita arredonda as coordenadas a 6 casas.</summary>
    public const double Tolerance = 1e-5;

    /// <param name="Problem">Por que a incorporação não deve ser aplicada; null se pode.</param>
    /// <param name="GeometryVerified">
    /// False quando a geometria tem tipos que não sabemos comparar (fillet, extrusão de perfil, varredura):
    /// a cena executou, mas não dá para garantir que o sólido ficou no mesmo lugar.
    /// </param>
    public sealed record Verdict(string? Problem, bool GeometryVerified);

    /// <param name="before">O sólido solto, como estava na última execução.</param>
    /// <param name="featuresBefore">As features de topo da última execução.</param>
    /// <param name="after">A execução da cena com a incorporação aplicada.</param>
    /// <param name="targetName">O nome da instância da peça que recebeu o sólido.</param>
    public static Verdict Verify(IFeature before, IReadOnlyList<IFeature> featuresBefore, SceneEvaluationResult after, string targetName)
    {
        if (!after.Success)
        {
            var error = after.Diagnostics.FirstOrDefault(d => d.Severity == ScriptDiagnosticSeverity.Error);
            return new Verdict($"A cena com o sólido incorporado não executa: {error?.ToString() ?? after.ErrorMessage}", false);
        }

        if (after.Features.Any(f => f.Name == before.Name))
            return new Verdict($"'{before.Name}' continuaria solto na cena.", false);

        var targets = after.Features.OfType<PartInstance>().Where(i => i.Name == targetName).ToList();
        if (targets.Count != 1)
            return new Verdict($"A cena devia ter uma instância '{targetName}' e tem {targets.Count}.", false);
        var target = targets[0];

        var namesBefore = featuresBefore.Select(f => f.Name).ToHashSet();
        var unexpected = after.Features.FirstOrDefault(f => !namesBefore.Contains(f.Name));
        if (unexpected != null)
            return new Verdict($"A incorporação criaria um elemento que não existia: {unexpected.Name}.", false);

        var found = Find(target.Definition, target.Placement, before.Name);
        if (found == null)
            return new Verdict($"'{before.Name}' não apareceu dentro de {target.PartTypeName}.", false);

        return Compare(before, Placement.Identity, found.Value.Feature, found.Value.Placement) switch
        {
            Match.Same => new Verdict(null, true),
            Match.Unknown => new Verdict(null, false),
            _ => new Verdict($"'{before.Name}' não ficaria no mesmo lugar dentro de {target.PartTypeName}; nada foi alterado.", false)
        };
    }

    private enum Match { Same, Different, Unknown }

    /// <summary>A feature com esse nome dentro da definição, com o placement que a leva para o mundo.</summary>
    private static (IFeature Feature, Placement Placement)? Find(IFeature feature, Placement toWorld, string name)
    {
        if (feature.Name == name) return (feature, toWorld);
        switch (feature)
        {
            case TransformedFeature transformed:
                return Find(transformed.Source, transformed.Placement.Then(toWorld), name);
            case BooleanFeature boolean:
                foreach (var child in boolean.BaseFeatures.Concat(boolean.ToolFeatures))
                {
                    if (Find(child, toWorld, name) is { } hit) return hit;
                }
                return null;
            case FilletFeature fillet:
                return Find(fillet.BaseFeature, toWorld, name);
            default:
                return null; // não desce em sub-peças: o sólido incorporado está no Build() desta
        }
    }

    private static Match Compare(IFeature a, Placement pa, IFeature b, Placement pb)
    {
        (a, pa) = Unwrap(a, pa);
        (b, pb) = Unwrap(b, pb);
        if (a.GetType() != b.GetType()) return Match.Different;

        switch (a, b)
        {
            case (CylinderFeature ca, CylinderFeature cb):
                return Same(pa.Apply(ca.GlobalBaseCenter), pb.Apply(cb.GlobalBaseCenter)) &&
                       Same(pa.ApplyVector(ca.GlobalDirection), pb.ApplyVector(cb.GlobalDirection)) &&
                       Close(ca.Radius, cb.Radius) && Close(ca.Height, cb.Height)
                    ? Match.Same : Match.Different;

            case (ConeFeature ca, ConeFeature cb):
                return Same(pa.Apply(ca.GlobalBaseCenter), pb.Apply(cb.GlobalBaseCenter)) &&
                       Same(pa.ApplyVector(ca.GlobalDirection), pb.ApplyVector(cb.GlobalDirection)) &&
                       Close(ca.BottomRadius, cb.BottomRadius) && Close(ca.TopRadius, cb.TopRadius) && Close(ca.Height, cb.Height)
                    ? Match.Same : Match.Different;

            case (BoxFeature, BoxFeature):
            case (PrismFeature, PrismFeature):
                return SameVertices(a.GetVertices().Select(pa.Apply).ToList(), b.GetVertices().Select(pb.Apply).ToList())
                    ? Match.Same : Match.Different;

            case (BooleanFeature ba, BooleanFeature bb):
                if (ba.Operation != bb.Operation ||
                    ba.BaseFeatures.Count != bb.BaseFeatures.Count || ba.ToolFeatures.Count != bb.ToolFeatures.Count)
                    return Match.Different;
                var result = Match.Same;
                foreach (var (x, y) in ba.BaseFeatures.Zip(bb.BaseFeatures).Concat(ba.ToolFeatures.Zip(bb.ToolFeatures)))
                {
                    var child = Compare(x, pa, y, pb);
                    if (child == Match.Different) return Match.Different;
                    if (child == Match.Unknown) result = Match.Unknown;
                }
                return result;

            default:
                return Match.Unknown;
        }
    }

    /// <summary>Desce pelos <see cref="TransformedFeature"/>/<see cref="PartInstance"/>, acumulando o placement.</summary>
    private static (IFeature, Placement) Unwrap(IFeature feature, Placement toWorld)
    {
        while (true)
        {
            switch (feature)
            {
                case TransformedFeature transformed:
                    toWorld = transformed.Placement.Then(toWorld);
                    feature = transformed.Source;
                    continue;
                case PartInstance instance:
                    toWorld = instance.Placement.Then(toWorld);
                    feature = instance.Definition;
                    continue;
                default:
                    return (feature, toWorld);
            }
        }
    }

    /// <summary>Mesmo conjunto de vértices (a ordem pode mudar, ex.: uma caixa girada 90°).</summary>
    private static bool SameVertices(IReadOnlyList<Point3D> a, IReadOnlyList<Point3D> b) =>
        a.Count == b.Count && a.All(p => b.Any(q => Same(p, q))) && b.All(q => a.Any(p => Same(p, q)));

    private static bool Same(Point3D a, Point3D b) => Close(a.X, b.X) && Close(a.Y, b.Y) && Close(a.Z, b.Z);

    private static bool Same(Vector3D a, Vector3D b) => Close(a.X, b.X) && Close(a.Y, b.Y) && Close(a.Z, b.Z);

    private static bool Close(double a, double b) => Math.Abs(a - b) < Tolerance;
}

/// <summary>
/// Avisos de uma incorporação que não impedem aplicá-la, mas mudam o que o usuário vai ver: o
/// <c>Build()</c> é compartilhado por todas as instâncias da peça, e o espelhamento da peça (feito à
/// mão, por parâmetro) não alcança um sólido escrito com coordenadas literais.
/// </summary>
public static class PartMergeWarnings
{
    public static IReadOnlyList<string> For(PartInstance target, IEnumerable<IFeature> sceneFeatures)
    {
        var warnings = new List<string>();

        var others = sceneFeatures.OfType<PartInstance>()
            .Where(i => !ReferenceEquals(i, target) && i.PartTypeName == target.PartTypeName)
            .Select(i => i.Name)
            .ToList();
        if (others.Count > 0)
            warnings.Add($"O sólido também vai aparecer em {string.Join(", ", others)}: todas as instâncias de {target.PartTypeName} compartilham o Build().");

        var mirrors = target.Part.GetParameters()
            .Where(p => p.Type == typeof(bool) && p.Name.StartsWith("Espelhad", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (mirrors.Count > 0)
        {
            string flags = string.Join(", ", mirrors.Select(p => $"{p.Name} = {((bool?)p.Value == true ? "true" : "false")}"));
            warnings.Add($"{target.PartTypeName} tem espelhamento por parâmetro ({flags}); o sólido incorporado usa coordenadas fixas e não acompanha esse espelho.");
        }
        return warnings;
    }
}
