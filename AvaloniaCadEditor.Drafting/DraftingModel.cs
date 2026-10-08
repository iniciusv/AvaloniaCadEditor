using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Drafting;

/// <summary>Direção do eixo de um cilindro visto de lado, no plano da vista.</summary>
public enum CenterAxis
{
    None,
    Horizontal,
    Vertical
}

/// <summary>Um ponto que pode ser cotado, identificado por "sólido.papel" (ex.: "furo_fixacao.centro").
/// O Id é estável entre execuções: sobrevive a reexecução e a mudanças de parâmetro.</summary>
public sealed record DraftAnchor(string Id, P2 Point);

/// <summary>Uma primitiva da peça projetada na vista.</summary>
public abstract record ViewEntity(string SourceName, PrimitiveRole Role)
{
    /// <summary>O fato da análise que esta entidade projeta.</summary>
    public PrimitiveFact? Fact { get; init; }

    public abstract P2 Min { get; }
    public abstract P2 Max { get; }

    public abstract IReadOnlyList<DraftAnchor> Anchors { get; }
}

/// <summary>Cilindro visto pelo eixo (furo de frente).</summary>
public sealed record ViewCircle(string SourceName, PrimitiveRole Role, P2 Center, double Radius)
    : ViewEntity(SourceName, Role)
{
    public override P2 Min => new(Center.U - Radius, Center.V - Radius);
    public override P2 Max => new(Center.U + Radius, Center.V + Radius);

    public override IReadOnlyList<DraftAnchor> Anchors =>
    [
        new($"{SourceName}.centro", Center),
        new($"{SourceName}.esq", new P2(Center.U - Radius, Center.V)),
        new($"{SourceName}.dir", new P2(Center.U + Radius, Center.V)),
        new($"{SourceName}.inf", new P2(Center.U, Center.V - Radius)),
        new($"{SourceName}.sup", new P2(Center.U, Center.V + Radius))
    ];
}

/// <summary>Caixa, prisma ou cilindro visto de lado. Para o cilindro, <see cref="Axis"/> diz por
/// onde passa a linha de centro.</summary>
public sealed record ViewRect(string SourceName, PrimitiveRole Role, P2 MinCorner, P2 MaxCorner, CenterAxis Axis = CenterAxis.None)
    : ViewEntity(SourceName, Role)
{
    public override P2 Min => MinCorner;
    public override P2 Max => MaxCorner;

    public P2 Center => new((MinCorner.U + MaxCorner.U) / 2.0, (MinCorner.V + MaxCorner.V) / 2.0);

    public override IReadOnlyList<DraftAnchor> Anchors =>
    [
        new($"{SourceName}.esq_inf", MinCorner),
        new($"{SourceName}.dir_inf", new P2(MaxCorner.U, MinCorner.V)),
        new($"{SourceName}.dir_sup", MaxCorner),
        new($"{SourceName}.esq_sup", new P2(MinCorner.U, MaxCorner.V)),
        new($"{SourceName}.centro", Center)
    ];
}

/// <summary>Prisma visto pelo eixo (ex.: alojamento hexagonal de porca).</summary>
public sealed record ViewPolygon(string SourceName, PrimitiveRole Role, IReadOnlyList<P2> Points)
    : ViewEntity(SourceName, Role)
{
    public override P2 Min => new(Points.Min(p => p.U), Points.Min(p => p.V));
    public override P2 Max => new(Points.Max(p => p.U), Points.Max(p => p.V));

    public override IReadOnlyList<DraftAnchor> Anchors =>
        Points.Select((p, i) => new DraftAnchor($"{SourceName}.v{i + 1}", p)).ToList();
}

public enum DimensionKind
{
    Horizontal,
    Vertical,
    Diameter,

    /// <summary>Chamada de um componente (peça aninhada, ex.: "3× FuroPorca M3"): sem medida, só o rótulo.</summary>
    Callout
}

/// <summary>Os níveis da hierarquia de cotas e seus nomes (na legenda).</summary>
public static class DimensionLevels
{
    public const int Chains = 1;
    public const int Location = 2;
    public const int Totals = 3;

    public static string Title(int level) => level switch
    {
        Chains => "Cadeias e tamanhos",
        Location => "Localização",
        Totals => "Totais",
        _ => $"Nível {level}"
    };
}

/// <summary>De que lado da peça a linha de cota fica (Diameter não tem lado: usa chamada).</summary>
public enum DimensionSide
{
    None,
    Below,
    Left,
    Above,
    Right
}

