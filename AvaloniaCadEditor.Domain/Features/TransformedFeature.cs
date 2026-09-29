using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

/// <summary>
/// Uma feature qualquer (não só peças) já transformada por um <see cref="Placement"/> — usada para
/// copiar, girar ou mover uma feature solta do script sem reescrever suas coordenadas originais.
/// <see cref="Source"/> continua intocada (e pode ser compartilhada por mais de um
/// <see cref="TransformedFeature"/>, como uma cópia e o original).
/// </summary>
/// <example>
/// <code>
/// AddFeature(prism_60e9.Transformed(Placement.RotationAbout(new Point3D(20, 10, 0), Vector3D.UnitZ, 90)));
/// </code>
/// </example>
public sealed class TransformedFeature : FeatureBase
{
    public TransformedFeature(IFeature source, Placement placement, string? name = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Placement = placement ?? Placement.Identity;
        Name = string.IsNullOrEmpty(name) ? source.Name : name;
    }

    /// <summary>A feature original, em coordenadas locais (nunca modificada por este objeto).</summary>
    public IFeature Source { get; }

    /// <summary>Transformação aplicada sobre <see cref="Source"/>.</summary>
    public Placement Placement { get; }

    public override IEnumerable<IFeature> Dependencies
    {
        get { yield return Source; }
    }

    public override IReadOnlyList<Point3D> GetVertices() =>
        Source.GetVertices().Select(Placement.Apply).ToList();
}

/// <summary>
/// Vista pelos scripts (o namespace <c>AvaloniaCadEditor.Domain.Features</c> já entra por
/// <c>global using</c> em toda cena — ver <see cref="Scripting.SceneHost.GlobalUsings"/>), para
/// copiar/girar/mover uma feature solta sem reescrever suas coordenadas:
/// <c>AddFeature(x.Transformed(Placement.At(10, 0, 0)))</c>.
/// </summary>
public static class FeatureTransformExtensions
{
    public static TransformedFeature Transformed(this IFeature feature, Placement placement, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(placement);

        // Achata: Transformed de um Transformed vira um só (evita empilhar objetos à toa e mantém
        // "mesmo pivô/eixo soma o ângulo" simples de reconhecer no código).
        if (feature is TransformedFeature already)
        {
            return new TransformedFeature(already.Source, already.Placement.Then(placement), name);
        }
        return new TransformedFeature(feature, placement, name);
    }
}
