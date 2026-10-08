using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AvaloniaCadEditor.Domain.ReferenceGuides;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.ReferenceGuides;

/// <summary>
/// Texto copiável de um <see cref="ReferenceGuideSet"/>: o que a análise gerou, legível por gente e
/// colável numa conversa junto do código da peça. Cada guia diz o que é (pelo tipo e pela cor na
/// tela), onde está (eixo = coordenada, pontas em coordenadas do mundo) e de quais sólidos veio.
/// </summary>
public static class ReferenceGuideReport
{
    private static readonly GuideKind[] Order =
        { GuideKind.Symmetry, GuideKind.HoleRow, GuideKind.Wall, GuideKind.ArcCenter, GuideKind.NearMiss };

    /// <summary>O relatório inteiro. <paramref name="highlightedId"/> é a feature selecionada: as guias
    /// dela (as que a tela mostra fortes) saem marcadas.</summary>
    public static string Format(ReferenceGuideSet set, bool isStale = false, Guid? highlightedId = null, string? highlightedName = null)
    {
        ArgumentNullException.ThrowIfNull(set);
        var sb = new StringBuilder();
        sb.AppendLine("# Guias de referência");
        sb.AppendLine(Inv($"# {set.Lines.Count} linhas, {set.Dimensions.Count} cotas · coordenadas do mundo (mm)"));
        if (isStale) sb.AppendLine("# ATENÇÃO: a cena mudou desde a análise (resultado desatualizado)");
        if (highlightedId != null) sb.AppendLine($"# selecionada: {highlightedName ?? highlightedId.ToString()} — guias dela marcadas com [destacada]");

        foreach (var kind in Order)
        {
            var lines = set.Lines.Where(l => l.Kind == kind).ToList();
            if (lines.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine(Inv($"## {Title(kind)} — {lines.Count}"));
            foreach (var line in lines.OrderBy(l => l.Axis).ThenBy(l => l.Coordinate))
            {
                AppendLine(sb, line, IsHighlighted(line.SourceFeatureIds, line.OwnerIds, highlightedId));
            }
        }

        if (set.Dimensions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Inv($"## Cotas das guias — {set.Dimensions.Count}"));
            foreach (var dimension in set.Dimensions)
            {
                AppendDimension(sb, dimension, IsHighlighted(dimension.SourceFeatureIds, dimension.OwnerIds, highlightedId));
            }
        }

        return sb.ToString();
    }

    /// <summary>Uma guia só (Alt+clique na linha).</summary>
    public static string FormatLine(GuideLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var sb = new StringBuilder();
        sb.AppendLine($"# Guia de referência — {Title(line.Kind)} · coordenadas do mundo (mm)");
        AppendLine(sb, line, highlighted: false);
        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, GuideLine line, bool highlighted)
    {
        var where = line.Kind == GuideKind.ArcCenter
            ? $"centro de arco em {P(Mid(line.Start, line.End))}"
            : line.Axis != null ? Inv($"{line.Axis} = {N(line.Coordinate)}") : Inv($"coordenada {N(line.Coordinate)}");
        if (line.Kind == GuideKind.Symmetry && line.Axis != null) where = $"plano ⟂ {line.Axis}: {where}";

        sb.Append("- ").Append(where);
        if (line.Note != null) sb.Append(" · ").Append(line.Note);
        if (highlighted) sb.Append(" [destacada]");
        sb.AppendLine();
        sb.AppendLine($"  linha: {P(line.Start)} → {P(line.End)}");
        AppendSources(sb, line.MemberNames, line.SourceNames, line.SourceFeatureIds.Count);
    }

    private static void AppendDimension(StringBuilder sb, GuideDimension dimension, bool highlighted)
    {
        sb.Append("- ").Append(DimensionTitle(dimension.Kind)).Append(' ').Append(dimension.Label);
        if (highlighted) sb.Append(" [destacada]");
        sb.AppendLine();
        sb.AppendLine($"  de {P(dimension.From)} a {P(dimension.To)}");
        AppendSources(sb, dimension.MemberNames, dimension.SourceNames, dimension.SourceFeatureIds.Count);
    }

    /// <summary>Os membros (componentes e furos coaxiais contam uma vez) ou, sem eles, os sólidos —
    /// agrupados pela peça (o trecho antes da primeira barra), para não repetir "trilho_mgn9_x/" vinte vezes.</summary>
    private static void AppendSources(StringBuilder sb, IReadOnlyList<string> members, IReadOnlyList<string> solids, int idCount)
    {
        var names = members.Count > 0 ? members : solids;
        var label = members.Count > 0 ? "membros" : "sólidos";
        if (names.Count == 0)
        {
            sb.AppendLine(Inv($"  sólidos: {idCount} (sem nome)"));
            return;
        }

        foreach (var group in names.GroupBy(Owner))
        {
            var local = group.Select(n => group.Key.Length == 0 ? n : n[(group.Key.Length + 1)..]).ToList();
            var owner = group.Key.Length == 0 ? "" : group.Key + ": ";
            sb.AppendLine(Inv($"  {label} ({local.Count}) {owner}{string.Join(", ", local)}"));
        }
    }

    private static string Owner(string name)
    {
        int slash = name.IndexOf('/');
        return slash < 0 ? "" : name[..slash];
    }

    private static bool IsHighlighted(IReadOnlyList<Guid> sources, IReadOnlyList<Guid>? owners, Guid? id) =>
        id != null && (sources.Contains(id.Value) || owners?.Contains(id.Value) == true);

    private static string Title(GuideKind kind) => kind switch
    {
        GuideKind.Symmetry => "Simetria (verde, traço-ponto)",
        GuideKind.HoleRow => "Eixo comum de furos (ciano)",
        GuideKind.Wall => "Parede alinhada (laranja)",
        GuideKind.ArcCenter => "Centro de arco (amarelo)",
        GuideKind.NearMiss => "Quase-alinhamento (vermelho)",
        _ => kind.ToString()
    };

    private static string DimensionTitle(GuideKind kind) => kind switch
    {
        GuideKind.HoleRow => "[passo entre fileiras]",
        GuideKind.Wall => "[fileira → parede]",
        GuideKind.NearMiss => "[conflito]",
        _ => $"[{kind}]"
    };

    private static Point3D Mid(Point3D a, Point3D b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);

    private static string P(Point3D p) => Inv($"({N(p.X)}, {N(p.Y)}, {N(p.Z)})");

    private static string N(double value) => (Math.Abs(value) < 5e-4 ? 0 : value).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Inv(FormattableString s) => FormattableString.Invariant(s);
}
