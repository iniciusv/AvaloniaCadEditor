using System;
using System.Collections.Generic;
using System.Threading;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Domain.Scripting;

/// <summary>
/// API global vista pelo código de uma cena. Os scripts a importam com
/// <c>global using static AvaloniaCadEditor.Domain.Scripting.Scene;</c>, por isso
/// <c>AddFeature(x)</c> funciona sem prefixo. Deve expor apenas o que o script pode chamar.
/// </summary>
public static class Scene
{
    /// <summary>Registra uma feature na cena que está sendo avaliada.</summary>
    public static void AddFeature(IFeature feature) => SceneHost.CurrentBuilder.AddFeature(feature);
}

/// <summary>
/// Acumula o resultado de uma execução de cena. Cada avaliação usa um builder próprio,
/// então avaliações concorrentes (ex.: a cena e o chat de IA validando um script) não se misturam.
/// </summary>
public sealed class SceneBuilder
{
    private readonly List<IFeature> _features = new();

    public IReadOnlyList<IFeature> Features => _features;

    public void AddFeature(IFeature feature)
    {
        if (feature != null)
        {
            _features.Add(feature);
        }
    }
}

/// <summary>
/// Lado do host: define qual <see cref="SceneBuilder"/> recebe as chamadas de <see cref="Scene"/>.
/// Fica fora de <see cref="Scene"/> para não aparecer como função global nos scripts.
/// </summary>
public static class SceneHost
{
    /// <summary>
    /// Namespaces importados automaticamente em todo arquivo de cena (equivalem aos itens
    /// <c>&lt;Using&gt;</c> de um .csproj de cena).
    /// </summary>
    public static readonly IReadOnlyList<string> GlobalUsings = new[]
    {
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "AvaloniaCadEditor.Domain.Features",
        "AvaloniaCadEditor.Domain.ValueObjects",
        "AvaloniaCadEditor.Domain.Profiles",
    };

    /// <summary>Tipo importado com <c>using static</c> em todo arquivo de cena.</summary>
    public static readonly string GlobalStaticUsing = typeof(Scene).FullName!;

    private static readonly AsyncLocal<SceneBuilder?> Current = new();

    // Usado quando o código roda sem host (ex.: `dotnet run` de uma cena autônoma):
    // as chamadas não falham, apenas acumulam aqui.
    private static readonly SceneBuilder Unhosted = new();

    internal static SceneBuilder CurrentBuilder => Current.Value ?? Unhosted;

    /// <summary>
    /// Direciona as chamadas de <see cref="Scene"/> para <paramref name="builder"/> até o
    /// escopo retornado ser descartado. O valor flui para chamadas assíncronas (AsyncLocal).
    /// </summary>
    public static IDisposable Begin(SceneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var previous = Current.Value;
        Current.Value = builder;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly SceneBuilder? _previous;
        private bool _disposed;

        public Scope(SceneBuilder? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Current.Value = _previous;
        }
    }
}
