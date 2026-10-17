using System.Text;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using static AvaloniaCadEditor.Harness.Views.TextFormat;

namespace AvaloniaCadEditor.Harness.Views;

/// <summary>De quem é a vista e como voltar dela ao código.</summary>
/// <param name="FrameIsWorld">A peça não tem referencial próprio: as coordenadas são as do mundo.</param>
/// <param name="CodeOrigin">O (0, 0, 0) do código no referencial da vista, quando se sabe.</param>
public sealed record WireframeHeader(string Name, string? PartType, bool FrameIsWorld, Point3D? CodeOrigin);

/// <summary>
/// Texto da vista em arame para a LLM: o que a tela mostraria em ortográfica, como linhas com
/// coordenadas 2D. Mesmo referencial e cabeçalho do texto das cotas (<see cref="DraftingReport"/>),
/// para que vista e cotas falem das mesmas coordenadas. Saída determinística.
/// </summary>
public static class WireframeReport
{
    /// <summary>Quanto um círculo da malha pode desviar do furo exato para receber o nome e o Ø dele.</summary>
    private const double CircleMatchFraction = 0.05;

    private sealed record Entry(bool Visible, double Length, bool IsCircle, string Body);

    /// <param name="knownCircles">Círculos exatos da análise paramétrica (<see cref="ViewProjector"/>), com nome.</param>
    /// <param name="maxChars">Orçamento do texto: as linhas menores saem primeiro (as ocultas antes das visíveis).</param>
    public static string Format(HiddenLineResult result, ProjectionBasis basis, WireframeHeader header,
        IReadOnlyList<ViewCircle> knownCircles, bool includeHidden = true, int maxChars = 6000)
    {
        var head = new StringBuilder();
        AppendHeader(head, result, basis, header, includeHidden);
        if (result.Error != null)
        {
            head.AppendLine("## Erro");
            head.AppendLine("- " + result.Error);
            return head.ToString();
        }

        var entries = result.Lines
            .Where(l => includeHidden || l.Visible)
            .Select(l => Describe(l, knownCircles))
            .OrderBy(e => e.Visible ? 0 : 1)
            .ThenByDescending(e => Math.Round(e.Length, 3))
            .ThenBy(e => e.Body, StringComparer.Ordinal)
            .ToList();

        // Corta pelo orçamento: as ocultas menores primeiro, depois as visíveis menores. A lista
        // está ordenada (visíveis, depois ocultas; cada grupo do maior para o menor). Primeiro uma
        // estimativa rápida; depois o texto de verdade, cortando até caber.
        var kept = new List<Entry>(entries);
        var omitted = new List<Entry>();
        void DropOne()
        {
            var drop = kept.LastOrDefault(e => !e.Visible) ?? kept[^1];
            kept.Remove(drop);
            omitted.Add(drop);
        }

        int total = head.Length + SectionsCost + kept.Sum(Cost);
        while (kept.Count > 0 && total > maxChars)
        {
            total -= Cost(kept.LastOrDefault(e => !e.Visible) ?? kept[^1]);
            if (omitted.Count == 0) total += OmittedLineCost;
            DropOne();
        }

        string text = Render(head.ToString(), kept, omitted, includeHidden, maxChars);
        while (kept.Count > 0 && text.Length > maxChars)
        {
            DropOne();
            text = Render(head.ToString(), kept, omitted, includeHidden, maxChars);
        }
        return text;
    }

    /// <summary>Estimativa dos títulos das seções e da linha de omitidas, para o corte rápido.</summary>
    private const int SectionsCost = 90;
    private const int OmittedLineCost = 130;

    private static string Render(string head, List<Entry> kept, List<Entry> omitted, bool includeHidden, int maxChars)
    {
        var sb = new StringBuilder(head);
        var visible = kept.Where(e => e.Visible).ToList();
        sb.AppendLine();
        sb.AppendLine($"## Visíveis — {visible.Count} linhas ({visible.Count(e => e.IsCircle)} círculos)");
        AppendEntries(sb, visible, "L", "C");

        if (includeHidden)
        {
            var hidden = kept.Where(e => !e.Visible).ToList();
            sb.AppendLine();
            sb.AppendLine($"## Ocultas (tracejadas) — {hidden.Count} linhas");
            AppendEntries(sb, hidden, "H", "HC");
        }

        if (omitted.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Omitidas pelo orçamento de {maxChars} caracteres — {omitted.Count} linhas " +
                $"({omitted.Count(e => e.Visible)} visíveis, {omitted.Count(e => !e.Visible)} ocultas; a maior com {N(omitted.Max(e => e.Length))} mm)");
        }

