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
/// <see cref="AssemblyLoadContext"/> colecionável e descarrega o assembly ao final.
/// </summary>
public class RoslynSceneEvaluator : ISceneEvaluator
{
    private const string GlobalUsingsFile = "GlobalUsings.g.cs";

    private static readonly CSharpParseOptions ParseOptions =
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
    /// Compilação da cena (sem emitir nem executar), com as mesmas referências e usings globais
    /// da execução. Também serve à navegação no código (definição, referências, renomear).
    /// </summary>
    /// <param name="userTrees">Árvores dos arquivos da cena, na ordem de <paramref name="documents"/>.</param>
    public static CSharpCompilation CreateCompilation(
        IReadOnlyList<ScriptDocument> documents,
        out IReadOnlyList<SyntaxTree> userTrees,
        CancellationToken cancellationToken = default)
    {
        var trees = documents
            .Select(d => CSharpSyntaxTree.ParseText(
                SourceText.From(d.Text ?? string.Empty, Encoding.UTF8),
                ParseOptions,
                path: d.FilePath,
                cancellationToken: cancellationToken))
            .ToList();
        userTrees = trees;

        bool hasEntryPoint = trees.Any(t =>
            ((CompilationUnitSyntax)t.GetRoot(cancellationToken)).Members.OfType<GlobalStatementSyntax>().Any());

        return CSharpCompilation.Create(
            assemblyName: "CadScene_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: trees.Prepend(GlobalUsingsTree.Value),
            references: References.Value,
            options: new CSharpCompilationOptions(
                hasEntryPoint ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                nullableContextOptions: NullableContextOptions.Disable,
                concurrentBuild: true));
    }

    private static async Task<SceneEvaluationResult> EvaluateCoreAsync(
        IReadOnlyList<ScriptDocument> documents,
        CancellationToken cancellationToken)
    {
        var compilation = CreateCompilation(documents, out _, cancellationToken);
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

        peStream.Position = 0;
        pdbStream.Position = 0;

        var loadContext = new SceneLoadContext();
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
    /// padrão, então as features criadas pelo script são os mesmos tipos que a aplicação usa.
    /// </summary>
    private sealed class SceneLoadContext : AssemblyLoadContext
    {
        public SceneLoadContext() : base("CadScene", isCollectible: true) { }

        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
