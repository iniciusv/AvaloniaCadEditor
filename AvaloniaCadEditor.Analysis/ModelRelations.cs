using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Analysis;

/// <summary>
/// Fatos que compartilham a mesma direção de eixo (furos furados por qualquer dos dois lados caem
/// no mesmo grupo), com uma base (U, V) no plano perpendicular. Para direções paralelas a um eixo
/// do referencial, a base é feita dos próprios eixos do referencial (Z → X,Y; X → Y,Z; Y → X,Z), de
/// modo que as coordenadas do grupo são coordenadas do referencial.
/// </summary>
public sealed class DirectionGroup
{
    public required Vector3D Direction { get; init; }
    public required Vector3D U { get; init; }
    public required Vector3D V { get; init; }

    /// <summary>Eixo do referencial paralelo à direção (null se oblíqua) e os de U e V.</summary>
    public FrameAxis? Normal { get; init; }
    public FrameAxis? AxisU { get; init; }
    public FrameAxis? AxisV { get; init; }

    /// <summary>Vazios redondos com este eixo.</summary>
    public required IReadOnlyList<PrimitiveFact> Holes { get; init; }

    /// <summary>Seções (caixas, prismas — material ou vazio) e cilindros de material com este eixo.</summary>
    public required IReadOnlyList<PrimitiveFact> Walls { get; init; }

    public required IReadOnlyList<AxisCandidate> CandidatesU { get; init; }
    public required IReadOnlyList<AxisCandidate> CandidatesV { get; init; }
    public required AxisClusters ClustersU { get; init; }
    public required AxisClusters ClustersV { get; init; }

    /// <summary>Arcos detectados nos perfis das seções do grupo.</summary>
    public required IReadOnlyList<(PrimitiveFact Fact, ArcInfo Arc, double Top)> Arcs { get; init; }

    public (double U, double V, double Depth) Local(Point3D p)
    {
        var d = p - Point3D.Origin;
        return (d.DotProduct(U), d.DotProduct(V), d.DotProduct(Direction));
    }

    public Point3D ToPoint(double u, double v, double depth) =>
        Point3D.Origin + U * u + V * v + Direction * depth;
}

/// <summary>Furos cujos eixos são a mesma reta (ex.: furo passante e o rebaixo da cabeça do parafuso).</summary>
/// <param name="Members">Do menor para o maior raio.</param>
public sealed record CoaxialSet(DirectionGroup Group, double U, double V, IReadOnlyList<PrimitiveFact> Members);

/// <summary>
/// Furos de mesmo diâmetro e mesma direção alinhados numa fileira: mesma coordenada transversal,
/// ordenados ao longo do outro eixo do plano do grupo.
/// </summary>
/// <param name="AlongU">True se a fileira corre ao longo de U do grupo (a transversal é V).</param>
/// <param name="Positions">Posições distintas ao longo da fileira, em ordem.</param>
/// <param name="Members">Um furo por posição (o primeiro, se houver coincidentes).</param>
/// <param name="Pitch">O passo, quando todos os passos são iguais; senão null.</param>
public sealed record LinearPattern(
    DirectionGroup Group,
    bool AlongU,
    double CrossCoordinate,
    double Diameter,
    IReadOnlyList<double> Positions,
    IReadOnlyList<PrimitiveFact> Members,
    IReadOnlyList<PrimitiveFact> AllMembers,
    IReadOnlyList<double> Steps,
    double? Pitch)
{
    public int Count => Positions.Count;

    public FrameAxis? AlongAxis => AlongU ? Group.AxisU : Group.AxisV;
    public FrameAxis? CrossAxis => AlongU ? Group.AxisV : Group.AxisU;
}

/// <summary>Dois fatos de mesmo tipo, papel e tamanho, espelhados um no outro.</summary>
public sealed record SymmetryPair(PrimitiveFact A, PrimitiveFact B);

/// <summary>
/// Plano de simetria perpendicular a um eixo do referencial.
/// </summary>
/// <param name="OnPlane">Fatos centrados no próprio plano (simétricos sozinhos).</param>
/// <param name="IsGlobal">A peça inteira é simétrica aqui: o corpo está centrado no plano e todo fato
/// está num par ou sobre o plano. Senão é uma simetria local (um grupo de furos, por exemplo).</param>
public sealed record SymmetryPlane(
    FrameAxis Normal,
    double Coordinate,
    IReadOnlyList<SymmetryPair> Pairs,
    IReadOnlyList<PrimitiveFact> OnPlane,
    bool IsGlobal);

/// <summary>Camada 2: tudo que a análise inferiu, em 3D, sem saber nada de vista ou tela.</summary>
public sealed record ModelRelations(
    IReadOnlyList<PrimitiveFact> Facts,
    IReadOnlyList<DirectionGroup> Groups,
    IReadOnlyDictionary<FrameAxis, AxisClusters> FrameClusters,
    IReadOnlyList<CoaxialSet> CoaxialSets,
    IReadOnlyList<LinearPattern> LinearPatterns,
    IReadOnlyList<SymmetryPlane> SymmetryPlanes,
    Point3D? BodyMin,
    Point3D? BodyMax)
{
    public static readonly ModelRelations Empty = new(
        Array.Empty<PrimitiveFact>(), Array.Empty<DirectionGroup>(), new Dictionary<FrameAxis, AxisClusters>(),
        Array.Empty<CoaxialSet>(), Array.Empty<LinearPattern>(), Array.Empty<SymmetryPlane>(), null, null);

    /// <summary>O plano de simetria global perpendicular a <paramref name="axis"/>, se houver.</summary>
    public SymmetryPlane? GlobalSymmetry(FrameAxis axis) =>
        SymmetryPlanes.FirstOrDefault(p => p.IsGlobal && p.Normal == axis);
}
