using System.Globalization;

namespace AvaloniaCadEditor.Drafting;

/// <summary>
/// Regras de cota automática de uma vista (v1):
/// <list type="bullet">
/// <item>Posições por <b>cota de referência</b> a partir do canto inferior esquerdo da peça na
/// vista: centros de furo, linhas de centro de cilindros vistos de lado e bordas de vazios que
/// caem dentro da peça. A largura e a altura totais são a última fileira.</item>
/// <item>Diâmetros: furos vistos de frente agrupados por diâmetro ("2× Ø3,4"); furos vistos de
/// lado cotados pela largura, acima ou à direita da peça.</item>
/// <item>Simetria: se todos os vazios têm par espelhado no meio da peça, só a primeira metade é
/// cotada e a linha de simetria é desenhada.</item>
/// </list>
/// As cotas ficam fora do contorno, em fileiras empilhadas (as menores mais perto da peça).
/// </summary>
public static class AutoDimensioner
{
    private const double Tolerance = 1e-3;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Formata um valor em mm: até duas casas, vírgula decimal ("3,4", "20,62").</summary>
    public static string Format(double value) => Math.Round(value, 2).ToString("0.##", PtBr);

    /// <summary>Espaçamento entre fileiras de cota, proporcional ao tamanho da peça.</summary>
    public static double GapFor(P2 partMin, P2 partMax) =>
        Math.Clamp(Math.Max(partMax.U - partMin.U, partMax.V - partMin.V) * 0.08, 1.5, 15.0);

    public static (IReadOnlyList<DraftDimension> Dimensions, IReadOnlyList<CenterLine> CenterLines) Build(
        IReadOnlyList<ViewEntity> entities, P2 partMin, P2 partMax, double gap)
    {
        var voids = entities.Where(e => e.Role == EntityRole.Void).ToList();
        double midU = (partMin.U + partMax.U) / 2.0;
        double midV = (partMin.V + partMax.V) / 2.0;
        bool symmetricU = voids.Count > 0 && voids.All(e => voids.Any(p => MirroredInU(e, p, midU)));
        bool symmetricV = voids.Count > 0 && voids.All(e => voids.Any(p => MirroredInV(e, p, midV)));

        // Lado mantido de cada eixo: com simetria, só a primeira metade. O meio fica de fora: ele
        // já é dado pela linha de simetria (e, numa peça espelhada, é só a emenda das duas metades).
        bool KeepU(double u) => !symmetricU || u < midU - Tolerance;
        bool KeepV(double v) => !symmetricV || v < midV - Tolerance;
        bool KeepCenterU(double u) => !symmetricU || u <= midU + Tolerance;
        bool KeepCenterV(double v) => !symmetricV || v <= midV + Tolerance;

        var uCoords = new List<Coordinate>();
        var vCoords = new List<Coordinate>();
        foreach (var entity in voids)
        {
            CollectCoordinates(entity, partMin, partMax, uCoords, vCoords);
        }

        uCoords.Add(new Coordinate(partMax.U, new P2(partMax.U, partMin.V), "peca"));
        vCoords.Add(new Coordinate(partMax.V, new P2(partMin.U, partMax.V), "peca"));

        var dimensions = new List<DraftDimension>();
        var datum = partMin;

        var horizontal = Merge(uCoords.Where(c => c.Value > partMin.U + Tolerance && (KeepU(c.Value) || c.Source == "peca")), byU: true);
        for (int i = 0; i < horizontal.Count; i++)
        {
            var c = horizontal[i];
            double value = c.Value - partMin.U;
            dimensions.Add(new DraftDimension(
                $"h:{c.Value:F3}", DimensionKind.Horizontal, datum, c.Point,
                partMin.V - gap * (i + 1), value, Format(value), c.Sources));
        }

        var vertical = Merge(vCoords.Where(c => c.Value > partMin.V + Tolerance && (KeepV(c.Value) || c.Source == "peca")), byU: false);
        for (int i = 0; i < vertical.Count; i++)
        {
            var c = vertical[i];
            double value = c.Value - partMin.V;
            dimensions.Add(new DraftDimension(
                $"v:{c.Value:F3}", DimensionKind.Vertical, datum, c.Point,
                partMin.U - gap * (i + 1), value, Format(value), c.Sources));
        }

        AddFrontDiameters(voids.OfType<ViewCircle>().ToList(), KeepCenterU, KeepCenterV, dimensions);
        int rowAbove = AddSideDiameters(voids.OfType<ViewRect>().Where(r => r.Axis != CenterAxis.None).ToList(), partMin, partMax, gap, KeepCenterU, KeepCenterV, dimensions);
        AddPolygonWidths(voids.OfType<ViewPolygon>().ToList(), partMax, gap, rowAbove, KeepCenterU, dimensions);

        var centerLines = new List<CenterLine>();
        double extra = gap * 0.25;
        if (symmetricU)
        {
            centerLines.Add(new CenterLine(new P2(midU, partMin.V - gap * 0.5), new P2(midU, partMax.V + gap * 0.5)));
        }
        if (symmetricV)
        {
            centerLines.Add(new CenterLine(new P2(partMin.U - gap * 0.5, midV), new P2(partMax.U + gap * 0.5, midV)));
        }
        foreach (var circle in voids.OfType<ViewCircle>())
        {
            var c = circle.Center;
            double r = circle.Radius + extra;
            centerLines.Add(new CenterLine(new P2(c.U - r, c.V), new P2(c.U + r, c.V)));
            centerLines.Add(new CenterLine(new P2(c.U, c.V - r), new P2(c.U, c.V + r)));
        }
        foreach (var rect in entities.OfType<ViewRect>().Where(r => r.Axis != CenterAxis.None))
        {
            var c = rect.Center;
            centerLines.Add(rect.Axis == CenterAxis.Horizontal
                ? new CenterLine(new P2(rect.Min.U - extra, c.V), new P2(rect.Max.U + extra, c.V))
                : new CenterLine(new P2(c.U, rect.Min.V - extra), new P2(c.U, rect.Max.V + extra)));
        }

        return (dimensions, centerLines);
    }

