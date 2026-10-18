using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>
/// Caixa esperada de um sólido de topo da cena, no mundo (mm). Um check de <c>case.json</c>:
/// <c>"solidBounds": [{ "name": "pino", "min": [x, y, z], "max": [x, y, z], "tolerance": 0.1 }]</c>.
/// Serve para pedidos cuja resposta certa é uma posição — o que um regex no código não consegue medir.
/// </summary>
internal sealed class SolidBoundsSpec
{
    /// <summary>Nome do sólido (<c>Name</c>) ou da variável passada a <c>Add</c>/<c>AddFeature</c>.</summary>
    public string Name { get; set; } = string.Empty;
    public double[] Min { get; set; } = Array.Empty<double>();
    public double[] Max { get; set; } = Array.Empty<double>();
    public double Tolerance { get; set; } = 0.1;
}

/// <summary>
/// Checks que precisam da cena executada: avalia os arquivos do caso com as alterações da resposta
/// pelo mesmo <see cref="WorkingCopyEvaluator"/> do app e mede a malha pelo kernel OCCT.
/// </summary>
internal static class GeometryChecks
{
    public static async Task<IReadOnlyList<CheckResult>> EvaluateAsync(
        CaseChecks checks, CadScriptTurnResult result, RecordedContext recorded, ISceneEvaluator evaluator)
    {
        if (checks.SolidBounds is not { Count: > 0 } specs) return Array.Empty<CheckResult>();
        if (!result.IsValid) return specs.Select(s => new CheckResult(Title(s), false, "a resposta não é válida")).ToList();

        var context = recorded.ToContext();
        var changes = result.Changes.Count == 0 && !context.IsMultiFile
            ? new[] { new ScriptDocument(context.FileName ?? ScriptDocument.DefaultSceneFile, context.CurrentCode) }
            : result.Changes;

        var scene = await new WorkingCopyEvaluator(evaluator).EvaluateAsync(changes, context);
        if (!scene.Success) return specs.Select(s => new CheckResult(Title(s), false, scene.ErrorMessage)).ToList();

        var kernel = new OpenCascadeGeometryKernel();
        return specs.Select(spec => Check(spec, scene.Features, kernel)).ToList();
    }

    private static CheckResult Check(SolidBoundsSpec spec, IReadOnlyList<IFeature> features, OpenCascadeGeometryKernel kernel)
    {
        if (spec.Min.Length != 3 || spec.Max.Length != 3)
            return new CheckResult(Title(spec), false, "min e max precisam de 3 coordenadas");

        var feature = features.FirstOrDefault(f => f.Name == spec.Name || FeatureOrigin.VariableName(f) == spec.Name);
        if (feature == null)
            return new CheckResult(Title(spec), false, $"não há sólido '{spec.Name}' na cena ({string.Join(", ", features.Select(f => f.Name))})");

        var positions = kernel.BuildFeatureMesh(feature).Positions;
        if (positions.Length < 3) return new CheckResult(Title(spec), false, "o sólido não tem malha");

        var min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var max = new[] { double.MinValue, double.MinValue, double.MinValue };
        for (int i = 0; i + 2 < positions.Length; i += 3)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                min[axis] = Math.Min(min[axis], positions[i + axis]);
                max[axis] = Math.Max(max[axis], positions[i + axis]);
            }
        }

        bool ok = Enumerable.Range(0, 3).All(a =>
            Math.Abs(min[a] - spec.Min[a]) <= spec.Tolerance && Math.Abs(max[a] - spec.Max[a]) <= spec.Tolerance);
        return new CheckResult(Title(spec), ok, $"caixa {Box(min, max)}");
    }

    private static string Title(SolidBoundsSpec s) => $"{s.Name} em {Box(s.Min, s.Max)} ±{s.Tolerance:0.###}";

    private static string Box(double[] min, double[] max) =>
        min.Length == 3 && max.Length == 3
            ? FormattableString.Invariant($"({min[0]:0.###}, {min[1]:0.###}, {min[2]:0.###})–({max[0]:0.###}, {max[1]:0.###}, {max[2]:0.###})")
            : "?";
}
