using System;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Isolation;

/// <summary>
/// Calcula o <see cref="PartFrame"/> de uma feature isolada com H. Função pura: recebe a feature e
/// a malha dela já em coordenadas do mundo, devolve o referencial (ou <c>null</c>).
/// </summary>
public static class PartFrameResolver
{
    /// <summary>
    /// Só há referencial para uma peça posicionada (<see cref="PartInstance"/> ou
    /// <see cref="TransformedFeature"/>) cujo eixo Z local continue no eixo Z do mundo — a câmera é
    /// orbital com Z para cima, então só a rotação em Z as vistas padrão conseguem acompanhar.
    /// Espelhos que mantêm o eixo Z (<c>MirrorZ</c>, <c>MirrorX</c>...) também valem: o referencial
    /// fica sendo a rotação em Z que leva o X da peça, e o espelho segue só no posicionamento das
    /// features (o desenho mostra a peça como ela está na cena). Qualquer outro caso devolve
    /// <c>null</c> e o isolamento segue sem referencial (grade na origem do mundo).
    /// </summary>
    /// <param name="feature">A feature de topo isolada.</param>
    /// <param name="worldPositions">Posições xyz (planas) da malha da feature, em coordenadas do mundo.</param>
    public static PartFrame? Resolve(IFeature feature, float[]? worldPositions)
    {
        ArgumentNullException.ThrowIfNull(feature);

        var placement = feature switch
        {
            PartInstance instance => instance.Placement,
            TransformedFeature transformed => transformed.Placement,
            _ => null
        };

        if (placement is null || worldPositions is null || worldPositions.Length < 3)
        {
            return null;
        }

        var z = placement.ApplyVector(Vector3D.UnitZ);
        if (Math.Abs(Math.Abs(z.Z) - 1) > 1e-9)
        {
            return null;
        }

        // Rotação em Z que leva o X da peça (igual ao placement quando não há espelho).
        var axes = Placement.RotationZ(placement.RotationZRadians * 180.0 / Math.PI)
            .Then(Placement.Translate(placement.Translation));

        // Caixa medida nos eixos do referencial: volta cada vértice para esses eixos.
        var worldToLocal = axes.Inverse();
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        for (int i = 0; i + 2 < worldPositions.Length; i += 3)
        {
            var local = worldToLocal.Apply(new Point3D(worldPositions[i], worldPositions[i + 1], worldPositions[i + 2]));
            if (local.X < minX) minX = local.X;
            if (local.Y < minY) minY = local.Y;
            if (local.Z < minZ) minZ = local.Z;
        }

        var frameToWorld = Placement.Translate(new Vector3D(minX, minY, minZ)).Then(axes);
        return new PartFrame(feature.Name, frameToWorld);
    }
}
