using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>
/// Caixa esperada de um sólido de topo da cena, no mundo (mm). Um check de <c>case.json</c>:
/// <c>"solidBounds": [{ "name": "pino", "min": [x, y, z], "max": [x, y, z], "tolerance": 0.1 }]</c>.
/// <c>null</c> numa coordenada = não conferir aquele lado (ex.: <c>"min": [null, null, 16]</c> só exige a
/// base em z = 16). Serve para pedidos cuja resposta certa é uma posição.
/// </summary>
internal sealed class SolidBoundsSpec
{
    /// <summary>Nome do sólido (<c>Name</c>) ou da variável passada a <c>Add</c>/<c>AddFeature</c>.</summary>
    public string Name { get; set; } = string.Empty;
    public double?[] Min { get; set; } = Array.Empty<double?>();
    public double?[] Max { get; set; } = Array.Empty<double?>();
    public double Tolerance { get; set; } = 0.1;
    public CheckLevel Level { get; set; } = CheckLevel.Conceito;
}

/// <summary>
/// Pontos do mundo que devem estar dentro do material de um sólido (<c>inside</c>) ou fora dele
/// (<c>outside</c>): o eixo de um furo de fixação, a folga de um encaixe, a chapa encostada numa face.
/// <c>"solidPoints": [{ "name": "placa", "outside": [[x, y, z]], "inside": [[x, y, z]] }]</c>.
/// Diferente de <see cref="SolidBoundsSpec"/>, aceita qualquer solução que encaixe (outra rotação,
/// outra espessura), não uma posição só.
/// </summary>
internal sealed class SolidPointsSpec
{
    public string Name { get; set; } = string.Empty;
    public List<double[]> Inside { get; set; } = new();
    public List<double[]> Outside { get; set; } = new();
    public CheckLevel Level { get; set; } = CheckLevel.Conceito;
}

/// <summary>
/// A caixa de um sólido tem de caber numa região do mundo e, se <c>size</c> vier, ter esse tamanho:
/// <c>"solidWithin": [{ "name": "motor", "min": [x, y, z], "max": [x, y, z], "size": [42.3, 42.3, 66] }]</c>.
/// <c>null</c> = lado ou eixo livre. Confere o entendimento sem impor a cota exata: "em pé, sob o tubo,
/// entre o chão e o tubo" aceita qualquer altura nesse vão. A cota exata fica num <see cref="SolidBoundsSpec"/>
/// de nível <c>ajuste</c>.
/// </summary>
internal sealed class SolidWithinSpec
{
    public string Name { get; set; } = string.Empty;
    public double?[] Min { get; set; } = { null, null, null };
    public double?[] Max { get; set; } = { null, null, null };
    public double?[]? Size { get; set; }
    public double Tolerance { get; set; } = 0.5;

    /// <summary>Uma frase do que a região significa, para o relatório (ex.: "em pé, sob o tubo").</summary>
    public string? Meaning { get; set; }

    public CheckLevel Level { get; set; } = CheckLevel.Conceito;
}

/// <summary>
/// Checks que precisam da cena executada: avalia os arquivos do caso com as alterações da resposta
/// pelo mesmo <see cref="WorkingCopyEvaluator"/> do app e mede a malha pelo kernel OCCT.
/// </summary>
internal static class GeometryChecks
{
    /// <summary>Direções dos raios do teste de ponto dentro/fora: tortas, para não passar rente a arestas.</summary>
    private static readonly (double X, double Y, double Z)[] RayDirections =
    {
        (0.5773, 0.4142, 0.7032), (-0.3127, 0.8411, 0.4413), (0.6293, -0.5437, -0.5552)
    };

