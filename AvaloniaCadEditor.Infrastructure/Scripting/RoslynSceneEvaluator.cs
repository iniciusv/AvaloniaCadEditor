using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

/// <summary>
/// Compila a cena como um programa C# comum (top-level statements + tipos em outros arquivos)
/// com uma <see cref="CSharpCompilation"/>, executa o ponto de entrada num
/// <see cref="AssemblyLoadContext"/> colecionável e descarrega o assembly ao final. Os arquivos sem
/// top-level statements (as peças) vêm compilados à parte e guardados entre execuções
/// (<see cref="ScenePartsLibrary"/>), e cada arquivo só é lido de novo quando o texto muda.
/// </summary>
public class RoslynSceneEvaluator : ISceneEvaluator
{
    private const string GlobalUsingsFile = "GlobalUsings.g.cs";

    internal static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(BuildReferences);

    private static readonly Lazy<SyntaxTree> GlobalUsingsTree = new(() =>
        CSharpSyntaxTree.ParseText(
            SourceText.From(BuildGlobalUsingsSource(), Encoding.UTF8),
            ParseOptions,
            path: GlobalUsingsFile));

    public Task<SceneEvaluationResult> EvaluateAsync(string code) =>
        EvaluateAsync(new[] { new ScriptDocument(ScriptDocument.DefaultSceneFile, code ?? string.Empty) });

    public Task<SceneEvaluationResult> EvaluateAsync(
        IReadOnlyList<ScriptDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        // Roda na thread chamadora e termina de forma síncrona quando a cena não usa await,
        // como o CSharpScript fazia: a UI hoje chama RunCodeCommand.Execute(null) sem aguardar
        // e lê a cena logo em seguida. Tirar isso da UI thread exige antes aguardar esses pontos.
        return EvaluateCoreAsync(documents, cancellationToken);
    }

