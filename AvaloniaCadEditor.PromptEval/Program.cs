using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Infrastructure.Scripting;
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
if (options.DryRun)
{
    baseOptions = new LlmOptions { ApiKey = "dry-run" };
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

var models = options.Models.Count > 0 ? options.Models : new List<string> { baseOptions.Model };
var evaluator = new RoslynSceneEvaluator();

var cases = await CaseLoader.LoadAsync(options.CaseDirectories, evaluator, options.CaseFilter);
if (cases.Count == 0)
{
    Console.Error.WriteLine("Nenhum caso encontrado.");
    return 1;
}

Console.WriteLine($"{cases.Count} caso(s) × {options.Profiles.Count} perfil(is) × {models.Count} modelo(s) × {options.Repeat} repetição(ões)");

var runner = new EvalRunner(baseOptions, evaluator, options.DryRun, options.Parallel);
var outcomes = await runner.RunAsync(cases, options.Profiles, models, options.Repeat);

string report = ReportWriter.Write(options.OutputDirectory, outcomes, cases, options);
Console.WriteLine();
Console.WriteLine($"Passaram {outcomes.Count(o => o.Passed)}/{outcomes.Count}. Relatório: {Path.GetFullPath(report)}");
return 0;