    public static async Task<IReadOnlyList<CheckResult>> EvaluateAsync(
        CaseChecks checks, CadScriptTurnResult result, RecordedContext recorded, ISceneEvaluator evaluator)
    {
        var bounds = checks.SolidBounds ?? new();
        var points = checks.SolidPoints ?? new();
        var within = checks.SolidWithin ?? new();
        if (bounds.Count == 0 && points.Count == 0 && within.Count == 0) return Array.Empty<CheckResult>();

        var titles = bounds.Select(b => (Title: Title(b), b.Level))
            .Concat(points.Select(p => (Title: Title(p), p.Level)))
            .Concat(within.Select(w => (Title: Title(w), w.Level))).ToList();
        if (!result.IsValid) return titles.Select(t => new CheckResult(t.Title, false, "a resposta não é válida", t.Level)).ToList();

        var context = recorded.ToContext();
        var changes = result.Changes.Count == 0 && !context.IsMultiFile
            ? new[] { new ScriptDocument(context.FileName ?? ScriptDocument.DefaultSceneFile, context.CurrentCode) }
            : result.Changes;

        var scene = await new WorkingCopyEvaluator(evaluator).EvaluateAsync(changes, context);
        if (!scene.Success) return titles.Select(t => new CheckResult(t.Title, false, scene.ErrorMessage, t.Level)).ToList();

        var kernel = new OpenCascadeGeometryKernel();
        var meshes = new Dictionary<string, TriangleMesh?>();
        TriangleMesh? MeshOf(string name)
        {
            if (meshes.TryGetValue(name, out var cached)) return cached;
            var feature = scene.Features.FirstOrDefault(f => f.Name == name || FeatureOrigin.VariableName(f) == name);
            return meshes[name] = feature == null ? null : kernel.BuildFeatureMesh(feature);
        }
        string Missing(string name) => $"não há sólido '{name}' na cena ({string.Join(", ", scene.Features.Select(f => f.Name))})";

        var results = new List<CheckResult>();
        foreach (var spec in bounds)
        {
            var mesh = MeshOf(spec.Name);
            results.Add(mesh == null ? new CheckResult(Title(spec), false, Missing(spec.Name), spec.Level) : Check(spec, mesh));
        }
        foreach (var spec in points)
        {
            var mesh = MeshOf(spec.Name);
            results.Add(mesh == null ? new CheckResult(Title(spec), false, Missing(spec.Name), spec.Level) : Check(spec, mesh));
        }
        foreach (var spec in within)
        {
            var mesh = MeshOf(spec.Name);
            results.Add(mesh == null ? new CheckResult(Title(spec), false, Missing(spec.Name), spec.Level) : Check(spec, mesh));
        }
        return results;
    }

    private static CheckResult Check(SolidBoundsSpec spec, TriangleMesh mesh)
    {
        if (spec.Min.Length != 3 || spec.Max.Length != 3)
            return new CheckResult(Title(spec), false, "min e max precisam de 3 coordenadas (null = não conferir)", spec.Level);

        if (BoxOf(mesh) is not { } box) return new CheckResult(Title(spec), false, "o sólido não tem malha", spec.Level);
        var (min, max) = box;

        bool Near(double? expected, double? actual) => expected == null || Math.Abs(actual!.Value - expected.Value) <= spec.Tolerance;
        bool ok = Enumerable.Range(0, 3).All(a => Near(spec.Min[a], min[a]) && Near(spec.Max[a], max[a]));
        return new CheckResult(Title(spec), ok, $"caixa {Box(min, max)}", spec.Level);
    }

    private static CheckResult Check(SolidWithinSpec spec, TriangleMesh mesh)
    {
        if (spec.Min.Length != 3 || spec.Max.Length != 3 || spec.Size is { Length: not 3 })
            return new CheckResult(Title(spec), false, "min, max e size precisam de 3 coordenadas (null = livre)", spec.Level);

        if (BoxOf(mesh) is not { } box) return new CheckResult(Title(spec), false, "o sólido não tem malha", spec.Level);
        var (min, max) = box;

        var wrong = new List<string>();
        string[] axes = { "x", "y", "z" };
        for (int a = 0; a < 3; a++)
        {
            double lo = min[a]!.Value, hi = max[a]!.Value;
            if (spec.Min[a] is double minAllowed && lo < minAllowed - spec.Tolerance)
                wrong.Add(FormattableString.Invariant($"{axes[a]} mín {lo:0.###} < {minAllowed:0.###}"));
            if (spec.Max[a] is double maxAllowed && hi > maxAllowed + spec.Tolerance)
                wrong.Add(FormattableString.Invariant($"{axes[a]} máx {hi:0.###} > {maxAllowed:0.###}"));
            if (spec.Size?[a] is double size && Math.Abs(hi - lo - size) > spec.Tolerance)
                wrong.Add(FormattableString.Invariant($"tamanho em {axes[a]} {hi - lo:0.###} ≠ {size:0.###}"));
        }
        string detail = $"caixa {Box(min, max)}";
        return new CheckResult(Title(spec), wrong.Count == 0, wrong.Count == 0 ? detail : $"{detail}: {string.Join("; ", wrong)}", spec.Level);
    }