    /// <summary>
    /// Compila e emite (sem carregar nem executar) uma cena mínima, para que o JIT do Roslyn e a
    /// leitura das referências aconteçam antes do primeiro Run. Não toca em estado da cena, então
    /// pode rodar em paralelo com uma avaliação normal. Falhas são ignoradas: é só otimização.
    /// </summary>
    public static void WarmUp()
    {
        try
        {
            var documents = new[] { new ScriptDocument(ScriptDocument.DefaultSceneFile, "var warmUp = 0;") };
            var compilation = CreateCompilation(documents, out _);
            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();
            compilation.Emit(peStream, pdbStream,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Falha ao aquecer o compilador da cena: " + ex.Message);
        }
    }

    /// <summary>
    /// Compilação da cena (sem emitir nem executar), com as mesmas referências e usings globais
    /// da execução. Também serve à navegação no código (definição, referências, renomear).
    /// </summary>
    /// <param name="userTrees">Árvores dos arquivos da cena, na ordem de <paramref name="documents"/>.</param>
    public static CSharpCompilation CreateCompilation(
        IReadOnlyList<ScriptDocument> documents,
        out IReadOnlyList<SyntaxTree> userTrees,
        CancellationToken cancellationToken = default)
    {
        var trees = documents.Select(d => Parse(d, cancellationToken)).ToList();
        userTrees = trees;
        return CreateCompilation(trees, cancellationToken);
    }

    private static SyntaxTree Parse(ScriptDocument document, CancellationToken cancellationToken) =>
        CSharpSyntaxTree.ParseText(
            SourceText.From(document.Text ?? string.Empty, Encoding.UTF8),
            ParseOptions,
            path: document.FilePath,
            cancellationToken: cancellationToken);

    private static CSharpCompilation CreateCompilation(IReadOnlyList<SyntaxTree> trees, CancellationToken cancellationToken)
    {
        bool hasEntryPoint = trees.Any(t => HasGlobalStatements(t, cancellationToken));

        return CSharpCompilation.Create(
            assemblyName: "CadScene_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: trees.Prepend(GlobalUsingsTree.Value),
            references: References.Value,
            options: CompilationOptions(hasEntryPoint ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary));
    }

    internal static CSharpCompilationOptions CompilationOptions(OutputKind kind) =>
        new(kind,
            optimizationLevel: OptimizationLevel.Debug,
            nullableContextOptions: NullableContextOptions.Disable,
            concurrentBuild: true);

    private static bool HasGlobalStatements(SyntaxTree tree, CancellationToken cancellationToken) =>
        ((CompilationUnitSyntax)tree.GetRoot(cancellationToken)).Members.OfType<GlobalStatementSyntax>().Any();

    /// <summary>
    /// Última árvore lida de cada arquivo: a cada execução quase todos chegam iguais (só a Cena.cs muda ao
    /// arrastar uma peça), e ler de novo os ~45 arquivos da cena de referência custa ~35 ms.
    /// </summary>
    private static readonly Dictionary<string, (string Text, SyntaxTree Tree)> ParsedByPath = new(StringComparer.Ordinal);

    private const int MaxParsedFiles = 512;

    private static SyntaxTree ParseCached(ScriptDocument document, CancellationToken cancellationToken)
    {
        string text = document.Text ?? string.Empty;
        lock (ParsedByPath)
        {
            if (ParsedByPath.TryGetValue(document.FilePath, out var cached) && cached.Text == text) return cached.Tree;
        }

        var tree = Parse(document, cancellationToken);
        lock (ParsedByPath)
        {
            if (ParsedByPath.Count >= MaxParsedFiles) ParsedByPath.Clear();
            ParsedByPath[document.FilePath] = (text, tree);
        }
        return tree;
    }

    private static async Task<SceneEvaluationResult> EvaluateCoreAsync(
        IReadOnlyList<ScriptDocument> documents,
        CancellationToken cancellationToken)
    {
        var trees = documents.Select(d => ParseCached(d, cancellationToken)).ToList();
        var compilation = CreateCompilation(trees, cancellationToken);
        bool hasEntryPoint = compilation.Options.OutputKind == OutputKind.ConsoleApplication;

        // Sem top-level statements não há o que executar (ex.: só comentários): cena vazia,
        // mas os erros de compilação dos tipos declarados ainda são reportados.
        if (!hasEntryPoint)
        {
            var diagnostics = ToScriptDiagnostics(compilation.GetDiagnostics(cancellationToken));
            return diagnostics.Any(d => d.Severity == ScriptDiagnosticSeverity.Error)
                ? CompileFailure(documents, diagnostics)
                : new SceneEvaluationResult(Array.Empty<IFeature>(), true) { Diagnostics = diagnostics };
        }

        var withParts = await TryEvaluateWithPartsLibraryAsync(documents, trees, cancellationToken);
        if (withParts != null) return withParts;

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        EmitResult emit = compilation.Emit(
            peStream,
            pdbStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            cancellationToken: cancellationToken);

        var compileDiagnostics = ToScriptDiagnostics(emit.Diagnostics);
        if (!emit.Success)
        {
            return CompileFailure(documents, compileDiagnostics);
        }

        return await RunAsync(peStream, pdbStream, null, documents, compileDiagnostics);
    }

    /// <summary>
    /// A cena compilada sozinha contra as peças já compiladas (<see cref="ScenePartsLibrary"/>). <c>null</c>
    /// quando o resultado poderia sair diferente do da compilação única, que então é feita: mais de um arquivo
    /// com top-level statements, peças que não compilam sozinhas, erro na cena (para os diagnósticos saírem
    /// iguais) ou um tipo da cena com o mesmo nome de uma peça (CS0436, que na compilação única é erro).
    /// </summary>
    private static async Task<SceneEvaluationResult?> TryEvaluateWithPartsLibraryAsync(
        IReadOnlyList<ScriptDocument> documents,
        IReadOnlyList<SyntaxTree> trees,
        CancellationToken cancellationToken)
    {
        var entry = Enumerable.Range(0, trees.Count).Where(i => HasGlobalStatements(trees[i], cancellationToken)).ToList();
        if (entry.Count != 1 || trees.Count == 1) return null;

        var partIndices = Enumerable.Range(0, trees.Count).Where(i => i != entry[0]).ToList();
        var library = ScenePartsLibrary.GetOrCompile(
            partIndices.Select(i => documents[i]).ToList(),
            partIndices.Select(i => trees[i]).ToList(),
            new[] { GlobalUsingsTree.Value },
            References.Value,
            cancellationToken);
        if (library.Reference == null) return null;

        var compilation = CSharpCompilation.Create(
            ScenePartsLibrary.EntryAssemblyName,
            new[] { GlobalUsingsTree.Value, trees[entry[0]] },
            References.Value.Append(library.Reference),
            CompilationOptions(OutputKind.ConsoleApplication));

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        EmitResult emit = compilation.Emit(
            peStream,
            pdbStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            cancellationToken: cancellationToken);
        if (!emit.Success || emit.Diagnostics.Any(d => d.Id == "CS0436")) return null;

        var assembly = library.Load();
        if (assembly == null) return null;

        return await RunAsync(peStream, pdbStream, assembly, documents, ToScriptDiagnostics(library.Diagnostics.Concat(emit.Diagnostics)));
    }

    private static async Task<SceneEvaluationResult> RunAsync(
        MemoryStream peStream,
        MemoryStream pdbStream,
        Assembly? parts,
        IReadOnlyList<ScriptDocument> documents,
        IReadOnlyList<ScriptDiagnostic> compileDiagnostics)
    {
        peStream.Position = 0;
        pdbStream.Position = 0;

        var loadContext = new SceneLoadContext(parts);
        try
        {
            Assembly assembly = loadContext.LoadFromStream(peStream, pdbStream);
            MethodInfo entryPoint = assembly.EntryPoint
                ?? throw new InvalidOperationException("A cena compilou, mas não gerou ponto de entrada.");

            var builder = new SceneBuilder();
            using (SceneHost.Begin(builder))
            {
                object?[]? args = entryPoint.GetParameters().Length == 0 ? null : new object?[] { Array.Empty<string>() };
                object? returned = entryPoint.Invoke(null, args);
                if (returned is Task task)
                {
                    await task.ConfigureAwait(false);
                }
            }

            return new SceneEvaluationResult(builder.Features, true) { Diagnostics = compileDiagnostics };
        }
        catch (Exception ex)
        {
            var error = ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException! : ex;
            return RuntimeFailure(documents, error, compileDiagnostics);
        }
        finally
        {
            // Descarregamento cooperativo: o assembly só sai da memória quando nada mais
            // referencia seus tipos. As features são tipos do Domain, então isso é imediato.
            loadContext.Unload();
        }
    }

    private static SceneEvaluationResult CompileFailure(
        IReadOnlyList<ScriptDocument> documents,
        IReadOnlyList<ScriptDiagnostic> diagnostics)
    {
        string errors = string.Join(
            Environment.NewLine,
            diagnostics.Where(d => d.Severity == ScriptDiagnosticSeverity.Error));

        TryWriteLog("script_compile_error.log", documents, "ERRORS", errors);
        return new SceneEvaluationResult(Array.Empty<IFeature>(), false, errors) { Diagnostics = diagnostics };
    }

    private static SceneEvaluationResult RuntimeFailure(
        IReadOnlyList<ScriptDocument> documents,
        Exception error,
        IReadOnlyList<ScriptDiagnostic> compileDiagnostics)
    {
        var (file, line, column) = FindUserFrame(error, documents);
        var diagnostic = new ScriptDiagnostic(
            file ?? string.Empty,
            line,
            column,
            ScriptDiagnosticSeverity.Error,
            error.GetType().Name,
            error.Message);

        string message = file != null ? diagnostic.ToString() : $"{error.GetType().Name}: {error.Message}";

        TryWriteLog("script_runtime_error.log", documents, "ERROR", error.ToString());
        return new SceneEvaluationResult(Array.Empty<IFeature>(), false, message)
        {
            Diagnostics = compileDiagnostics.Append(diagnostic).ToList()
        };
    }

    /// <summary>Primeiro quadro da pilha que pertence a um arquivo da cena (via PDB em memória).</summary>
    private static (string? File, int Line, int Column) FindUserFrame(Exception error, IReadOnlyList<ScriptDocument> documents)
    {
        var paths = new HashSet<string>(documents.Select(d => d.FilePath), StringComparer.OrdinalIgnoreCase);
        foreach (var frame in new StackTrace(error, fNeedFileInfo: true).GetFrames())
        {
            string? file = frame.GetFileName();
            if (file != null && paths.Contains(file))
            {
                return (file, frame.GetFileLineNumber(), Math.Max(1, frame.GetFileColumnNumber()));
            }
        }
        return (null, 0, 0);
    }

    private static IReadOnlyList<ScriptDiagnostic> ToScriptDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(d =>
            {
                var span = d.Location.GetMappedLineSpan();
                bool hasLocation = d.Location.IsInSource;
                return new ScriptDiagnostic(
                    hasLocation ? span.Path : string.Empty,
                    hasLocation ? span.StartLinePosition.Line + 1 : 0,
                    hasLocation ? span.StartLinePosition.Character + 1 : 0,
                    d.Severity == DiagnosticSeverity.Error ? ScriptDiagnosticSeverity.Error : ScriptDiagnosticSeverity.Warning,
                    d.Id,
                    d.GetMessage());
            })
            .ToList();
    }

