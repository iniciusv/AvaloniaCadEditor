using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

/// <summary>
/// Uma peça posicionada na cena. A <see cref="Definition"/> (geometria local) é compartilhada por
/// todas as instâncias de peças iguais; cada instância só acrescenta o <see cref="Placement"/>.
/// </summary>
public abstract class PartInstance : FeatureBase
{
    protected PartInstance(Part part, IFeature definition, Placement placement)
    {
        Part = part ?? throw new ArgumentNullException(nameof(part));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Placement = placement ?? Placement.Identity;
    }

    /// <summary>A receita (parâmetros) desta instância.</summary>
    public Part Part { get; }

    /// <summary>Geometria da peça em coordenadas locais; a mesma para peças com parâmetros iguais.</summary>
    public IFeature Definition { get; }

    /// <summary>Posição e rotação desta instância no mundo.</summary>
    public Placement Placement { get; }

    public string PartTypeName => Part.GetType().Name;

    /// <summary>Converte um ponto local da peça (ex.: uma "porta") para coordenadas do mundo.</summary>
    public Point3D ToWorld(Point3D local) => Placement.Apply(local);

    /// <summary>Converte uma direção local da peça para coordenadas do mundo.</summary>
    public Vector3D ToWorld(Vector3D localDirection) => Placement.ApplyVector(localDirection);

    public override IReadOnlyList<Point3D> GetVertices() =>
        Definition.GetVertices().Select(Placement.Apply).ToList();
}

/// <summary>Instância tipada: dá acesso às propriedades da peça (ex.: portas) sem cast.</summary>
public sealed class PartInstance<T> : PartInstance where T : Part
{
    public PartInstance(T part, IFeature definition, Placement placement)
        : base(part, definition, placement)
    {
    }

    public new T Part => (T)base.Part;

    /// <summary>Uma porta da peça em coordenadas do mundo: <c>flange.World(f => f.CentroTopo)</c>.</summary>
    public Point3D World(Func<T, Point3D> port) => Placement.Apply(port(Part));
}
