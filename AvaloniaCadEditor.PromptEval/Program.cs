using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Infrastructure.Scripting;
using AvaloniaCadEditor.Infrastructure.Settings;
using AvaloniaCadEditor.PromptEval;

// Avaliador offline dos perfis de pré-prompt: reexecuta casos (fixtures e conversas gravadas pelo chat)
// com cada perfil × modelo e compara validade, tentativas, tokens e o "quão limpo" ficou o código.

EvalOptions options;
try
{
    options = EvalOptions.Parse(args);
    _ = options.Profiles;
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(EvalOptions.Usage);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(EvalOptions.Usage);
    return 0;
}

LlmOptions baseOptions;
if (options.DryRun || options.Endpoints.Count > 0 || options.LocalUrl != null)
{
    // Com --endpoints, chave e URL vêm de cada perfil; o ambiente não é obrigatório.
    baseOptions = CadScriptChatSession.LoadOptionsFromEnvironment().Options ?? new LlmOptions { ApiKey = "dry-run" };
}
else
{
    var (loaded, error) = CadScriptChatSession.LoadOptionsFromEnvironment();
    if (loaded == null)
    {
        Console.Error.WriteLine(error);
        return 1;
    }
    baseOptions = loaded;
}
baseOptions.EnableThinkingMode = options.Reasoning;

var settingsProvider = new JsonAiSettingsProvider();
var (targets, targetError) = EvalTargets.Build(options, baseOptions, settingsProvider.GetSettings);
var (inspector, inspectorError) = EvalTargets.Inspector(options, settingsProvider.GetSettings);
if (targets == null || inspectorError != null)
{
    Console.Error.WriteLine(targetError ?? inspectorError);
    return 2;
}
if (inspector != null && !options.ToolModes.Contains(true))
{
    Console.Error.WriteLine("--inspector só vale com --tools on ou both.");
    return 2;
}
var evaluator = new RoslynSceneEvaluator();

var cases = await CaseLoader.LoadAsync(options.CaseDirectories, evaluator, options.CaseFilter);
if (cases.Count == 0)
{
    Console.Error.WriteLine("Nenhum caso encontrado.");
    return 1;
}

Console.WriteLine($"{cases.Count} caso(s) × {options.Profiles.Count} perfil(is) × {targets.Count} destino(s) × {options.ToolModes.Count} modo(s) × {options.Repeat} repetição(ões)" +
    (inspector != null ? $" · Inspetor: {inspector.Options.Model}@{inspector.Endpoint}" : ""));

var runner = new EvalRunner(targets, options.ToolModes, inspector, evaluator, options.DryRun, options.Parallel);
var outcomes = await runner.RunAsync(cases, options.Profiles, options.Repeat);

string report = ReportWriter.Write(options.OutputDirectory, outcomes, cases, options);
Console.WriteLine();
Console.WriteLine($"Passaram {outcomes.Count(o => o.Passed)}/{outcomes.Count}. Relatório: {Path.GetFullPath(report)}");
return 0;