    private static string BuildGlobalUsingsSource()
    {
        var sb = new StringBuilder();
        foreach (var ns in SceneHost.GlobalUsings)
        {
            sb.AppendLine($"global using {ns};");
        }
        sb.AppendLine($"global using static {SceneHost.GlobalStaticUsing};");
        return sb.ToString();
    }

    /// <summary>
    /// Assemblies do runtime .NET (os mesmos do diretório de System.Private.CoreLib) + o Domain.
    /// Assemblies da aplicação (Avalonia, OCCT, UI) ficam de fora de propósito.
    /// </summary>
    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var trustedAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

        var paths = trustedAssemblies
            .Where(p => string.Equals(Path.GetDirectoryName(p), runtimeDir, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (paths.Count == 0)
        {
            paths.AddRange(new[] { "System.Runtime", "System.Collections", "System.Linq" }
                .Select(name => Assembly.Load(name).Location));
            paths.Add(typeof(object).Assembly.Location);
        }

        paths.Add(typeof(IFeature).Assembly.Location);

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }

    private static void TryWriteLog(string fileName, IReadOnlyList<ScriptDocument> documents, string header, string body)
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var doc in documents)
            {
                sb.Append("=== ").Append(doc.FilePath).AppendLine(" ===").AppendLine(doc.Text).AppendLine();
            }
            sb.Append("=== ").Append(header).AppendLine(" ===").AppendLine(body);
            File.WriteAllText(fileName, sb.ToString());
        }
        catch
        {
            // Log de depuração: falhar ao gravar não pode derrubar a avaliação.
        }
    }

    /// <summary>
    /// Contexto colecionável sem resolução própria: dependências (Domain, runtime) vêm do contexto
    /// padrão, então as features criadas pelo script são os mesmos tipos que a aplicação usa. A única
    /// exceção são as peças compiladas à parte (<see cref="ScenePartsLibrary"/>), que já estão carregadas.
    /// </summary>
    private sealed class SceneLoadContext(Assembly? parts) : AssemblyLoadContext("CadScene", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            parts != null && assemblyName.Name == parts.GetName().Name ? parts : null;
    }
}
