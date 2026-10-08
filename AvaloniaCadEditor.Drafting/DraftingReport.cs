using System.Globalization;
using System.Text;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Drafting;

/// <summary>
/// Texto copiável de uma folha de cotas: o que foi desenhado, legível e colável numa conversa junto
/// do código da peça. As coordenadas são as da folha (U para a direita, V para cima), e o cabeçalho diz
/// a que eixos da peça elas correspondem, para que se possa voltar delas ao código.
/// As cotas lineares saem agrupadas em cadeias: uma cadeia é uma fileira — mesmo nível, mesmo lado e
/// mesma posição de linha.
/// </summary>
public static class DraftingReport
{
    /// <param name="shown">As cotas que estão na tela (automáticas menos as escondidas, mais as manuais).</param>
    /// <param name="hints">Parâmetro do record por trás de uma cota (chave → "RaioFuroPassante").</param>
    /// <param name="codeOrigin">Onde fica o (0, 0, 0) do código da peça, no referencial da folha (que
    /// começa no canto da caixa): sem isso, quem lê não tem como voltar das cotas às coordenadas do código.</param>
    public static string Format(DraftingSheet sheet, IReadOnlyList<DraftDimension> shown, string partName, string? partType = null,
        IReadOnlyDictionary<string, string>? hints = null, Point3D? codeOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(shown);
        var sb = new StringBuilder();
        AppendHeader(sb, sheet, partName, partType, codeOrigin);

        sb.AppendLine();
        sb.AppendLine(Inv($"## Sólidos na vista — {sheet.Entities.Count}"));
        foreach (var entity in sheet.Entities) sb.AppendLine("- " + Describe(entity));

        var linear = shown.Where(IsLinear).ToList();
        var chains = Chains(linear).ToList();
        sb.AppendLine();
        sb.AppendLine(Inv($"## Cotas na tela — {shown.Count} ({chains.Count} cadeias)"));
        foreach (var level in chains.GroupBy(c => c[0].Level).OrderBy(g => g.Key))
        {
            sb.AppendLine(Inv($"### Nível {level.Key} — {DimensionLevels.Title(level.Key)}"));
            foreach (var chain in level) AppendChain(sb, chain, hints);
        }

        var others = shown.Where(d => !IsLinear(d)).ToList();
        if (others.Count > 0)
        {
            sb.AppendLine("### Diâmetros e chamadas");
            foreach (var d in others) sb.AppendLine("- " + Describe(d, hints));
        }

        if (sheet.Suppressed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Inv($"## Ocultas pela densidade — {sheet.Suppressed.Count}"));
            foreach (var s in sheet.Suppressed)
            {
                var reason = s.Reason == SuppressionReason.Rows ? "fileiras demais no lado" : "cruzaria outras cotas";
                sb.AppendLine(Inv($"- nível {s.Dimension.Level}, {Side(s.Dimension.Side)} · {Describe(s.Dimension, hints)} · motivo: {reason}"));
            }
        }

