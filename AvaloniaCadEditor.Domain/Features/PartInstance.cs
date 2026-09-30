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

    /// <summary>
    /// Instâncias transladadas e giradas podem ser operandos de <see cref="BooleanFeature"/>: o kernel
    /// reconstrói o sólido já em coordenadas do mundo (o OcctNet.Wrapper só expõe translação de sólidos
    /// prontos, então uma instância girada refaz a extrusão/box/etc. com os pontos já transformados).
    /// A única exceção é quando a definição depende de arestas ou malha de um sólido não-girado
    /// (<see cref="FilletFeature"/>, <see cref="ImportedMeshFeature"/>): esses não podem ser refeitos
    /// girados, então continuam bloqueados.
    /// </summary>
    internal static void EnsureCanBeBooleanOperand(IFeature feature)
    {
        if (feature is PartInstance instance && instance.Placement.HasRotation && DependsOnFixedGeometry(instance.Definition))
        {
            throw new NotSupportedException(
                $"A instância '{instance.Name}' ({instance.PartTypeName}) está girada e não pode ser operando de uma " +
                "operação booleana: a definição usa fillet ou malha importada, que dependem das arestas/malha do sólido " +
                "não-girado e não podem ser reconstruídos girados. Faça a operação dentro da peça (no Build) ou use uma " +
                "instância sem rotação.");
        }

        if (feature is TransformedFeature transformed && transformed.Placement.HasRotation && DependsOnFixedGeometry(transformed.Source))
        {
            throw new NotSupportedException(
                $"'{transformed.Name}' está girada (Transformed) e não pode ser operando de uma operação booleana: " +
                "depende de fillet ou malha importada, que dependem das arestas/malha do sólido não-girado e não podem " +
                "ser reconstruídos girados. Use uma transformação sem rotação, ou aplique o fillet depois da booleana.");
        }
    }

    /// <summary>True se, em algum ponto da árvore de features, houver um fillet ou uma malha importada.</summary>
    private static bool DependsOnFixedGeometry(IFeature feature) => feature switch
    {
        FilletFeature => true,
        ImportedMeshFeature => true,
        BooleanFeature boolean => boolean.BaseFeatures.Any(DependsOnFixedGeometry) ||
                                   boolean.ToolFeatures.Any(DependsOnFixedGeometry),
        PartInstance nested => DependsOnFixedGeometry(nested.Definition),
        TransformedFeature transformed => DependsOnFixedGeometry(transformed.Source),
        _ => false
    };
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