        return sb.ToString();
    }

    /// <summary>Caracteres de uma linha no texto: o corpo mais "- L12 " e a quebra.</summary>
    private static int Cost(Entry e) => e.Body.Length + 8;

    private static void AppendHeader(StringBuilder sb, HiddenLineResult result, ProjectionBasis basis, WireframeHeader header, bool includeHidden)
    {
        string type = header.PartType != null && header.PartType != header.Name ? $" ({header.PartType})" : "";
        sb.AppendLine($"# Vista {basis.View} — {header.Name}{type} · ortográfica, arame{(includeHidden ? "" : ", sem as ocultas")}");
        string frame = header.FrameIsWorld
            ? "referencial do mundo (a peça não tem referencial próprio)"
            : "referencial da peça";
        sb.AppendLine($"# folha: U → direita = {Axis(basis.U)}, V → cima = {Axis(basis.V)}, olhando de {Axis(basis.W)} · mm, {frame}");
        var b = result.Bounds;
        sb.AppendLine($"# contorno: U {N(b.MinU)}..{N(b.MaxU)}, V {N(b.MinV)}..{N(b.MaxV)} ({N(b.MaxU - b.MinU)} × {N(b.MaxV - b.MinV)}); profundidade W {N(b.MinW)}..{N(b.MaxW)} (maior = mais perto)");
        if (header.CodeOrigin is { } origin)
        {
            var o = basis.Project(origin);
            sb.AppendLine($"# origem do código (0, 0, 0) na folha: U {N(o.U)}, V {N(o.V)} — coordenada no código = folha − origem");
        }
    }

    private static void AppendEntries(StringBuilder sb, List<Entry> entries, string linePrefix, string circlePrefix)
    {
        int lines = 0, circles = 0;
        foreach (var e in entries)
        {
            string id = e.IsCircle ? $"{circlePrefix}{++circles}" : $"{linePrefix}{++lines}";
            sb.AppendLine($"- {id} {e.Body}");
        }
    }

    private static Entry Describe(ViewLine line, IReadOnlyList<ViewCircle> knownCircles)
    {
        var shape = line.Shape;
        if (shape.IsCircle)
        {
            var center = new P2(shape.Center!.Value.X, shape.Center.Value.Y);
            double r = shape.Radius!.Value;
            var known = knownCircles
                .Where(c => c.Center.DistanceTo(center) < CircleMatchFraction * c.Radius + 1e-3 && Math.Abs(c.Radius - r) < CircleMatchFraction * c.Radius + 1e-3)
                .OrderBy(c => c.Center.DistanceTo(center))
                .FirstOrDefault();

            string body = known != null
                ? $"Ø{N(2 * known.Radius)} centro ({N(known.Center.U)}, {N(known.Center.V)}) [{known.SourceName}]"
                : $"Ø{N(2 * r)} centro ({N(center.U)}, {N(center.V)}) (ajustado na malha)";
            return new Entry(line.Visible, 2 * Math.PI * r, true, body);
        }

        var points = shape.Points;
        double length = 0;
        for (int i = 1; i < points.Count; i++) length += points[i].DistanceTo(points[i - 1]);
        if (shape.Closed && points.Count > 2) length += points[^1].DistanceTo(points[0]);

        string kind = line.Kind switch
        {
            ProjectedEdgeKind.Silhouette => "silhueta",
            ProjectedEdgeKind.Border => "borda",
            _ => "quina"
        };
        string text = points.Count == 2 && !shape.Closed
            ? $"{kind}: {P(points[0])} → {P(points[1])}"
            : $"{kind}{(shape.Closed ? " fechada" : "")}: {string.Join(" ", points.Select(P))}";
        return new Entry(line.Visible, length, false, text);
    }

    private static string P(Point3D p) => $"({N(p.X)}, {N(p.Y)})";
}
