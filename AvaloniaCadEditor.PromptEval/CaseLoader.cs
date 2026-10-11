using System.Text.Json;
using AvaloniaCadEditor.Application.Ai.Context;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;

namespace AvaloniaCadEditor.PromptEval;

/// <summary>
/// Carrega os casos de uma pasta:
/// <list type="bullet">
///   <item>fixture — uma subpasta com <c>case.json</c> (pedido + checks) e <c>files/</c> (a cena inicial).
///   O contexto é montado pelo <see cref="AiContextBuilder"/> real, como no app;</item>
///   <item>gravado — um <c>*.json</c> salvo pelo botão 💾 do chat, com checks opcionais em <c>*.checks.json</c>.</item>
/// </list>
/// </summary>
internal static class CaseLoader
{
    private const string FixtureFile = "case.json";
    private const string ChecksSuffix = ".checks.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>O <c>case.json</c> de uma fixture.</summary>
    private sealed class FixtureSpec
    {
        public string? Description { get; set; }
        public string Instruction { get; set; } = string.Empty;

        /// <summary>False = cena de arquivo único (só <see cref="EntryFile"/>).</summary>
        public bool Project { get; set; } = true;

        public string EntryFile { get; set; } = ScriptDocument.DefaultSceneFile;
        public string? ActiveFile { get; set; }

        /// <summary>Sólidos selecionados na viewport, pelo nome da variável.</summary>
        public List<string>? Select { get; set; }

        /// <summary>Arquivos fixados no contexto (vão com o código completo, não só a assinatura).</summary>
        public List<string>? Pin { get; set; }

        public CaseChecks Checks { get; set; } = new();
    }

    public static async Task<List<EvalCase>> LoadAsync(IEnumerable<string> directories, ISceneEvaluator evaluator, string? filter)
    {
        var cases = new List<EvalCase>();
        foreach (string dir in directories.Where(Directory.Exists))
        {
            foreach (string spec in Directory.EnumerateFiles(dir, FixtureFile, SearchOption.AllDirectories))
            {
                cases.Add(await LoadFixtureAsync(Path.GetDirectoryName(spec)!, evaluator));
            }

            foreach (string file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
                         .Where(f => !Path.GetFileName(f).Equals(FixtureFile, StringComparison.OrdinalIgnoreCase)
                                     && !f.EndsWith(ChecksSuffix, StringComparison.OrdinalIgnoreCase)))
            {
                cases.Add(LoadRecording(file));
            }
        }

        return cases
            .Where(c => filter == null || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static EvalCase LoadRecording(string file)
    {
        var record = AiConversationRecordStore.Load(file);
        string checksFile = Path.ChangeExtension(file, null) + ChecksSuffix;
        var checks = File.Exists(checksFile)
            ? JsonSerializer.Deserialize<CaseChecks>(File.ReadAllText(checksFile), JsonOptions) ?? new()
            : new CaseChecks();
        return new EvalCase(Path.GetFileNameWithoutExtension(file), "gravado", null, record, checks);
    }

    private static async Task<EvalCase> LoadFixtureAsync(string dir, ISceneEvaluator evaluator)
    {
        string name = Path.GetFileName(dir);
        var spec = JsonSerializer.Deserialize<FixtureSpec>(File.ReadAllText(Path.Combine(dir, FixtureFile)), JsonOptions)
                   ?? throw new InvalidDataException($"{name}: case.json vazio.");

        var documents = ReadFiles(Path.Combine(dir, "files"));
        if (!documents.Any(d => d.FilePath.Equals(spec.EntryFile, StringComparison.OrdinalIgnoreCase)))
        {
            documents.Insert(0, new ScriptDocument(spec.EntryFile, string.Empty));
        }
        if (!spec.Project && documents.Count > 1)
        {
            throw new InvalidDataException($"{name}: cena sem projeto deve ter só o arquivo {spec.EntryFile}.");
        }

        var selected = await ResolveSelectionAsync(name, documents, spec.Select ?? new(), evaluator);
        var adjustments = new AiContextAdjustments();
        foreach (string path in spec.Pin ?? new()) adjustments.Pin(path);

        var context = new AiContextBuilder().Build(new AiContextRequest(
            documents, spec.EntryFile, spec.ActiveFile, selected, spec.Project, Adjustments: adjustments));

        var record = new AiConversationRecord
        {
            Rounds = { new RecordedRound { Instruction = spec.Instruction, Context = RecordedContext.From(context) } }
        };
        return new EvalCase(name, "fixture", spec.Description, record, spec.Checks);
    }

    private static List<ScriptDocument> ReadFiles(string root)
    {
        if (!Directory.Exists(root)) return new();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new ScriptDocument(Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToList();
    }

    /// <summary>
    /// Executa a cena inicial (o que também garante que a fixture compila) e pega os sólidos pelo nome,
    /// com a origem no código que a seleção do app teria.
    /// </summary>
    private static async Task<IReadOnlyList<IFeature>> ResolveSelectionAsync(
        string caseName, IReadOnlyList<ScriptDocument> documents, List<string> names, ISceneEvaluator evaluator)
    {
        if (documents.All(d => string.IsNullOrWhiteSpace(d.Text))) return Array.Empty<IFeature>();

        var result = await evaluator.EvaluateAsync(documents);
        if (!result.Success)
        {
            throw new InvalidDataException($"{caseName}: a cena inicial não executa — {result.ErrorMessage}");
        }

        return names
            .Select(n => result.Features.FirstOrDefault(f => FeatureOrigin.VariableName(f) == n || f.Name == n)
                         ?? throw new InvalidDataException($"{caseName}: sólido '{n}' não encontrado na cena inicial."))
            .ToList();
    }
}