    /// <summary>Cota manual entre dois pontos, com a linha de cota na posição escolhida.</summary>
    public static DraftDimension Linear(DraftAnchor a, DraftAnchor b, DimensionKind kind, double linePosition)
    {
        double value = kind == DimensionKind.Horizontal
            ? Math.Abs(b.Point.U - a.Point.U)
            : Math.Abs(b.Point.V - a.Point.V);
        return new DraftDimension($"m:{a.Id}|{b.Id}|{kind}", kind, a.Point, b.Point, linePosition, value, Format(value),
            SourcesOf(a, b), IsManual: true);
    }

    /// <summary>Cota manual de diâmetro de um furo visto de frente.</summary>
    public static DraftDimension Diameter(ViewCircle circle) =>
        DiameterOf(circle, $"m:{circle.SourceName}|Ø", $"Ø{Format(circle.Radius * 2)}", [circle.SourceName], isManual: true);

    private static IReadOnlyList<string> SourcesOf(params DraftAnchor[] anchors) =>
        anchors.Select(a => a.Id.Split('.')[0]).Where(s => s != "peca").Distinct().ToList();

    private static void CollectCoordinates(ViewEntity entity, P2 partMin, P2 partMax, List<Coordinate> uCoords, List<Coordinate> vCoords)
    {
        bool InsideU(double u) => u > partMin.U + Tolerance && u < partMax.U - Tolerance;
        bool InsideV(double v) => v > partMin.V + Tolerance && v < partMax.V - Tolerance;
        double ClampU(double u) => Math.Clamp(u, partMin.U, partMax.U);
        double ClampV(double v) => Math.Clamp(v, partMin.V, partMax.V);
        string name = entity.SourceName;

        switch (entity)
        {
            case ViewCircle circle:
                uCoords.Add(new Coordinate(circle.Center.U, circle.Center, name));
                vCoords.Add(new Coordinate(circle.Center.V, circle.Center, name));
                return;

            case ViewRect { Axis: CenterAxis.Horizontal } rect:
                vCoords.Add(new Coordinate(rect.Center.V, new P2(ClampU(rect.Min.U), rect.Center.V), name));
                if (InsideU(rect.Min.U)) uCoords.Add(new Coordinate(rect.Min.U, new P2(rect.Min.U, rect.Center.V), name));
                if (InsideU(rect.Max.U)) uCoords.Add(new Coordinate(rect.Max.U, new P2(rect.Max.U, rect.Center.V), name));
                return;

            case ViewRect { Axis: CenterAxis.Vertical } rect:
                uCoords.Add(new Coordinate(rect.Center.U, new P2(rect.Center.U, ClampV(rect.Min.V)), name));
                if (InsideV(rect.Min.V)) vCoords.Add(new Coordinate(rect.Min.V, new P2(rect.Center.U, rect.Min.V), name));
                if (InsideV(rect.Max.V)) vCoords.Add(new Coordinate(rect.Max.V, new P2(rect.Center.U, rect.Max.V), name));
                return;

            case ViewPolygon polygon:
                // Prisma visto pelo eixo (ex.: alojamento de porca): posição pelo centro em U e pelas
                // bordas em V que caem dentro da peça; a largura vai numa cota própria.
                double centerU = (polygon.Min.U + polygon.Max.U) / 2.0;
                uCoords.Add(new Coordinate(centerU, new P2(centerU, ClampV(polygon.Min.V)), name));
                if (InsideV(polygon.Min.V)) vCoords.Add(new Coordinate(polygon.Min.V, new P2(ClampU(polygon.Min.U), polygon.Min.V), name));
                if (InsideV(polygon.Max.V)) vCoords.Add(new Coordinate(polygon.Max.V, new P2(ClampU(polygon.Min.U), polygon.Max.V), name));
                return;

            default:
                // Caixa ou prisma: só as bordas que caem dentro da peça (as outras são abertas/passantes).
                var lowV = ClampV(entity.Min.V);
                var lowU = ClampU(entity.Min.U);
                if (InsideU(entity.Min.U)) uCoords.Add(new Coordinate(entity.Min.U, new P2(entity.Min.U, lowV), name));
                if (InsideU(entity.Max.U)) uCoords.Add(new Coordinate(entity.Max.U, new P2(entity.Max.U, lowV), name));
                if (InsideV(entity.Min.V)) vCoords.Add(new Coordinate(entity.Min.V, new P2(lowU, entity.Min.V), name));
                if (InsideV(entity.Max.V)) vCoords.Add(new Coordinate(entity.Max.V, new P2(lowU, entity.Max.V), name));
                return;
        }
    }

