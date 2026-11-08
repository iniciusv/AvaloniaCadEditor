using System;
using System.Globalization;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Scenes;

/// <summary>
/// Um <see cref="Placement"/> como código C# de cena, para qualquer rotação. Prefere a forma legível
/// (<c>Placement.Translate</c>, <c>Placement.At(..., rotationZDegrees:)</c>, eixos inteiros em
/// <c>Placement.FromAxes</c>) e só cai nos números com todas as casas quando a rotação é um ângulo
/// qualquer: arredondar ali deixaria os eixos fora da tolerância do <c>FromAxes</c> e o sólido fora do
/// lugar que o <see cref="PartMergeCheck"/> confere.
/// </summary>
public static class PlacementSource
{
    public static string Code(Placement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        if (placement.IsMirrored)
        {
            // MirrorX é a própria inversa: placement = MirrorX ∘ (MirrorX ∘ placement), e o segundo fator
            // já não espelha.
            return $"Placement.MirrorX().Then({Code(Placement.MirrorX().Then(placement))})";
        }

        var t = placement.Translation;
        if (!placement.HasRotation) return $"Placement.Translate({Vector(t)})";

        if (placement.IsRotationAboutZOnly)
        {
            double degrees = placement.RotationZRadians * 180.0 / Math.PI;
            if (degrees <= -180 + 1e-9) degrees += 360;
            if (IsClean(degrees))
                return $"Placement.At({Number(t.X)}, {Number(t.Y)}, {Number(t.Z)}, rotationZDegrees: {Number(degrees)})";
        }

        var x = placement.ApplyVector(Vector3D.UnitX);
        var y = placement.ApplyVector(Vector3D.UnitY);
        var z = placement.ApplyVector(Vector3D.UnitZ);
        return $"Placement.FromAxes({Vector(x)}, {Vector(y)}, {Vector(z)}, {Vector(t)})";
    }

    /// <summary>Número sem ruído (6 casas, sem "-0") quando isso não muda o valor; senão, todas as casas.</summary>
    public static string Number(double value)
    {
        double rounded = Math.Round(value, 6);
        if (rounded == 0) rounded = 0;
        return Math.Abs(rounded - value) < 1e-12
            ? rounded.ToString("0.######", CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string Vector(Vector3D v) => $"new Vector3D({Number(v.X)}, {Number(v.Y)}, {Number(v.Z)})";

    public static string Point(Point3D p) => $"new Point3D({Number(p.X)}, {Number(p.Y)}, {Number(p.Z)})";

    private static bool IsClean(double value) => Math.Abs(Math.Round(value, 6) - value) < 1e-9;
}
