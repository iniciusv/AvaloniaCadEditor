using System;

namespace AvaloniaCadEditor.Domain.ValueObjects;

/// <summary>O que foi escolhido para projetar no plano do esboço.</summary>
public enum ProjectionTarget
{
    /// <summary>Uma face do sólido: as bordas e silhuetas dela.</summary>
    Face,

    /// <summary>O sólido inteiro, no modo de <see cref="VolumeProjectionMode"/>.</summary>
    Body
}

/// <summary>Como o volume projetado vira referência.</summary>
public enum VolumeProjectionMode
{
    /// <summary>Só a sombra: contorno externo e furos que atravessam.</summary>
    Outline,

    /// <summary>Todas as quinas e silhuetas, inclusive as escondidas.</summary>
    AllEdges
}

/// <summary>
/// Uma face ou um sólido escolhido como referência de um plano de trabalho.
/// <list type="bullet">
/// <item>A feature é guardada pelo Id e pelo nome: rodar a cena de novo cria features com Ids novos, e o nome
/// (a variável do CadScript) é o que a reencontra.</item>
/// <item>A face é guardada pelo ponto clicado e pela normal ali, e não pelo índice do triângulo: o índice muda
/// quando o sólido é retesselado, o ponto e a normal reencontram a mesma face na malha nova.</item>
/// </list>
/// </summary>
public sealed record ProjectionSource(Guid FeatureId, string FeatureName, ProjectionTarget Target, Point3D Seed, Vector3D SeedNormal)
{
    public static ProjectionSource Face(Features.IFeature feature, RaycastHit hit) => new(feature.Id, feature.Name, ProjectionTarget.Face, hit.Point, hit.Normal);

    public static ProjectionSource Body(Features.IFeature feature, RaycastHit hit) => new(feature.Id, feature.Name, ProjectionTarget.Body, hit.Point, hit.Normal);

    /// <summary>A mesma fonte, apontando para a feature recriada com outro Id.</summary>
    public ProjectionSource Rebind(Guid featureId) => this with { FeatureId = featureId };
}