/// <summary>
/// Uma cota no plano da vista. Para <see cref="DimensionKind.Horizontal"/> a linha de cota fica em
/// V = <see cref="LinePosition"/> e mede de From.U a To.U; para <see cref="DimensionKind.Vertical"/>
/// fica em U = <see cref="LinePosition"/> e mede de From.V a To.V. Para
/// <see cref="DimensionKind.Diameter"/>, From e To são as pontas do diâmetro.
/// </summary>
/// <param name="Key">Identidade estável (para esconder uma cota automática entre execuções).</param>
/// <param name="Level">Hierarquia: 1 = cadeias e tamanhos (junto à peça), 2 = localização, 3 = totais
/// (por fora). O layout nunca põe uma fileira de nível maior por dentro de uma de nível menor.</param>
public sealed record DraftDimension(
    string Key,
    DimensionKind Kind,
    P2 From,
    P2 To,
    double LinePosition,
    double Value,
    string Text,
    IReadOnlyList<string> SourceNames,
    bool IsManual = false,
    int Level = 1,
    DimensionSide Side = DimensionSide.None);

/// <summary>Linha de centro ou de simetria (traço-ponto).</summary>
public sealed record CenterLine(P2 A, P2 B);

/// <summary>O que o analisador precisa da peça.</summary>
/// <param name="Subjects">O que desenhar, já levado para o referencial da peça (hoje, a peça isolada;
/// no futuro, várias).</param>
/// <param name="View">A vista a desenhar.</param>
/// <param name="FrameBoundsMin">Canto mínimo da malha no referencial (o contorno real, já com as booleanas).</param>
/// <param name="FrameBoundsMax">Canto máximo da malha no referencial.</param>
public sealed record DraftingInput(
    IReadOnlyList<AnalysisSubject> Subjects,
    StandardView View,
    Point3D FrameBoundsMin,
    Point3D FrameBoundsMax)
{
    /// <summary>Uma árvore de features só, já no referencial.</summary>
    public static DraftingInput ForRoot(IFeature root, Placement rootToFrame, StandardView view, Point3D frameBoundsMin, Point3D frameBoundsMax) =>
        new([new AnalysisSubject(root, rootToFrame, root.Name)], view, frameBoundsMin, frameBoundsMax);

    /// <summary>
    /// Monta a entrada para a peça isolada com H: a árvore de features dela, levada para o
    /// <paramref name="frame"/>, e o contorno medido na malha (<paramref name="worldPositions"/>, xyz
    /// planos em coordenadas do mundo). <c>null</c> se não houver malha.
    /// </summary>
    public static DraftingInput? ForIsolated(IFeature feature, PartFrame frame, float[]? worldPositions, StandardView view)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(frame);
        if (worldPositions is null || worldPositions.Length < 3) return null;

        var (root, rootToWorld) = feature switch
        {
            PartInstance instance => (instance.Definition, instance.Placement),
            TransformedFeature transformed => (transformed.Source, transformed.Placement),
            _ => (feature, Placement.Identity)
        };

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (int i = 0; i + 2 < worldPositions.Length; i += 3)
        {
            var p = frame.ToFrame(new Point3D(worldPositions[i], worldPositions[i + 1], worldPositions[i + 2]));
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
        }

        var subject = new AnalysisSubject(root, rootToWorld.Then(frame.WorldToFrame), feature.Name, feature.Id);
        return new DraftingInput([subject], view, new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ));
    }
}

/// <summary>O desenho de uma vista: geometria projetada, cotas e linhas de centro.</summary>
/// <param name="FrontDepth">Profundidade (ao longo de <see cref="ViewBasis.W"/>) da face da peça mais
/// próxima da câmera; as cotas são desenhadas logo à frente dela.</param>
/// <param name="Gap">Espaçamento entre fileiras de cota, em mm (escala do desenho).</param>
public sealed record DraftingSheet(
    ViewBasis Basis,
    P2 PartMin,
    P2 PartMax,
    double FrontDepth,
    double Gap,
    IReadOnlyList<ViewEntity> Entities,
    IReadOnlyList<DraftDimension> Dimensions,
    IReadOnlyList<CenterLine> CenterLines,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Cotas que a política de densidade escondeu (ver <see cref="DensityPolicy"/>).</summary>
    public IReadOnlyList<SuppressedDimension> Suppressed { get; init; } = Array.Empty<SuppressedDimension>();

    /// <summary>Todos os pontos cotáveis: os das primitivas e os quatro cantos da peça ("peca.*").</summary>
    public IReadOnlyList<DraftAnchor> Anchors =>
        Entities.SelectMany(e => e.Anchors)
            .Concat(
            [
                new DraftAnchor("peca.esq_inf", PartMin),
                new DraftAnchor("peca.dir_inf", new P2(PartMax.U, PartMin.V)),
                new DraftAnchor("peca.dir_sup", PartMax),
                new DraftAnchor("peca.esq_sup", new P2(PartMin.U, PartMax.V))
            ])
            .ToList();

    public DraftAnchor? FindAnchor(string id) => Anchors.FirstOrDefault(a => a.Id == id);
}