        if (sheet.CenterLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Inv($"## Linhas de centro / simetria — {sheet.CenterLines.Count}"));
            foreach (var line in sheet.CenterLines) sb.AppendLine(Inv($"- {DescribeLine(line)}"));
        }

        if (sheet.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Avisos");
            foreach (var w in sheet.Warnings) sb.AppendLine("- " + w);
        }

        return sb.ToString();
    }

    /// <summary>A cadeia (fileira) de <paramref name="dimension"/> e os sólidos que ela mede — o
    /// registro de um Alt+clique num texto de cota.</summary>
    public static string FormatChain(DraftingSheet sheet, IReadOnlyList<DraftDimension> shown, DraftDimension dimension, string partName,
        string? partType = null, IReadOnlyDictionary<string, string>? hints = null, Point3D? codeOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(dimension);
        var sb = new StringBuilder();
        AppendHeader(sb, sheet, partName, partType, codeOrigin);
        sb.AppendLine();

        List<DraftDimension> members;
        if (IsLinear(dimension))
        {
            var chain = Chains(shown.Where(IsLinear)).FirstOrDefault(c => c.Contains(dimension)) ?? new List<DraftDimension> { dimension };
            sb.AppendLine(Inv($"## Cadeia clicada — nível {dimension.Level} ({DimensionLevels.Title(dimension.Level)})"));
            AppendChain(sb, chain, hints, clicked: dimension);
            members = chain;
        }
        else
        {
            sb.AppendLine("## Cota clicada");
            sb.AppendLine("- " + Describe(dimension, hints));
            members = new List<DraftDimension> { dimension };
        }

        var names = members.SelectMany(d => d.SourceNames).ToHashSet();
        var entities = sheet.Entities.Where(e => names.Contains(e.SourceName)).ToList();
        if (entities.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Sólidos medidos");
            foreach (var entity in entities) sb.AppendLine("- " + Describe(entity));
        }

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, DraftingSheet sheet, string partName, string? partType, Point3D? codeOrigin)
    {
        var basis = sheet.Basis;
        sb.AppendLine($"# Desenho de cotas — {partName}{(partType != null && partType != partName ? $" ({partType})" : "")}, vista {basis.View}");
        sb.AppendLine($"# folha: U → direita = {Axis(basis.U)} da peça, V → cima = {Axis(basis.V)}, olhando de {Axis(basis.W)} · mm, referencial da peça");
        var size = sheet.PartMax - sheet.PartMin;
        sb.AppendLine(Inv($"# contorno: U {N(sheet.PartMin.U)}..{N(sheet.PartMax.U)}, V {N(sheet.PartMin.V)}..{N(sheet.PartMax.V)} ({N(size.U)} × {N(size.V)})"));
        if (codeOrigin is { } origin)
        {
            var o = basis.Project(origin);
            sb.AppendLine(Inv($"# origem do código (0, 0, 0) na folha: U {N(o.U)}, V {N(o.V)} — coordenada no código = folha − origem (já com o sinal de um espelhamento, se o código multiplicar por −1)"));
        }
    }

    private static void AppendChain(StringBuilder sb, List<DraftDimension> chain, IReadOnlyDictionary<string, string>? hints, DraftDimension? clicked = null)
    {
        var first = chain[0];
        var orientation = first.Kind == DimensionKind.Horizontal ? "horizontal" : "vertical";
        var position = first.Kind == DimensionKind.Horizontal ? Inv($"V = {N(first.LinePosition)}") : Inv($"U = {N(first.LinePosition)}");
        var values = string.Join(" + ", chain.Select(d => N(d.Value)));
        var total = chain.Count > 1 ? Inv($" = {N(chain.Sum(d => d.Value))}") : "";
        sb.AppendLine(Inv($"- cadeia {orientation}, {Side(first.Side)}, linha em {position}: {values}{total}"));
        foreach (var d in chain)
        {
            sb.AppendLine("    " + (ReferenceEquals(d, clicked) ? "▶ " : "· ") + Describe(d, hints));
        }
    }

    /// <summary>Fileiras: mesmo tipo, nível, lado e posição de linha; cada uma em ordem ao longo da linha.</summary>
    private static IEnumerable<List<DraftDimension>> Chains(IEnumerable<DraftDimension> linear) =>
        linear
            .GroupBy(d => (d.Kind, d.Level, d.Side, Line: Math.Round(d.LinePosition, 3)))
            .OrderBy(g => g.Key.Level).ThenBy(g => g.Key.Side).ThenBy(g => g.Key.Kind).ThenBy(g => Math.Abs(g.Key.Line))
            .Select(g => g.OrderBy(d => Start(d)).ToList());

    private static bool IsLinear(DraftDimension d) => d.Kind is DimensionKind.Horizontal or DimensionKind.Vertical;

    private static double Start(DraftDimension d) =>
        d.Kind == DimensionKind.Horizontal ? Math.Min(d.From.U, d.To.U) : Math.Min(d.From.V, d.To.V);

    private static string Describe(DraftDimension d, IReadOnlyDictionary<string, string>? hints)
    {
        var sb = new StringBuilder();
        switch (d.Kind)
        {
            case DimensionKind.Horizontal:
                sb.Append(Inv($"{d.Text}  (U {N(d.From.U)} → {N(d.To.U)})"));
                break;
            case DimensionKind.Vertical:
                sb.Append(Inv($"{d.Text}  (V {N(d.From.V)} → {N(d.To.V)})"));
                break;
            case DimensionKind.Diameter:
                sb.Append(Inv($"{d.Text}  centro {P(new P2((d.From.U + d.To.U) / 2, (d.From.V + d.To.V) / 2))}"));
                break;
            default:
                sb.Append(Inv($"chamada \"{d.Text}\" em {P(d.From)}"));
                break;
        }

        if (hints != null && hints.TryGetValue(d.Key, out var hint)) sb.Append(" · parâmetro ").Append(hint);
        if (d.IsManual) sb.Append(" · manual");
        sb.Append(" · mede: ").Append(d.SourceNames.Count > 0 ? string.Join(", ", d.SourceNames) : "contorno da peça");
        if (d.AlignedWith.Count > 0) sb.Append(" · mesmo eixo: ").Append(string.Join(", ", d.AlignedWith));
        return sb.ToString();
    }

    private static string Describe(ViewEntity entity)
    {
        var role = entity.Role == PrimitiveRole.Void ? "vazio" : "material";
        var shape = entity switch
        {
            ViewCircle c => Inv($"círculo Ø{N(c.Radius * 2)} centro {P(c.Center)}"),
            ViewRect r => Inv($"retângulo {P(r.MinCorner)}–{P(r.MaxCorner)} ({N(r.MaxCorner.U - r.MinCorner.U)} × {N(r.MaxCorner.V - r.MinCorner.V)})")
                + (r.Axis == CenterAxis.None ? "" : $", cilindro de lado (eixo {(r.Axis == CenterAxis.Horizontal ? "horizontal" : "vertical")})"),
            ViewPolygon p => Inv($"polígono de {p.Points.Count} vértices, caixa {P(p.Min)}–{P(p.Max)}"),
            _ => entity.GetType().Name
        };
        var component = entity.Fact?.Component is { } c2 ? $" · componente {c2.Name} ({c2.Label})" : "";
        return $"{entity.SourceName} · {role} · {shape}{component}";
    }

    private static string DescribeLine(CenterLine line)
    {
        if (Math.Abs(line.A.U - line.B.U) < 1e-6) return Inv($"vertical em U = {N(line.A.U)} (V {N(line.A.V)} → {N(line.B.V)})");
        if (Math.Abs(line.A.V - line.B.V) < 1e-6) return Inv($"horizontal em V = {N(line.A.V)} (U {N(line.A.U)} → {N(line.B.U)})");
        return $"{P(line.A)} → {P(line.B)}";
    }

    private static string Side(DimensionSide side) => side switch
    {
        DimensionSide.Below => "abaixo",
        DimensionSide.Above => "acima",
        DimensionSide.Left => "à esquerda",
        DimensionSide.Right => "à direita",
        _ => "sem lado"
    };

    /// <summary>"+X", "-Z"… para um vetor de base alinhado a um eixo.</summary>
    private static string Axis(Vector3D v)
    {
        if (Math.Abs(v.X) > 0.999) return v.X > 0 ? "+X" : "-X";
        if (Math.Abs(v.Y) > 0.999) return v.Y > 0 ? "+Y" : "-Y";
        if (Math.Abs(v.Z) > 0.999) return v.Z > 0 ? "+Z" : "-Z";
        return Inv($"({N(v.X)}, {N(v.Y)}, {N(v.Z)})");
    }

    private static string P(P2 p) => Inv($"({N(p.U)}, {N(p.V)})");

    private static string N(double value) => (Math.Abs(value) < 5e-4 ? 0 : value).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Inv(FormattableString s) => FormattableString.Invariant(s);
}
