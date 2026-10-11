using System.Diagnostics;
using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Prompts;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>Uma execução: um round de um caso, com um perfil e um modelo.</summary>
internal sealed record RunOutcome(
    string Case,
    int Round,
    string Profile,
    string Model,
    int Repetition,
    bool IsValid,
    int Attempts,
    string? Error,
    int PromptTokens,
    int CompletionTokens,
    double Seconds,
    int SystemPromptChars,
    CodeMetrics? Metrics,
    IReadOnlyList<CheckResult> Checks,
    string ChatText,
    IReadOnlyList<ScriptDocument> Changes)
{
    public bool ChecksPassed => Checks.All(c => c.Passed);

    /// <summary>Válido (compila e executa) e cumpre todas as verificações do caso.</summary>
    public bool Passed => IsValid && Error == null && ChecksPassed;
}

/// <summary>
/// Reexecuta cada round de cada caso com cada perfil × modelo × repetição, pela mesma
/// <see cref="CadScriptChatSession"/> do app (validação e auto-fix inclusos). Os rounds são independentes:
/// o contexto gravado já traz o estado completo dos arquivos, e só o pré-prompt varia.
/// </summary>
internal sealed class EvalRunner
{
    private readonly LlmOptions _baseOptions;
    private readonly ISceneEvaluator _evaluator;
    private readonly bool _dryRun;
    private readonly int _parallel;

    public EvalRunner(LlmOptions baseOptions, ISceneEvaluator evaluator, bool dryRun, int parallel)
    {
        _baseOptions = baseOptions;
        _evaluator = evaluator;
        _dryRun = dryRun;
        _parallel = parallel;
    }

    public async Task<List<RunOutcome>> RunAsync(
        IReadOnlyList<EvalCase> cases, IReadOnlyList<ICadScriptPromptProfile> profiles, IReadOnlyList<string> models, int repeat)
    {
        var jobs = (
            from evalCase in cases
            from round in evalCase.Record.Rounds.Select((r, i) => (Round: r, Index: i))
            from profile in profiles
            from model in models
            from rep in Enumerable.Range(1, repeat)
            select (evalCase, round.Round, round.Index, profile, model, rep)).ToList();

        var outcomes = new List<RunOutcome>();
        using var gate = new SemaphoreSlim(_parallel);
        int done = 0;

        await Task.WhenAll(jobs.Select(async job =>
        {
            await gate.WaitAsync();
            try
            {
                var outcome = await RunOneAsync(job.evalCase, job.Round, job.Index, job.profile, job.model, job.rep);
                lock (outcomes)
                {
                    outcomes.Add(outcome);
                    done++;
                    string status = outcome.Passed ? "ok" : outcome.IsValid ? "checks" : "falhou";
                    Console.WriteLine($"[{done}/{jobs.Count}] {job.evalCase.Name}#{job.Index + 1} {job.profile.Id} {job.model} r{job.rep}: {status} ({outcome.Attempts} tent., {outcome.Seconds:F0}s)");
                }
            }
            finally
            {
                gate.Release();
            }
        }));

        return outcomes
            .OrderBy(o => o.Case, StringComparer.Ordinal).ThenBy(o => o.Round)
            .ThenBy(o => o.Profile).ThenBy(o => o.Model).ThenBy(o => o.Repetition)
            .ToList();
    }

    private async Task<RunOutcome> RunOneAsync(EvalCase evalCase, RecordedRound round, int index, ICadScriptPromptProfile profile, string model, int rep)
    {
        var protocol = new CadScriptProtocol(profile);
        var options = new LlmOptions
        {
            ApiKey = _baseOptions.ApiKey,
            BaseUrl = _baseOptions.BaseUrl,
            Model = model,
            Temperature = _baseOptions.Temperature,
            MaxTokens = _baseOptions.MaxTokens,
            EnableThinkingMode = _baseOptions.EnableThinkingMode
        };

        // Dry-run: devolve a resposta gravada (vazia numa fixture) — valida o pipeline sem gastar tokens.
        CadScriptChatSession.SendDelegate? send = _dryRun
            ? (_, _, _) => Task.FromResult(new LlmResult { Content = round.Response.RawContent })
            : null;
        var session = new CadScriptChatSession(options, protocol, _evaluator, send) { KeepHistory = false };

        int systemPromptChars = protocol.GetSystemPrompt().Length;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await session.SendAsync(round.Instruction, round.Context.ToContext());
            stopwatch.Stop();

            var metrics = CodeMetrics.From(result, round.Context);
            return new RunOutcome(
                evalCase.Name, index + 1, profile.Id, model, rep,
                result.IsValid, result.Attempts, result.Error,
                result.PromptTokens, result.CompletionTokens, stopwatch.Elapsed.TotalSeconds, systemPromptChars,
                metrics, evalCase.Checks.Evaluate(result, round.Context, metrics),
                result.ChatText, result.Changes);
        }
        catch (Exception ex)
        {
            // Erro de rede/API: conta como execução que falhou, sem derrubar as demais.
            return new RunOutcome(
                evalCase.Name, index + 1, profile.Id, model, rep,
                IsValid: false, Attempts: 0, Error: $"{ex.GetType().Name}: {ex.Message}",
                0, 0, stopwatch.Elapsed.TotalSeconds, systemPromptChars,
                Metrics: null, Checks: Array.Empty<CheckResult>(), ChatText: string.Empty, Changes: Array.Empty<ScriptDocument>());
        }
    }
}
