using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Levers;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;
using Microsoft.CodeAnalysis;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

/// <summary>
/// Compila a cena instrumentada (<see cref="RoslynLeverInstrumenter"/>) uma vez e a carrega num contexto
/// colecionável próprio. Cada execução da sonda roda o ponto de entrada da cena com um
/// <see cref="SceneBuilder"/> novo (<see cref="SceneHost"/> é por execução), então as execuções não se
/// misturam entre si nem com a cena da UI.
/// </summary>
public sealed class RoslynLeverProbeFactory : ILeverProbeFactory
{
    public ILeverProbe? Create(IReadOnlyList<ScriptDocument> documents, string partTypeName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (string.IsNullOrEmpty(partTypeName)) return null;

        var compilation = RoslynSceneEvaluator.CreateCompilation(documents, out var trees, cancellationToken);
        if (compilation.Options.OutputKind != OutputKind.ConsoleApplication) return null;

        var instrumented = RoslynLeverInstrumenter.Instrument(compilation, trees, partTypeName, cancellationToken);
        if (instrumented == null) return null;

        using var peStream = new MemoryStream();
        var emit = instrumented.Compilation.Emit(peStream, cancellationToken: cancellationToken);
        if (!emit.Success) return null;
        peStream.Position = 0;

        var context = new ProbeLoadContext();
        try
        {
            var assembly = context.LoadFromStream(peStream);
            var values = assembly.GetType(RoslynLeverInstrumenter.HelperClass)
                ?.GetField("Values", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as Dictionary<int, double>;
            if (assembly.EntryPoint is not { } entryPoint || values == null)
            {
                context.Unload();
                return null;
            }
            return new Probe(context, entryPoint, values, instrumented.Levers);
        }
        catch (Exception)
        {
            context.Unload();
            return null;
        }
    }

    private sealed class Probe(AssemblyLoadContext context, MethodInfo entryPoint, Dictionary<int, double> values, IReadOnlyList<CodeLever> levers)
        : ILeverProbe
    {
        private readonly object _gate = new();
        private bool _disposed;

        public IReadOnlyList<CodeLever> Levers { get; } = levers;

        public IReadOnlyList<IFeature>? Run(IReadOnlyDictionary<int, double> overrides)
        {
            ArgumentNullException.ThrowIfNull(overrides);
            lock (_gate)
            {
                if (_disposed) return null;

                values.Clear();
                foreach (var (id, value) in overrides) values[id] = value;
                try
                {
                    var builder = new SceneBuilder();
                    using (SceneHost.Begin(builder))
                    {
                        object?[]? args = entryPoint.GetParameters().Length == 0 ? null : new object?[] { Array.Empty<string>() };
                        if (entryPoint.Invoke(null, args) is Task task) task.GetAwaiter().GetResult();
                    }
                    return builder.Features;
                }
                catch (Exception)
                {
                    return null;
                }
                finally
                {
                    values.Clear();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                context.Unload();
            }
        }
    }

    /// <summary>Como o contexto da cena: Domain e runtime vêm do contexto padrão, para as features serem
    /// os mesmos tipos que a aplicação usa.</summary>
    private sealed class ProbeLoadContext() : AssemblyLoadContext("CadLeverProbe", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
