using System.Diagnostics;
using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Prompts;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Harness;
using AvaloniaCadEditor.Harness.Agents;
using AvaloniaCadEditor.Harness.Skills;
using AvaloniaCadEditor.Infrastructure.OpenCascade;

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
    IReadOnlyList<ScriptDocument> Changes,
    string Endpoint = EvalTarget.EnvironmentEndpoint,
    bool Tools = false,
    int ToolSteps = 0,
    int InspectorTokens = 0,
    string Skills = "")
{
    public bool ChecksPassed => Checks.All(c => c.Passed);

    /// <summary>Modelo, endpoint e modo, como coluna do relatório (ex.: "qwen3:8b@ollama +tools").</summary>
    public string Variant => Model + (Endpoint == EvalTarget.EnvironmentEndpoint ? "" : "@" + Endpoint) + (Tools ? " +tools" : "") +
        (Skills.Length > 0 ? " +" + Skills : "");

    /// <summary>Válido (compila e executa) e cumpre todas as verificações do caso.</summary>
    public bool Passed => IsValid && Error == null && ChecksPassed;

    /// <summary>
    /// Entendeu o pedido e a cena: válido e sem erro de conceito. Pode ter errado um ajuste (uma cota
    /// fina), que se corrige com um número. Sem checks de ajuste no caso, é o mesmo que <see cref="Passed"/>.
    /// </summary>
    public bool Understood => IsValid && Error == null && Checks.Where(c => c.Level == CheckLevel.Conceito).All(c => c.Passed);
}

/// <summary>
/// Reexecuta cada round de cada caso com cada perfil × destino (endpoint + modelo) × modo (com ou sem
/// ferramentas) × repetição, pelas mesmas sessões do app: a <see cref="CadScriptChatSession"/> (validação
/// e auto-fix inclusos) ou a <see cref="HarnessChatSession"/>. Os rounds são independentes: o contexto
/// gravado já traz o estado completo dos arquivos.
/// </summary>
internal sealed class EvalRunner
{
    private readonly IReadOnlyList<EvalTarget> _targets;
    private readonly IReadOnlyList<bool> _toolModes;
    private readonly EvalTarget? _inspector;
    private readonly ISceneEvaluator _evaluator;
    private readonly bool _dryRun;
    private readonly int _parallel;
    private readonly string? _dumpDirectory;
    private readonly IReadOnlyList<string> _skills;

    public EvalRunner(IReadOnlyList<EvalTarget> targets, IReadOnlyList<bool> toolModes, EvalTarget? inspector,
        ISceneEvaluator evaluator, bool dryRun, int parallel, string? dumpDirectory = null, IReadOnlyList<string>? skills = null)
    {
        _skills = skills ?? Array.Empty<string>();
        _targets = targets;
        _toolModes = toolModes;
        _inspector = inspector;
        _evaluator = evaluator;
        _dryRun = dryRun;
        _parallel = parallel;
        _dumpDirectory = dumpDirectory;
    }

    public async Task<List<RunOutcome>> RunAsync(IReadOnlyList<EvalCase> cases, IReadOnlyList<ICadScriptPromptProfile> profiles, int repeat)
    {
        var jobs = (
            from evalCase in cases
            from round in evalCase.Record.Rounds.Select((r, i) => (Round: r, Index: i))
            from profile in profiles
            from target in _targets
            from tools in _toolModes
            from rep in Enumerable.Range(1, repeat)
            select (evalCase, round.Round, round.Index, profile, target, tools, rep)).ToList();

        var outcomes = new List<RunOutcome>();
        using var gate = new SemaphoreSlim(_parallel);
        // Um servidor local atende uma chamada por vez: fila própria por endpoint local (o do Inspetor também).
        var localGates = _targets.Append(_inspector).OfType<EvalTarget>().Where(t => t.IsLocal)
            .Select(t => t.Endpoint).Distinct().ToDictionary(e => e, _ => new SemaphoreSlim(1));
        int done = 0;

        await Task.WhenAll(jobs.Select(async job =>
        {
            var local = localGates.GetValueOrDefault(job.target.Endpoint)
                ?? (job.tools && _inspector is { IsLocal: true } ? localGates[_inspector.Endpoint] : null);
            await gate.WaitAsync();
            if (local != null) await local.WaitAsync();
            try
            {
                var outcome = await RunOneAsync(job.evalCase, job.Round, job.Index, job.profile, job.target, job.tools, job.rep);
                lock (outcomes)
                {
                    outcomes.Add(outcome);
                    done++;
                    string status = outcome.Passed ? "ok" : outcome.Understood ? "entendeu (errou ajuste)" : outcome.IsValid ? "checks" : "falhou";
                    Console.WriteLine($"[{done}/{jobs.Count}] {job.evalCase.Name}#{job.Index + 1} {job.profile.Id} {outcome.Variant} r{job.rep}: {status} ({outcome.Attempts} tent., {outcome.ToolSteps} ferr., {outcome.Seconds:F0}s)");
                }
            }
            finally
            {
                local?.Release();
                gate.Release();
            }
        }));

        foreach (var g in localGates.Values) g.Dispose();
        return outcomes
            .OrderBy(o => o.Case, StringComparer.Ordinal).ThenBy(o => o.Round)
            .ThenBy(o => o.Profile).ThenBy(o => o.Variant, StringComparer.Ordinal).ThenBy(o => o.Repetition)
            .ToList();
    }

