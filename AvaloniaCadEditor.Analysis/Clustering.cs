namespace AvaloniaCadEditor.Analysis;

/// <summary>O que uma coordenada candidata representa.</summary>
public enum ClusterMemberKind
{
    /// <summary>Centro de um furo (vazio redondo) — a coordenada é a do eixo.</summary>
    HoleCenter,

    /// <summary>Uma aresta reta de seção alinhada ao eixo.</summary>
    Edge,

    /// <summary>Centro de um arco detectado num perfil, ou de um cilindro de material.</summary>
    ArcCenter
}

/// <summary>Uma coordenada a agrupar. <see cref="Cross"/> é a outra coordenada no plano (para o
/// alcance de uma linha) e <see cref="Top"/> a profundidade ao longo do eixo do grupo.</summary>
public sealed record AxisCandidate(double Coordinate, double Cross, double Top, PrimitiveFact Fact, ClusterMemberKind Kind);

/// <summary>Coordenadas iguais (dentro de <see cref="AnalysisOptions.AlignTolerance"/>) ao longo de um eixo.</summary>
public sealed class Cluster
{
    public required double Coordinate { get; init; }
    public required double CrossMin { get; init; }
    public required double CrossMax { get; init; }
    public required double Top { get; init; }
    public required IReadOnlyList<AxisCandidate> Members { get; init; }

    /// <summary>Furos (fatos distintos) neste alinhamento.</summary>
    public IReadOnlyList<PrimitiveFact> Holes =>
        Members.Where(m => m.Kind == ClusterMemberKind.HoleCenter).Select(m => m.Fact).Distinct().ToList();

    /// <summary>Paredes (arestas e centros de arco, fatos distintos) neste alinhamento.</summary>
    public IReadOnlyList<PrimitiveFact> Walls =>
        Members.Where(m => m.Kind != ClusterMemberKind.HoleCenter).Select(m => m.Fact).Distinct().ToList();

    public IReadOnlyList<PrimitiveFact> Facts => Members.Select(m => m.Fact).Distinct().ToList();

    /// <summary>Quantos candidatos sustentam a coordenada (vértices, extremos de aresta, centros) —
    /// usado para mostrar o apoio de cada lado num quase-alinhamento, nunca para decidir qual está certo.</summary>
    public int MemberCount => Members.Count;
}

/// <summary>Resultado do agrupamento de um eixo.</summary>
/// <param name="Tight">Alinhamentos de verdade.</param>
/// <param name="NearMisses">Vizinhanças que se partiram em mais de um alinhamento: valores perto o bastante
/// para parecerem intencionais, mas que não são iguais.</param>
public sealed record AxisClusters(IReadOnlyList<Cluster> Tight, IReadOnlyList<IReadOnlyList<Cluster>> NearMisses)
{
    public static readonly AxisClusters Empty = new(Array.Empty<Cluster>(), Array.Empty<IReadOnlyList<Cluster>>());
}

public static class Clustering
{
    /// <summary>
    /// Agrupamento em dois níveis: primeiro junta valores a menos de
    /// <see cref="AnalysisOptions.NearMissTolerance"/> numa "vizinhança", depois parte cada vizinhança
    /// em alinhamentos com a tolerância bem mais estrita <see cref="AnalysisOptions.AlignTolerance"/>.
    /// Uma vizinhança que se parte em mais de um alinhamento é um quase-alinhamento.
    /// </summary>
    public static AxisClusters Cluster(IEnumerable<AxisCandidate> candidates, AnalysisOptions options)
    {
        var sorted = candidates.OrderBy(c => c.Coordinate).ToList();
        if (sorted.Count == 0) return AxisClusters.Empty;

        var tight = new List<Cluster>();
        var nearMisses = new List<IReadOnlyList<Cluster>>();

        foreach (var neighborhood in SplitRuns(sorted, options.NearMissTolerance))
        {
            var clusters = SplitRuns(neighborhood, options.AlignTolerance).Select(Build).ToList();
            tight.AddRange(clusters);
            if (clusters.Count > 1) nearMisses.Add(clusters);
        }

        return new AxisClusters(tight, nearMisses);
    }

    /// <summary>Partes de uma lista ordenada onde vizinhos consecutivos distam no máximo <paramref name="gap"/>.</summary>
    private static List<List<AxisCandidate>> SplitRuns(List<AxisCandidate> sorted, double gap)
    {
        var runs = new List<List<AxisCandidate>>();
        var current = new List<AxisCandidate> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Coordinate - sorted[i - 1].Coordinate <= gap)
            {
                current.Add(sorted[i]);
            }
            else
            {
                runs.Add(current);
                current = new List<AxisCandidate> { sorted[i] };
            }
        }
        runs.Add(current);
        return runs;
    }

    private static Cluster Build(List<AxisCandidate> members) => new()
    {
        Coordinate = members.Average(m => m.Coordinate),
        CrossMin = members.Min(m => m.Cross),
        CrossMax = members.Max(m => m.Cross),
        Top = members.Max(m => m.Top),
        Members = members
    };
}
