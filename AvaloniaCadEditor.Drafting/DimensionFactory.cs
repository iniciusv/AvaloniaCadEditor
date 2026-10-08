using System.Globalization;

namespace AvaloniaCadEditor.Drafting;

/// <summary>Formatação de valores e montagem de cotas avulsas (as manuais, e os diâmetros).</summary>
public static class DimensionFactory
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Formata um valor em mm: até duas casas, vírgula decimal ("3,4", "20,62").</summary>
    public static string Format(double value) => Math.Round(value, 2).ToString("0.##", PtBr);

    /// <summary>Espaçamento entre fileiras de cota, proporcional ao tamanho da peça na vista.</summary>
    public static double GapFor(P2 partMin, P2 partMax) =>
        Math.Clamp(Math.Max(partMax.U - partMin.U, partMax.V - partMin.V) * 0.08, 1.5, 15.0);

    /// <summary>Texto de um padrão de passo constante: "3× 20 (=60)".</summary>
    public static string PatternText(int steps, double pitch) =>
        $"{steps}× {Format(pitch)} (={Format(steps * pitch)})";

    /// <summary>Cota manual entre dois pontos, com a linha de cota na posição escolhida.</summary>
    public static DraftDimension Linear(DraftAnchor a, DraftAnchor b, DimensionKind kind, double linePosition)
    {
        double value = kind == DimensionKind.Horizontal
            ? Math.Abs(b.Point.U - a.Point.U)
            : Math.Abs(b.Point.V - a.Point.V);
        var sources = new[] { a, b }
            .Select(x => x.Id[..x.Id.LastIndexOf('.')])
            .Where(s => s != "peca")
            .Distinct()
            .ToList();
        return new DraftDimension($"m:{a.Id}|{b.Id}|{kind}", kind, a.Point, b.Point, linePosition, value, Format(value),
            sources, IsManual: true);
    }

    /// <summary>Cota manual de diâmetro de um furo visto de frente.</summary>
    public static DraftDimension Diameter(ViewCircle circle) =>
        Diameter(circle, $"m:{circle.SourceName}|Ø", $"Ø{Format(circle.Radius * 2)}", [circle.SourceName], isManual: true);

    /// <summary>Diâmetro: linha atravessando o círculo a 45°, texto na ponta da chamada.</summary>
    public static DraftDimension Diameter(ViewCircle circle, string key, string text, IReadOnlyList<string> sources, bool isManual)
    {
        var direction = new P2(Math.Sqrt(0.5), Math.Sqrt(0.5));
        return new DraftDimension(key, DimensionKind.Diameter,
            circle.Center - direction * circle.Radius, circle.Center + direction * circle.Radius,
            0, circle.Radius * 2, text, sources, isManual);
    }
}
