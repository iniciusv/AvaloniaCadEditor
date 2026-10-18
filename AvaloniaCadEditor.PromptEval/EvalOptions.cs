using AvaloniaCadEditor.Application.Ai.Prompts;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Harness.Skills;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>Argumentos da linha de comando.</summary>
internal sealed class EvalOptions
{
    public const string Usage =
        """
        Uso: dotnet run --project AvaloniaCadEditor.PromptEval -- [opções]

          --profiles atual,enxuto,simples   perfis de pré-prompt (padrão: todos)
          --models deepseek-flash,...       modelos (padrão: DEEPSEEK_MODEL ou o modelo do perfil);
                                            perfil:modelo escolhe o endpoint (ex.: ollama:qwen3:8b)
          --endpoints deepseek,ollama       perfis de endpoint do ai_settings.json (padrão: DEEPSEEK_* do ambiente)
          --local http://localhost:1234/v1  servidor local avulso (LM Studio, Ollama, llama.cpp), sem perfil gravado;
                                            o modelo vem de --models. Raciocínio desligado, salvo com --reasoning
          --tools off|on|both               chat de sempre, com ferramentas (harness) ou os dois (padrão: off)
          --inspector ollama                perfil do Inspetor (só com ferramentas; pré-inspeção ligada)
          --skills questionar,revisar       skills do harness ligadas em todos os casos (só com ferramentas),
                                            somadas às do case.json
          --repeat N                        repetições de cada combinação (padrão: 1)
          --cases texto                     só casos cujo nome contém o texto
          --cases-dir pasta                 pasta extra de casos (pode repetir)
          --saved                           inclui as conversas gravadas pelo chat (%AppData%/AvaloniaCadEditor/ai-cases)
          --parallel N                      chamadas simultâneas (padrão: 4)
          --reasoning                       liga o thinking mode
          --out pasta                       onde gravar o relatório (padrão: eval-results)
          --dump pasta                      grava a conversa de cada rodada como foi para o modelo
                                            (system prompt, pedido, ferramentas, chamadas e resultados)
          --dry-run                         não chama a IA: reaproveita a resposta gravada no caso

        Sem --endpoints, a chave vem de DEEPSEEK_API_KEY (e DEEPSEEK_BASE_URL, se houver).
        Endpoints locais rodam uma chamada por vez, qualquer que seja o --parallel.
        """;

    public List<string> ProfileIds { get; } = new();
    public List<string> Models { get; } = new();
    public int Repeat { get; private set; } = 1;
    public string? CaseFilter { get; private set; }
    public List<string> CaseDirectories { get; } = new() { Path.Combine(AppContext.BaseDirectory, "Cases") };
    public int Parallel { get; private set; } = 4;
    public bool Reasoning { get; private set; }
    public string OutputDirectory { get; private set; } = "eval-results";
    public bool DryRun { get; private set; }
    public string? DumpDirectory { get; private set; }
    public List<string> Endpoints { get; } = new();
    public string? LocalUrl { get; private set; }

    /// <summary>false = chat de sempre; true = com ferramentas (harness).</summary>
    public List<bool> ToolModes { get; } = new() { false };
    public string? InspectorEndpoint { get; private set; }
    public List<string> Skills { get; } = new();
    public bool ShowHelp { get; private set; }

    public IReadOnlyList<ICadScriptPromptProfile> Profiles =>
        ProfileIds.Count == 0
            ? CadScriptPromptProfiles.All
            : ProfileIds.Select(id => CadScriptPromptProfiles.All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Perfil desconhecido: {id}. Existentes: {string.Join(", ", CadScriptPromptProfiles.All.Select(p => p.Id))}")).ToList();

    public static EvalOptions Parse(string[] args)
    {
        var options = new EvalOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} precisa de um valor.");
            IEnumerable<string> List() => Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            switch (args[i])
            {
                case "--profiles": options.ProfileIds.AddRange(List()); break;
                case "--models": options.Models.AddRange(List()); break;
                case "--repeat": options.Repeat = Math.Max(1, int.Parse(Next())); break;
                case "--cases": options.CaseFilter = Next(); break;
                case "--cases-dir": options.CaseDirectories.Add(Next()); break;
                case "--saved": options.CaseDirectories.Add(AiConversationRecordStore.DefaultDirectory); break;
                case "--parallel": options.Parallel = Math.Max(1, int.Parse(Next())); break;
                case "--reasoning": options.Reasoning = true; break;
                case "--out": options.OutputDirectory = Next(); break;
                case "--dry-run": options.DryRun = true; break;
                case "--dump": options.DumpDirectory = Next(); break;
                case "--endpoints": options.Endpoints.AddRange(List()); break;
                case "--local": options.LocalUrl = Next(); break;
                case "--inspector": options.InspectorEndpoint = Next(); break;
                case "--skills":
                    foreach (string id in List())
                    {
                        if (SkillRegistry.Default().Find(id) == null)
                            throw new ArgumentException($"Skill desconhecida: {id}. Existentes: {string.Join(", ", SkillRegistry.Default().All.Select(s => s.Id))}");
                        options.Skills.Add(id);
                    }
                    break;
                case "--tools":
                    var modes = Next() switch
                    {
                        "off" => new[] { false },
                        "on" => new[] { true },
                        "both" => new[] { false, true },
                        var other => throw new ArgumentException($"--tools aceita off, on ou both (veio '{other}').")
                    };
                    options.ToolModes.Clear();
                    options.ToolModes.AddRange(modes);
                    break;
                case "-h" or "--help": options.ShowHelp = true; break;
                default: throw new ArgumentException($"Opção desconhecida: {args[i]}");
            }
        }
        return options;
    }
}
