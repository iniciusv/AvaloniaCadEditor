using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Drafting;

/// <summary>Ponto de entrada do módulo: monta o desenho de uma vista de uma peça.</summary>
public static class DraftingAnalyzer
{
    /// <summary>O desenho da vista, ou <c>null</c> se a vista não é ortogonal (Iso).</summary>
    public static DraftingSheet? Analyze(DraftingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var basis = ViewBasis.For(input.View);
        if (basis == null) return null;

        var (entities, warnings) = ViewProjector.Project(input.Root, input.RootToFrame, basis);

        // Contorno real da peça (da malha, já com as booleanas) projetado na vista.
        var min = input.FrameBoundsMin;
        var max = input.FrameBoundsMax;
        var corners = new[]
        {
            new Point3D(min.X, min.Y, min.Z), new Point3D(max.X, min.Y, min.Z),
            new Point3D(min.X, max.Y, min.Z), new Point3D(max.X, max.Y, min.Z),
            new Point3D(min.X, min.Y, max.Z), new Point3D(max.X, min.Y, max.Z),
            new Point3D(min.X, max.Y, max.Z), new Point3D(max.X, max.Y, max.Z)
        };
        var projected = corners.Select(basis.Project).ToList();
        var partMin = new P2(projected.Min(p => p.U), projected.Min(p => p.V));
        var partMax = new P2(projected.Max(p => p.U), projected.Max(p => p.V));
        double frontDepth = corners.Max(basis.Depth);

        double gap = AutoDimensioner.GapFor(partMin, partMax);
        var (dimensions, centerLines) = AutoDimensioner.Build(entities, partMin, partMax, gap);

        return new DraftingSheet(basis, partMin, partMax, frontDepth, gap, entities, dimensions, centerLines, warnings);
    }
}

/// <summary>Como a etiqueta de uma cota se posiciona em relação ao seu ponto de ancoragem.</summary>
public enum LabelPlacement
{
    /// <summary>Centrada, logo acima da linha (cota horizontal).</summary>
    Above,

    /// <summary>Centrada, girada 90°, à esquerda da linha (cota vertical).</summary>
    LeftRotated,

    /// <summary>Começando no ponto, à direita (ponta do leader de um diâmetro).</summary>
    AfterPoint
}

/// <summary>Os traços de uma cota (linhas de chamada, linha de cota, setas, leader) e onde vai o texto.</summary>
public sealed record DimensionGraphics(IReadOnlyList<(P2 A, P2 B)> Segments, P2 LabelAnchor, LabelPlacement Placement)
{
    /// <summary>Monta os traços de <paramref name="dimension"/>; <paramref name="arrow"/> é o tamanho da seta em mm.</summary>
    public static DimensionGraphics Build(DraftDimension dimension, double arrow)
    {
        var segments = new List<(P2, P2)>();
        double clearance = arrow * 0.4;   // folga entre a peça e o início da linha de chamada
        double overshoot = arrow * 0.6;   // quanto a linha de chamada passa da linha de cota

        switch (dimension.Kind)
        {
            case DimensionKind.Horizontal:
            {
                double line = dimension.LinePosition;
                AddExtension(segments, dimension.From, new P2(dimension.From.U, line), clearance, overshoot);
                AddExtension(segments, dimension.To, new P2(dimension.To.U, line), clearance, overshoot);
                var a = new P2(dimension.From.U, line);
                var b = new P2(dimension.To.U, line);
                segments.Add((a, b));
                AddArrow(segments, a, b, arrow);
                AddArrow(segments, b, a, arrow);
                return new DimensionGraphics(segments, new P2((a.U + b.U) / 2, line), LabelPlacement.Above);
            }

            case DimensionKind.Vertical:
            {
                double line = dimension.LinePosition;
                AddExtension(segments, dimension.From, new P2(line, dimension.From.V), clearance, overshoot);
                AddExtension(segments, dimension.To, new P2(line, dimension.To.V), clearance, overshoot);
                var a = new P2(line, dimension.From.V);
                var b = new P2(line, dimension.To.V);
                segments.Add((a, b));
                AddArrow(segments, a, b, arrow);
                AddArrow(segments, b, a, arrow);
                return new DimensionGraphics(segments, new P2(line, (a.V + b.V) / 2), LabelPlacement.LeftRotated);
            }

            default:
            {
                // Diâmetro: linha atravessando o furo, setas para fora nas bordas e leader até o texto.
                var a = dimension.From;
                var b = dimension.To;
                segments.Add((a, b));
                AddArrow(segments, a, b, arrow);
                AddArrow(segments, b, a, arrow);
                var direction = (b - a) * (1.0 / Math.Max((b - a).Length, 1e-9));
                var elbow = b + direction * (arrow * 2.5);
                var end = elbow + new P2(arrow * 2, 0);
                segments.Add((b, elbow));
                segments.Add((elbow, end));
                return new DimensionGraphics(segments, end, LabelPlacement.AfterPoint);
            }
        }
    }

    private static void AddExtension(List<(P2, P2)> segments, P2 feature, P2 onLine, double clearance, double overshoot)
    {
        var delta = onLine - feature;
        double length = delta.Length;
        if (length < 1e-9) return;
        var unit = delta * (1.0 / length);
        var start = length > clearance * 2 ? feature + unit * clearance : feature;
        segments.Add((start, onLine + unit * overshoot));
    }

    /// <summary>Seta em <paramref name="tip"/> apontando para longe de <paramref name="from"/>.</summary>
    private static void AddArrow(List<(P2, P2)> segments, P2 tip, P2 from, double size)
    {
        var delta = from - tip;
        double length = delta.Length;
        if (length < 1e-9) return;
        var back = delta * (1.0 / length);
        var normal = new P2(-back.V, back.U);
        double s = Math.Min(size, length / 2.5);
        segments.Add((tip, tip + back * s + normal * (s * 0.35)));
        segments.Add((tip, tip + back * s - normal * (s * 0.35)));
    }
}