    /// <summary>Junta coordenadas iguais (mesma fileira) e ordena da menor para a maior. O ponto de
    /// partida da linha de chamada é o mais próximo da linha de cota.</summary>
    private static List<MergedCoordinate> Merge(IEnumerable<Coordinate> coordinates, bool byU)
    {
        var merged = new List<MergedCoordinate>();
        foreach (var c in coordinates.OrderBy(c => c.Value))
        {
            var last = merged.Count > 0 ? merged[^1] : null;
            if (last != null && Math.Abs(last.Value - c.Value) < Tolerance)
            {
                if (!last.Sources.Contains(c.Source)) last.Sources.Add(c.Source);
                bool closer = byU ? c.Point.V < last.Point.V : c.Point.U < last.Point.U;
                if (closer) last.Point = c.Point;
                continue;
            }

            merged.Add(new MergedCoordinate(c.Value, c.Point, [c.Source]));
        }

        foreach (var m in merged) m.Sources.Remove("peca");
        return merged;
    }

    private static void AddFrontDiameters(List<ViewCircle> circles, Func<double, bool> keepU, Func<double, bool> keepV, List<DraftDimension> dimensions)
    {
        foreach (var group in circles.GroupBy(c => Math.Round(c.Radius / Tolerance)))
        {
            var members = group.ToList();
            var representative = members
                .Where(c => keepU(c.Center.U) && keepV(c.Center.V))
                .OrderBy(c => c.Center.U).ThenBy(c => c.Center.V)
                .FirstOrDefault() ?? members[0];
            string text = CountPrefix(members) + $"Ø{Format(representative.Radius * 2)}";
            dimensions.Add(DiameterOf(representative, $"d:{representative.SourceName}", text, members.Select(c => c.SourceName).ToList(), isManual: false));
        }
    }

    private static DraftDimension DiameterOf(ViewCircle circle, string key, string text, IReadOnlyList<string> sources, bool isManual)
    {
        var direction = new P2(Math.Sqrt(0.5), Math.Sqrt(0.5));
        return new DraftDimension(key, DimensionKind.Diameter,
            circle.Center - direction * circle.Radius, circle.Center + direction * circle.Radius,
            0, circle.Radius * 2, text, sources, isManual);
    }

    /// <summary>"3× " quando há mais de uma posição distinta; entidades sobrepostas na vista (ex.: furos
    /// espelhados vistos de lado) contam uma vez só.</summary>
    private static string CountPrefix(IEnumerable<ViewEntity> members)
    {
        int distinct = members
            .Select(e => (Math.Round(e.Min.U / Tolerance), Math.Round(e.Min.V / Tolerance), Math.Round(e.Max.U / Tolerance), Math.Round(e.Max.V / Tolerance)))
            .Distinct()
            .Count();
        return distinct > 1 ? $"{distinct}× " : "";
    }

