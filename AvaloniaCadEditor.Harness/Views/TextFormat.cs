using System.Globalization;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Harness.Views;

/// <summary>Números e eixos no texto para a LLM: invariante, até 3 casas, como o DraftingReport.</summary>
internal static class TextFormat
{
    public static string N(double value) => (Math.Abs(value) < 5e-4 ? 0 : value).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>"+X", "-Z"... para eixos; o vetor por extenso para direções oblíquas (Iso).</summary>
    public static string Axis(Vector3D v)
    {
        if (Math.Abs(Math.Abs(v.X) - 1) < 1e-9) return v.X > 0 ? "+X" : "-X";
        if (Math.Abs(Math.Abs(v.Y) - 1) < 1e-9) return v.Y > 0 ? "+Y" : "-Y";
        if (Math.Abs(Math.Abs(v.Z) - 1) < 1e-9) return v.Z > 0 ? "+Z" : "-Z";
        return $"({N(v.X)}, {N(v.Y)}, {N(v.Z)})";
    }
}
