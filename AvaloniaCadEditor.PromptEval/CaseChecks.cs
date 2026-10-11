using System.Text.RegularExpressions;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Scenes;

namespace AvaloniaCadEditor.PromptEval;

internal sealed record CheckResult(string Name, bool Passed, string? Detail = null);

/// <summary>
/// Verificações opcionais de um caso, lidas do <c>case.json</c> da fixture ou do <c>*.checks.json</c> ao
/// lado de uma conversa gravada. Caminhos aceitam glob (<c>*</c> = um nível, <c>**</c> = qualquer nível);
/// padrões de código são regex aplicadas a todos os arquivos devolvidos.
/// </summary>
internal sealed class CaseChecks
{
    /// <summary>A resposta deve ser só texto (ex.: uma pergunta).</summary>
    public bool ExpectNoCode { get; set; }

    /// <summary>Cada glob deve casar com pelo menos um arquivo NOVO (ex.: "Pecas/*.cs").</summary>
    public List<string>? ExpectNewFiles { get; set; }

    /// <summary>Todo arquivo alterado deve casar com algum destes globs.</summary>
    public List<string>? OnlyChangeFiles { get; set; }

    /// <summary>Peças que já existem na cena e não podem ser declaradas de novo em outro arquivo.</summary>
    public List<string>? MustNotRedeclare { get; set; }

    public int? MaxPartParameters { get; set; }
    public int? MinFeatures { get; set; }
    public int? MaxFeatures { get; set; }
    public List<string>? MustMatch { get; set; }
    public List<string>? MustNotMatch { get; set; }

    /// <summary>Regex → número mínimo de ocorrências (ex.: { "new CylinderFeature": 4 }).</summary>
    public Dictionary<string, int>? MinOccurrences { get; set; }

    public IReadOnlyList<CheckResult> Evaluate(CadScriptTurnResult result, RecordedContext context, CodeMetrics metrics)
    {
        var checks = new List<CheckResult>();
        string code = string.Join("\n", result.Changes.Select(c => c.Text));

        if (ExpectNoCode)
        {
            checks.Add(new("sem código", result.Changes.Count == 0, $"{result.Changes.Count} arquivo(s)"));
        }

        foreach (var glob in ExpectNewFiles ?? new())
        {
            checks.Add(new($"arquivo novo {glob}", metrics.NewFiles.Any(f => Glob(glob, f)), string.Join(", ", metrics.NewFiles)));
        }

        if (OnlyChangeFiles is { Count: > 0 })
        {
            var outside = result.Changes.Select(c => c.FilePath).Where(f => !OnlyChangeFiles.Any(g => Glob(g, f))).ToList();
            checks.Add(new($"só altera {string.Join(", ", OnlyChangeFiles)}", outside.Count == 0, string.Join(", ", outside)));
        }

        if (MustNotRedeclare is { Count: > 0 })
        {
            var declaredBefore = PartSourceEditor.FindPartTypes(CodeMetrics.OriginalDocuments(context))
                .ToDictionary(t => t.Name, t => t.FilePath, StringComparer.Ordinal);
            foreach (string type in MustNotRedeclare)
            {
                var redeclared = PartSourceEditor.FindPartTypes(result.Changes)
                    .Where(t => t.Name == type && !(declaredBefore.TryGetValue(type, out var file) && PathEquals(file, t.FilePath)))
                    .Select(t => t.FilePath)
                    .ToList();
                checks.Add(new($"não redeclara {type}", redeclared.Count == 0, string.Join(", ", redeclared)));
            }
        }

        if (MaxPartParameters is int maxParams)
        {
            checks.Add(new($"≤ {maxParams} parâmetros por peça", (metrics.MaxPartParameters ?? 0) <= maxParams, $"{metrics.MaxPartParameters}"));
        }

        if (MinFeatures is int min) checks.Add(new($"≥ {min} features", metrics.FeatureCount >= min, $"{metrics.FeatureCount}"));
        if (MaxFeatures is int max) checks.Add(new($"≤ {max} features", metrics.FeatureCount <= max, $"{metrics.FeatureCount}"));

        foreach (string pattern in MustMatch ?? new())
        {
            checks.Add(new($"contém /{pattern}/", Regex.IsMatch(code, pattern)));
        }

        foreach (string pattern in MustNotMatch ?? new())
        {
            var match = Regex.Match(code, pattern);
            checks.Add(new($"não contém /{pattern}/", !match.Success, match.Success ? match.Value : null));
        }

        foreach (var (pattern, minCount) in MinOccurrences ?? new())
        {
            int count = Regex.Matches(code, pattern).Count;
            checks.Add(new($"/{pattern}/ ≥ {minCount}x", count >= minCount, $"{count}x"));
        }

        return checks;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool Glob(string glob, string path)
    {
        string regex = "^" + Regex.Escape(glob.Replace('\\', '/'))
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*") + "$";
        return Regex.IsMatch(path.Replace('\\', '/'), regex, RegexOptions.IgnoreCase);
    }
}