    /// <summary>As skills de uma execução com ferramentas: as do caso e as do <c>--skills</c>, na ordem do registro.</summary>
    private IReadOnlyList<HarnessSkill> SkillsFor(EvalCase evalCase, bool tools) =>
        tools ? SkillRegistry.Default().Resolve((evalCase.Skills ?? Array.Empty<string>()).Concat(_skills)) : Array.Empty<HarnessSkill>();

    private ICadScriptChatSession CreateSession(CadScriptProtocol protocol, EvalTarget target, bool tools, RecordedRound round,
        IReadOnlyList<HarnessSkill> skills)
    {
        if (!tools)
        {
            // Dry-run: devolve a resposta gravada (vazia numa fixture) — valida o pipeline sem gastar tokens.
            CadScriptChatSession.SendDelegate? send = _dryRun
                ? (_, _, _) => Task.FromResult(new LlmResult { Content = round.Response.RawContent })
                : null;
            return new CadScriptChatSession(target.Options, protocol, _evaluator, send) { KeepHistory = false };
        }

        ToolSendDelegate? toolSend = _dryRun
            ? (_, _, _, _) => Task.FromResult(new LlmResult { Content = round.Response.RawContent })
            : null;
        var inspector = _inspector == null ? null
            : new InspectorAgent(_inspector.Options, _inspector.Limits, _inspector.Endpoint, toolSend);
        return new HarnessChatSession(target.Options, protocol, _evaluator, new OpenCascadeGeometryKernel(), target.Limits,
            send: toolSend, inspector: inspector, skills: skills) { KeepHistory = false };
    }

    private async Task<RunOutcome> RunOneAsync(EvalCase evalCase, RecordedRound round, int index, ICadScriptPromptProfile profile,
        EvalTarget target, bool tools, int rep)
    {
        var protocol = new CadScriptProtocol(profile);
        string model = target.Options.Model;
        var skills = SkillsFor(evalCase, tools);
        // Só as do --skills entram no rótulo: as do caso fazem parte dele (e o relatório por caso não se divide).
        string skillLabel = tools ? string.Join(",", SkillRegistry.Default().Resolve(_skills).Select(s => s.Id)) : "";
        var session = CreateSession(protocol, target, tools, round, skills);

        int systemPromptChars = session.History[0].Content.Length;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await session.SendAsync(round.Instruction, round.Context.ToContext());
            stopwatch.Stop();

            var metrics = CodeMetrics.From(result, round.Context);
            var harness = session as HarnessChatSession;
            var inspection = harness?.LastPreInspection;
            var checks = evalCase.Checks.Evaluate(result, round.Context, metrics)
                .Concat(await GeometryChecks.EvaluateAsync(evalCase.Checks, result, round.Context, _evaluator))
                .ToList();
            var outcome = new RunOutcome(
                evalCase.Name, index + 1, profile.Id, model, rep,
                result.IsValid, result.Attempts, result.Error,
                result.PromptTokens, result.CompletionTokens, stopwatch.Elapsed.TotalSeconds, systemPromptChars,
                metrics, checks,
                result.ChatText, result.Changes,
                target.Endpoint, tools, harness?.LastTurnSteps.Count ?? 0,
                inspection == null ? 0 : inspection.PromptTokens + inspection.CompletionTokens, skillLabel);
            if (_dumpDirectory != null) PromptDump.Write(_dumpDirectory, outcome, session.History, tools);
            return outcome;
        }
        catch (Exception ex)
        {
            // Erro de rede/API: conta como execução que falhou, sem derrubar as demais.
            return new RunOutcome(
                evalCase.Name, index + 1, profile.Id, model, rep,
                IsValid: false, Attempts: 0, Error: $"{ex.GetType().Name}: {ex.Message}",
                0, 0, stopwatch.Elapsed.TotalSeconds, systemPromptChars,
                Metrics: null, Checks: Array.Empty<CheckResult>(), ChatText: string.Empty, Changes: Array.Empty<ScriptDocument>(),
                Endpoint: target.Endpoint, Tools: tools, Skills: skillLabel);
        }
    }
}
