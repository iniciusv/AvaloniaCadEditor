using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>Grava <c>report.md</c> (resumo comparativo) e <c>runs.jsonl</c> (cada execução completa).</summary>
internal static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonlOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Write(string outputRoot, IReadOnlyList<RunOutcome> outcomes, IReadOnlyList<EvalCase> cases, EvalOptions options)
    {
        string dir = Path.Combine(outputRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);

        File.WriteAllLines(Path.Combine(dir, "runs.jsonl"), outcomes.Select(o => JsonSerializer.Serialize(o, JsonlOptions)));

        string reportPath = Path.Combine(dir, "report.md");
        File.WriteAllText(reportPath, BuildMarkdown(outcomes, cases, options));
        return reportPath;
    }

    private static string BuildMarkdown(IReadOnlyList<RunOutcome> outcomes, IReadOnlyList<EvalCase> cases, EvalOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Avaliação de pré-prompts — {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"{cases.Count} caso(s), {options.Repeat} repetição(ões){(options.DryRun ? ", **dry-run** (respostas gravadas, sem IA)" : "")}{(options.Reasoning ? ", reasoning ligado" : "")}.");
        sb.AppendLine("\"Passou\" = compilou, executou e cumpriu todas as verificações do caso.");
        sb.AppendLine();

        sb.AppendLine("## Resumo por perfil × modelo");
        sb.AppendLine();
        sb.AppendLine("| Perfil | Modelo | Execuções | Passou | Válido | 1ª tentativa | Tentativas | Tokens prompt | Tokens resposta | Tempo (s) | Parâm./peça (máx) | Linhas | System prompt (chars) |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var group in outcomes.GroupBy(o => (o.Profile, o.Model)))
        {
            var runs = group.ToList();
            var withParts = runs.Where(r => r.Metrics?.MaxPartParameters != null).ToList();
            sb.AppendLine(string.Join(" | ",
                "", group.Key.Profile, group.Key.Model, runs.Count.ToString(CultureInfo.InvariantCulture),
                Percent(runs, r => r.Passed),
                Percent(runs, r => r.IsValid),
                Percent(runs, r => r.IsValid && r.Attempts == 1),
                Average(runs.Where(r => r.Attempts > 0), r => r.Attempts, "F2"),
                Average(runs, r => r.PromptTokens, "F0"),
                Average(runs, r => r.CompletionTokens, "F0"),
                Average(runs, r => r.Seconds, "F1"),
                withParts.Count == 0 ? "—" : Average(withParts, r => r.Metrics!.MaxPartParameters!.Value, "F1"),
                Average(runs.Where(r => r.Metrics != null), r => r.Metrics!.CodeLines, "F0"),
                runs[0].SystemPromptChars.ToString(CultureInfo.InvariantCulture),
                "").Trim());
        }
        sb.AppendLine();

        var columns = outcomes.Select(o => (o.Profile, o.Model)).Distinct().ToList();
        sb.AppendLine("## Por caso (execuções que passaram / total)");
        sb.AppendLine();
        sb.AppendLine("| Caso | " + string.Join(" | ", columns.Select(c => $"{c.Profile} · {c.Model}")) + " |");
        sb.AppendLine("|---|" + string.Concat(columns.Select(_ => "---:|")));
        foreach (var caseRound in outcomes.GroupBy(o => (o.Case, o.Round)))
        {
            var evalCase = cases.First(c => c.Name == caseRound.Key.Case);
            string label = evalCase.Record.Rounds.Count > 1 ? $"{caseRound.Key.Case} #{caseRound.Key.Round}" : caseRound.Key.Case;
            var cells = columns.Select(c =>
            {
                var runs = caseRound.Where(o => o.Profile == c.Profile && o.Model == c.Model).ToList();
                int passed = runs.Count(r => r.Passed);
                return $"{(passed == runs.Count ? "✓" : passed == 0 ? "✗" : "~")} {passed}/{runs.Count}";
            });
            sb.AppendLine($"| {label} | {string.Join(" | ", cells)} |");
        }
        sb.AppendLine();

        var failures = outcomes.Where(o => !o.Passed).ToList();
        if (failures.Count > 0)
        {
            sb.AppendLine("## Falhas");
            sb.AppendLine();
            foreach (var f in failures)
            {
                string why = f.Error != null
                    ? Truncate(f.Error)
                    : string.Join("; ", f.Checks.Where(c => !c.Passed).Select(c => c.Detail is { Length: > 0 } ? $"{c.Name} ({Truncate(c.Detail)})" : c.Name));
                sb.AppendLine($"- **{f.Case}#{f.Round}** · {f.Profile} · {f.Model} · r{f.Repetition}: {why}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Casos");
        sb.AppendLine();
        foreach (var evalCase in cases)
        {
            sb.AppendLine($"- **{evalCase.Name}** ({evalCase.Source}): {Truncate(evalCase.Description ?? evalCase.Record.Rounds.FirstOrDefault()?.Instruction ?? "")}");
        }

        return sb.ToString();
    }

    private static string Percent(IReadOnlyCollection<RunOutcome> runs, Func<RunOutcome, bool> predicate) =>
        runs.Count == 0 ? "—" : $"{100.0 * runs.Count(predicate) / runs.Count:F0}%";

    private static string Average(IEnumerable<RunOutcome> runs, Func<RunOutcome, double> selector, string format)
    {
        var values = runs.Select(selector).ToList();
        return values.Count == 0 ? "—" : values.Average().ToString(format, CultureInfo.InvariantCulture);
    }

    private static string Truncate(string text, int max = 160)
    {
        string oneLine = text.ReplaceLineEndings(" ").Replace("|", "\\|");
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "…";
    }
}
