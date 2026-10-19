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
    private const double Snap = 1e-9;

    /// <summary>
    /// Há referencial para qualquer peça posicionada (<see cref="PartInstance"/> ou
    /// <see cref="TransformedFeature"/>): os eixos são os da própria peça, em qualquer rotação — a
    /// câmera acompanha com uma rolagem (<see cref="Domain.Entities.CameraState.Roll"/>). Só devolve
    /// <c>null</c> para uma feature sem posicionamento (solta na cena). Dois casos especiais:
    /// <list type="bullet">
    /// <item>Espelho que mantém o eixo Z do mundo em ±Z (<c>MirrorZ</c>, <c>MirrorX</c> seguido de
    /// giro em Z...): o referencial fica sendo a rotação em Z que leva o X da peça, sempre com Z do
    /// referencial para cima no mundo — o espelho segue só no posicionamento das features dentro dele
    /// (regra congelada pelos testes de peças espelhadas).</item>
    /// <item>Outro espelho (a peça fica de "mão trocada" fora de Z): usa X e Y da peça e fecha a base
    /// com <c>Z = X × Y</c>, para o referencial continuar sendo uma rotação pura (nunca um espelho).</item>
    /// </list>
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

        Vector3D xAxis, yAxis, zAxis;
        if (!placement.IsMirrored)
        {
            // Rotação pura, em qualquer eixo: os eixos da própria peça.
            xAxis = Clean(placement.ApplyVector(Vector3D.UnitX));
            yAxis = Clean(placement.ApplyVector(Vector3D.UnitY));
            zAxis = Clean(placement.ApplyVector(Vector3D.UnitZ));
        }
        else if (Math.Abs(Math.Abs(Clean(placement.ApplyVector(Vector3D.UnitZ)).Z) - 1) < Snap)
        {
            // Espelho que mantém o eixo Z do mundo: regra congelada — gira em Z para acompanhar o X
            // da peça, com o referencial sempre "de pé" (Z para cima), não de cabeça para baixo.
            var axes = Placement.RotationZ(placement.RotationZRadians * 180.0 / Math.PI);
            xAxis = Clean(axes.ApplyVector(Vector3D.UnitX));
            yAxis = Clean(axes.ApplyVector(Vector3D.UnitY));
            zAxis = Clean(axes.ApplyVector(Vector3D.UnitZ));
        }
        else
        {
            // Outro espelho: X e Y da peça, Z = X × Y fecha a base como rotação (nunca espelho).
            xAxis = Clean(placement.ApplyVector(Vector3D.UnitX));
            yAxis = Clean(placement.ApplyVector(Vector3D.UnitY));
            zAxis = Clean(xAxis.CrossProduct(yAxis));
        }

        var rotation = Placement.FromAxes(xAxis, yAxis, zAxis, new Vector3D(0, 0, 0));

        // Caixa medida nos eixos do referencial: volta cada vértice para esses eixos.
        var worldToLocal = rotation.Inverse();
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        for (int i = 0; i + 2 < worldPositions.Length; i += 3)
        {
            var local = worldToLocal.Apply(new Point3D(worldPositions[i], worldPositions[i + 1], worldPositions[i + 2]));
            if (local.X < minX) minX = local.X;
            if (local.Y < minY) minY = local.Y;
            if (local.Z < minZ) minZ = local.Z;
        }

        var frameToWorld = Placement.Translate(new Vector3D(minX, minY, minZ)).Then(rotation);
        return new PartFrame(feature.Name, frameToWorld);
    }

    /// <summary>Elimina ruído de ponto flutuante: componente perto de 0 ou ±1 vira exatamente isso.</summary>
    private static Vector3D Clean(Vector3D v)
    {
        var n = v.Normalize();
        return new Vector3D(Round(n.X), Round(n.Y), Round(n.Z)).Normalize();
    }

    private static double Round(double x) =>
        Math.Abs(x) < Snap ? 0 : Math.Abs(Math.Abs(x) - 1) < Snap ? Math.Sign(x) : x;
}
