using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Scripting;

/// <summary>
/// API global vista pelo código de uma cena. Os scripts a importam com
/// <c>global using static AvaloniaCadEditor.Domain.Scripting.Scene;</c>, por isso
/// <c>AddFeature(x)</c> funciona sem prefixo. Deve expor apenas o que o script pode chamar.
/// </summary>
public static class Scene
{
    /// <summary>
    /// Registra uma feature na cena que está sendo avaliada. Os parâmetros opcionais são
    /// preenchidos pelo compilador e registram de onde a feature veio (arquivo, linha, variável).
    /// </summary>
    public static void AddFeature(
        IFeature feature,
        [CallerArgumentExpression(nameof(feature))] string? expression = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int line = 0)
    {
        if (feature == null) return;
        FeatureOrigin.Set(feature, new SourceOrigin(filePath, line, expression));
        SceneHost.CurrentBuilder.AddFeature(feature);
    }

    /// <summary>
    /// Coloca uma instância da peça na cena. Peças com parâmetros iguais compartilham a mesma
    /// geometria (construída uma vez); cada instância só acrescenta sua posição.
    /// </summary>
    /// <param name="part">A peça (um record que herda de <see cref="Part"/>).</param>
    /// <param name="at">Posição e rotação; padrão: na origem, sem rotação.</param>
    /// <param name="name">Nome exibido na árvore; padrão: TipoDaPeca_N.</param>
    /// <param name="color">Cor "#RRGGBB"; padrão: a <see cref="IFeature.Color"/> da geometria da peça, se houver.</param>
    public static PartInstance<T> Add<T>(
        T part,
        Placement? at = null,
        string? name = null,
        string? color = null,
        [CallerArgumentExpression(nameof(part))] string? expression = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int line = 0) where T : Part
    {
        ArgumentNullException.ThrowIfNull(part);
        var builder = SceneHost.CurrentBuilder;
        var definition = builder.GetOrBuildDefinition(part);
        var instance = new PartInstance<T>(part, definition, at ?? Placement.Identity)
        {
            Name = name ?? builder.NextInstanceName(part.GetType().Name),
            Color = color ?? definition.Color
        };
        FeatureOrigin.Set(instance, new SourceOrigin(filePath, line, expression));
        builder.AddFeature(instance);
        return instance;
    }
}

/// <summary>Onde, no código da cena, uma feature foi registrada.</summary>
/// <param name="FilePath">Arquivo da chamada (relativo à cena quando avaliada pelo app).</param>
/// <param name="Line">Linha da chamada, começando em 1.</param>
/// <param name="Expression">Texto do argumento passado a <c>AddFeature</c> (ex.: <c>box_1</c>).</param>
public sealed record SourceOrigin(string FilePath, int Line, string? Expression)
{
    private static readonly Regex IdentifierPattern = new(@"^@?[\p{L}_][\p{L}\p{Nd}_]*$", RegexOptions.CultureInvariant);

    /// <summary>Nome da variável quando o argumento era um identificador simples; senão null.</summary>
    public string? Variable =>
        Expression != null && IdentifierPattern.IsMatch(Expression) ? Expression.TrimStart('@') : null;
}

/// <summary>
/// Origem de cada feature no código-fonte. Fica fora de <see cref="FeatureBase"/> para não virar
/// parte da API vista pelos scripts; a tabela é fraca, então não segura features descartadas.
/// </summary>
public static class FeatureOrigin
{
    private static readonly ConditionalWeakTable<IFeature, SourceOrigin> Origins = new();

    public static SourceOrigin? Of(IFeature feature) =>
        feature != null && Origins.TryGetValue(feature, out var origin) ? origin : null;

    /// <summary>
    /// Nome pelo qual o código se refere à feature: a variável passada a <c>AddFeature</c> ou,
    /// na falta dela, o <see cref="IFeature.Name"/>.
    /// </summary>
    public static string VariableName(IFeature feature) => Of(feature)?.Variable ?? feature.Name;

    internal static void Set(IFeature feature, SourceOrigin origin) => Origins.AddOrUpdate(feature, origin);
}

/// <summary>
/// Acumula o resultado de uma execução de cena. Cada avaliação usa um builder próprio,
/// então avaliações concorrentes (ex.: a cena e o chat de IA validando um script) não se misturam.
/// </summary>
public sealed class SceneBuilder
{
    private readonly List<IFeature> _features = new();
    private readonly Dictionary<Part, IFeature> _definitions = new();
    private readonly Dictionary<string, int> _instanceCounters = new(StringComparer.Ordinal);

    public IReadOnlyList<IFeature> Features => _features;

    /// <summary>Quantas vezes um <see cref="Part"/> teve a geometria construída nesta execução.</summary>
    public int DefinitionBuildCount { get; private set; }

    /// <summary>
    /// Geometria local da peça. A chave é a igualdade por valor do record: peças com os mesmos
    /// parâmetros recebem o mesmo objeto, e o kernel (que guarda formas por Id) as constrói uma vez.
    /// </summary>
    public IFeature GetOrBuildDefinition(Part part)
    {
        if (!_definitions.TryGetValue(part, out var definition))
        {
            definition = part.CreateDefinition();
            _definitions[part] = definition;
            DefinitionBuildCount++;
        }
        return definition;
    }

    internal string NextInstanceName(string typeName)
    {
        _instanceCounters.TryGetValue(typeName, out int n);
        _instanceCounters[typeName] = ++n;
        return $"{typeName}_{n}";
    }

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
