using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace AvaloniaCadEditor.Domain.Features;

/// <summary>
/// Peça paramétrica reutilizável. Declare como <c>record</c>: os parâmetros são o construtor
/// primário, e o compilador gera a igualdade por valor que o app usa como cache (peças com os
/// mesmos parâmetros são construídas uma vez só) e o <c>with</c> para variantes.
/// </summary>
/// <example>
/// <code>
/// public sealed record Placa(double Largura, double Comprimento, double Espessura = 5) : Part
/// {
///     protected override IFeature Build() =>
///         new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "placa" };
/// }
///
/// // Na cena:
/// var p1 = Add(new Placa(100, 60));
/// var p2 = Add(p1.Part with { Espessura = 8 }, at: Placement.At(150, 0, 0));
/// </code>
/// </example>
public abstract record Part
{
    /// <summary>
    /// Constrói a geometria da peça em coordenadas locais (origem da peça em (0,0,0)).
    /// Deve ser pura: não chame AddFeature aqui; a posição vem do <see cref="Placement"/> de cada instância.
    /// </summary>
    protected abstract IFeature Build();

    /// <summary>Parâmetros do construtor primário, na ordem declarada, com os valores desta peça.</summary>
    public IReadOnlyList<PartParameter> GetParameters()
    {
        var ctor = PrimaryConstructor(GetType());
        if (ctor == null) return Array.Empty<PartParameter>();

        var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        return ctor.GetParameters()
            .Select(p =>
            {
                var prop = properties.FirstOrDefault(pr => string.Equals(pr.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                return new PartParameter(p.Name ?? "?", p.ParameterType, prop?.GetValue(this), p.GetCustomAttribute<RangeAttribute>());
            })
            .ToList();
    }

    /// <summary>Lança <see cref="ArgumentOutOfRangeException"/> se algum parâmetro viola seu [Range].</summary>
    public void Validate()
    {
        foreach (var parameter in GetParameters())
        {
            if (parameter.Range != null && !parameter.Range.IsValid(parameter.Value))
            {
                throw new ArgumentOutOfRangeException(
                    parameter.Name,
                    parameter.Value,
                    $"{GetType().Name}.{parameter.Name} = {Format(parameter.Value)} está fora do intervalo " +
                    $"[{Format(parameter.Range.Minimum)}, {Format(parameter.Range.Maximum)}].");
            }
        }
    }

    internal IFeature CreateDefinition()
    {
        Validate();
        var definition = Build()
            ?? throw new InvalidOperationException($"{GetType().Name}.Build() retornou null.");
        if (string.IsNullOrEmpty(definition.Name) || definition.Name == "Feature")
        {
            definition.Name = GetType().Name;
        }
        return definition;
    }

    private static ConstructorInfo? PrimaryConstructor(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            // Ignora o construtor de cópia gerado para records: Tipo(Tipo original).
            .Where(c => !(c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == type))
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

    private static string Format(object? value) =>
        value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "null";
}

/// <summary>Um parâmetro de peça: nome, tipo, valor atual e o intervalo permitido (se houver [Range]).</summary>
public sealed record PartParameter(string Name, Type Type, object? Value, RangeAttribute? Range)
{
    public override string ToString() =>
        $"{Name} = {(Value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : Value)}";
}