    /// <summary>Largura dos prismas vistos pelo eixo (ex.: chave da porca), acima da peça, depois dos diâmetros.</summary>
    private static void AddPolygonWidths(List<ViewPolygon> polygons, P2 partMax, double gap, int row,
        Func<double, bool> keepU, List<DraftDimension> dimensions)
    {
        foreach (var group in polygons.GroupBy(p => Math.Round((p.Max.U - p.Min.U) / Tolerance)))
        {
            var members = group.ToList();
            var rep = members.Where(p => keepU((p.Min.U + p.Max.U) / 2)).OrderBy(p => p.Min.U).FirstOrDefault() ?? members[0];
            double top = Math.Min(rep.Max.V, partMax.V);
            double value = rep.Max.U - rep.Min.U;
            dimensions.Add(new DraftDimension($"pw:{rep.SourceName}", DimensionKind.Horizontal,
                new P2(rep.Min.U, top), new P2(rep.Max.U, top), partMax.V + gap * (++row), value,
                CountPrefix(members) + Format(value), members.Select(p => p.SourceName).ToList()));
        }
    }

    /// <summary>Devolve quantas fileiras acima da peça foram usadas.</summary>
    private static int AddSideDiameters(List<ViewRect> rects, P2 partMin, P2 partMax, double gap,
        Func<double, bool> keepU, Func<double, bool> keepV, List<DraftDimension> dimensions)
    {
        // Eixo vertical: diâmetro na largura (U), cotado acima da peça.
        int row = 0;
        var verticalAxis = rects.Where(r => r.Axis == CenterAxis.Vertical)
            .GroupBy(r => Math.Round((r.Max.U - r.Min.U) / Tolerance));
        foreach (var group in verticalAxis)
        {
            var members = group.ToList();
            var rep = members.Where(r => keepU(r.Center.U)).OrderBy(r => r.Center.U).FirstOrDefault() ?? members[0];
            double top = Math.Min(rep.Max.V, partMax.V);
            double value = rep.Max.U - rep.Min.U;
            string text = CountPrefix(members) + $"Ø{Format(value)}";
            dimensions.Add(new DraftDimension($"ds:{rep.SourceName}", DimensionKind.Horizontal,
                new P2(rep.Min.U, top), new P2(rep.Max.U, top), partMax.V + gap * (++row), value, text,
                members.Select(r => r.SourceName).ToList()));
        }

        int rowsAbove = row;

        // Eixo horizontal: diâmetro na altura (V), cotado à direita da peça.
        row = 0;
        var horizontalAxis = rects.Where(r => r.Axis == CenterAxis.Horizontal)
            .GroupBy(r => Math.Round((r.Max.V - r.Min.V) / Tolerance));
        foreach (var group in horizontalAxis)
        {
            var members = group.ToList();
            var rep = members.Where(r => keepV(r.Center.V)).OrderBy(r => r.Center.V).FirstOrDefault() ?? members[0];
            double right = Math.Min(rep.Max.U, partMax.U);
            double value = rep.Max.V - rep.Min.V;
            string text = CountPrefix(members) + $"Ø{Format(value)}";
            dimensions.Add(new DraftDimension($"ds:{rep.SourceName}", DimensionKind.Vertical,
                new P2(right, rep.Min.V), new P2(right, rep.Max.V), partMax.U + gap * (++row), value, text,
                members.Select(r => r.SourceName).ToList()));
        }

        return rowsAbove;
    }

    private static bool MirroredInU(ViewEntity e, ViewEntity p, double mid) =>
        e.GetType() == p.GetType() &&
        Math.Abs(p.Min.U - (2 * mid - e.Max.U)) < Tolerance &&
        Math.Abs(p.Max.U - (2 * mid - e.Min.U)) < Tolerance &&
        Math.Abs(p.Min.V - e.Min.V) < Tolerance &&
        Math.Abs(p.Max.V - e.Max.V) < Tolerance;

    private static bool MirroredInV(ViewEntity e, ViewEntity p, double mid) =>
        e.GetType() == p.GetType() &&
        Math.Abs(p.Min.V - (2 * mid - e.Max.V)) < Tolerance &&
        Math.Abs(p.Max.V - (2 * mid - e.Min.V)) < Tolerance &&
        Math.Abs(p.Min.U - e.Min.U) < Tolerance &&
        Math.Abs(p.Max.U - e.Max.U) < Tolerance;

    private sealed record Coordinate(double Value, P2 Point, string Source);

    private sealed class MergedCoordinate(double value, P2 point, List<string> sources)
    {
        public double Value { get; } = value;
        public P2 Point { get; set; } = point;
        public List<string> Sources { get; } = sources;
    }
}
