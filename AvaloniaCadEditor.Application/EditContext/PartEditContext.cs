using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.EditContext;

/// <summary>
/// A peça aberta para edição (duplo clique numa instância): as ferramentas que criam geometria gravam no
/// <c>Build()</c> do record dela, em coordenadas locais, e não na cena. Guarda o nome da instância, e não
/// o objeto, porque cada execução da cena recria todas as features: quem usa o contexto acha a instância
/// de novo pelo nome (ver <see cref="PartSolidInserter"/>).
/// </summary>
/// <param name="InstanceName">Nome da instância aberta na cena (ex.: "placa_suporte_carrinho_y_a").</param>
/// <param name="PartTypeName">O record que recebe as edições (ex.: "PlacaSuporteCarrinhoY").</param>
/// <param name="Placement">Placement da instância na última execução: leva o local da peça ao mundo.</param>
/// <param name="InstanceCount">Quantas instâncias de topo da cena usam o mesmo record (todas mudam juntas).</param>
public sealed record PartEditContext(string InstanceName, string PartTypeName, Placement Placement, int InstanceCount)
{
    /// <summary>O contexto de uma instância de topo da cena; null para qualquer outra coisa.</summary>
    public static PartEditContext? For(IFeature? feature, IEnumerable<IFeature> sceneFeatures)
    {
        ArgumentNullException.ThrowIfNull(sceneFeatures);
        if (feature is not PartInstance instance) return null;

        int count = sceneFeatures.OfType<PartInstance>().Count(i => i.PartTypeName == instance.PartTypeName);
        return new PartEditContext(instance.Name, instance.PartTypeName, instance.Placement, Math.Max(count, 1));
    }

    /// <summary>Um ponto do mundo nas coordenadas do <c>Build()</c> da peça.</summary>
    public Point3D ToLocal(Point3D world) => Placement.Inverse().Apply(world);
}