    /// <summary>A caixa da malha no mundo, ou null sem malha.</summary>
    private static (double?[] Min, double?[] Max)? BoxOf(TriangleMesh mesh)
    {
        var positions = mesh.Positions;
        if (positions.Length < 3) return null;

        var min = new double?[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var max = new double?[] { double.MinValue, double.MinValue, double.MinValue };
        for (int i = 0; i + 2 < positions.Length; i += 3)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                min[axis] = Math.Min(min[axis]!.Value, positions[i + axis]);
                max[axis] = Math.Max(max[axis]!.Value, positions[i + axis]);
            }
        }
        return (min, max);
    }

    private static CheckResult Check(SolidPointsSpec spec, TriangleMesh mesh)
    {
        var wrong = new List<string>();
        foreach (var p in spec.Inside.Where(p => !IsInside(mesh, p))) wrong.Add($"{P(p)} devia estar no material");
        foreach (var p in spec.Outside.Where(p => IsInside(mesh, p))) wrong.Add($"{P(p)} devia estar vazio");

        int total = spec.Inside.Count + spec.Outside.Count;
        return new CheckResult(Title(spec), wrong.Count == 0,
            wrong.Count == 0 ? $"{total} pontos ok" : $"{wrong.Count}/{total} errados: {string.Join("; ", wrong)}", spec.Level);
    }

    /// <summary>Ponto dentro do sólido: paridade das interseções de um raio com a malha (maioria de 3 raios).</summary>
    private static bool IsInside(TriangleMesh mesh, double[] point)
    {
        if (point.Length != 3) return false;
        int votes = RayDirections.Count(d => Crossings(mesh, point, d) % 2 == 1);
        return votes >= 2;
    }

    /// <summary>Quantos triângulos o raio cruza (Möller–Trumbore, só à frente da origem).</summary>
    private static int Crossings(TriangleMesh mesh, double[] o, (double X, double Y, double Z) d)
    {
        var p = mesh.Positions;
        var idx = mesh.Indices;
        int count = 0;
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            int a = (int)idx[t] * 3, b = (int)idx[t + 1] * 3, c = (int)idx[t + 2] * 3;
            double e1x = p[b] - p[a], e1y = p[b + 1] - p[a + 1], e1z = p[b + 2] - p[a + 2];
            double e2x = p[c] - p[a], e2y = p[c + 1] - p[a + 1], e2z = p[c + 2] - p[a + 2];

            double hx = d.Y * e2z - d.Z * e2y, hy = d.Z * e2x - d.X * e2z, hz = d.X * e2y - d.Y * e2x;
            double det = e1x * hx + e1y * hy + e1z * hz;
            if (Math.Abs(det) < 1e-12) continue;
            double inv = 1 / det;

            double sx = o[0] - p[a], sy = o[1] - p[a + 1], sz = o[2] - p[a + 2];
            double u = (sx * hx + sy * hy + sz * hz) * inv;
            if (u < 0 || u > 1) continue;

            double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
            double v = (d.X * qx + d.Y * qy + d.Z * qz) * inv;
            if (v < 0 || u + v > 1) continue;

            double dist = (e2x * qx + e2y * qy + e2z * qz) * inv;
            if (dist > 1e-9) count++;
        }
        return count;
    }

    private static string Title(SolidBoundsSpec s) => $"{s.Name} em {Box(s.Min, s.Max)} ±{s.Tolerance:0.###}";

    private static string Title(SolidWithinSpec s) =>
        $"{s.Name} {s.Meaning ?? "na região"}: dentro de {Box(s.Min, s.Max)}" + (s.Size is { Length: 3 } size ? $", tamanho {P(size)}" : "");

    private static string Title(SolidPointsSpec s) => $"{s.Name}: {s.Inside.Count} pontos no material, {s.Outside.Count} vazios";

    private static string Box(double?[] min, double?[] max) =>
        min.Length == 3 && max.Length == 3 ? $"{P(min)}–{P(max)}" : "?";

    private static string P(IReadOnlyList<double?> p) =>
        "(" + string.Join(", ", p.Select(v => v == null ? "*" : FormattableString.Invariant($"{v:0.###}"))) + ")";

    private static string P(double[] p) => P(p.Select(v => (double?)v).ToList());
}
