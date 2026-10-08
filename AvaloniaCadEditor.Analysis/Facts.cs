using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Analysis;

/// <summary>
/// Uma raiz a analisar e como levá-la para o referencial da análise.
/// </summary>
/// <param name="Feature">Árvore de features (ex.: a <c>Definition</c> de uma peça, ou uma feature de topo da cena).</param>
/// <param name="ToFrame">Leva as coordenadas de <paramref name="Feature"/> para o referencial da análise.</param>
/// <param name="Name">Nome do sujeito (ex.: a instância "bloco_fixacao_belt"); não entra no nome dos fatos.</param>
/// <param name="OwnerId">Feature de topo da cena que o sujeito representa (para destacar a seleção); por
/// padrão, a própria <paramref name="Feature"/>.</param>
public sealed record AnalysisSubject(IFeature Feature, Placement ToFrame, string Name = "", Guid? OwnerId = null);

/// <summary>
/// Peça aninhada (uma <see cref="PartInstance"/> dentro do <c>Build()</c> do sujeito, como
/// <c>new FuroPorca(...).Instance("fixacao_1")</c>) a que um fato pertence. No desenho da hospedeira
/// ela é uma caixa-preta: cotada pela referência, sem as medidas internas, que são parâmetros dela.
/// </summary>
/// <param name="Name">Nome da instância na hospedeira ("fixacao_1").</param>
/// <param name="TypeName">Tipo da peça ("FuroPorca").</param>
/// <param name="Label">Texto da chamada: <see cref="Part.DrawingLabel"/> ou o tipo mais os parâmetros enum.</param>
public sealed record ComponentRef(string Name, string TypeName, string Label)
{
    public static ComponentRef For(PartInstance instance)
    {
        var part = instance.Part;
        var label = part.DrawingLabel;
        if (string.IsNullOrWhiteSpace(label))
        {
            var enums = part.GetParameters()
                .Where(p => p.Type.IsEnum && p.Value != null)
                .Select(p => p.Value!.ToString());
            label = string.Join(" ", new[] { instance.PartTypeName }.Concat(enums));
        }
        return new ComponentRef(instance.Name, instance.PartTypeName, label);
    }
}

public enum PrimitiveKind
{
    /// <summary>Cilindro (inclui extrusão de perfil circular).</summary>
    Cylinder,
    Cone,
    Box,

    /// <summary>Seção poligonal extrudada (prisma ou extrusão de perfil poligonal/retangular).</summary>
    Prism
}

public enum PrimitiveRole
{
    /// <summary>Material da peça.</summary>
    Body,

    /// <summary>Vazio: furo, rebaixo, alojamento (ferramenta de subtração ou <c>IsTransientTool</c>).</summary>
    Void
}

/// <summary>
/// Uma primitiva da árvore de features, já no referencial da análise. É a unidade sobre a qual as
/// relações são inferidas: tudo que importa da geometria está aqui, sem depender da feature de origem.
/// </summary>
public sealed class PrimitiveFact
{
    /// <summary>Id da feature primitiva (muda a cada execução do código; serve dentro de uma análise).</summary>
    public required Guid Id { get; init; }

    /// <summary>Feature de topo dona da primitiva (para destacar a seleção).</summary>
    public required Guid OwnerId { get; init; }

    /// <summary>Peça aninhada a que o fato pertence (a mais externa, dentro do sujeito); null para
    /// sólidos do próprio sujeito.</summary>
    public ComponentRef? Component { get; init; }

    /// <summary>Nome do sujeito de onde veio.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Nome estável dentro do sujeito: o caminho das instâncias aninhadas mais o nome do sólido
    /// ("suporte/furo"); na raiz do sujeito, só o nome ("furo_fixacao"). Repetidos ganham "#2".
    /// </summary>
    public required string Name { get; init; }

    public required PrimitiveKind Kind { get; init; }
    public required PrimitiveRole Role { get; init; }

    /// <summary>Direção (normalizada) do eixo do cilindro/cone ou da extrusão da seção.</summary>
    public required Vector3D Axis { get; init; }

    /// <summary>Comprimento ao longo de <see cref="Axis"/>.</summary>
    public required double Height { get; init; }

    /// <summary>Centro da base (cilindro/cone); para seções, o centro da caixa da base.</summary>
    public required Point3D BaseCenter { get; init; }

    /// <summary>Raio da base (cilindro/cone); 0 para seções.</summary>
    public double Radius { get; init; }

    /// <summary>Raio do topo (cone); igual a <see cref="Radius"/> no cilindro.</summary>
    public double TopRadius { get; init; }

    /// <summary>Laço da seção base (caixa/prisma), em ordem; vazio para cilindro/cone.</summary>
    public IReadOnlyList<Point3D> Profile { get; init; } = Array.Empty<Point3D>();

    public required Point3D BoundsMin { get; init; }
    public required Point3D BoundsMax { get; init; }

    public bool IsRound => Kind is PrimitiveKind.Cylinder or PrimitiveKind.Cone;

    public double MaxRadius => Math.Max(Radius, TopRadius);

    public Point3D TopCenter => BaseCenter + Axis * Height;

    /// <summary>Vértices da seção na base e no topo (caixa/prisma).</summary>
    public IReadOnlyList<Point3D> SectionVertices =>
        Profile.Concat(Profile.Select(p => p + Axis * Height)).ToList();

    public override string ToString() => $"{Name} ({Kind}, {Role})";
}

/// <summary>Um dos três eixos do referencial da análise.</summary>
public enum FrameAxis
{
    X,
    Y,
    Z
}

public static class FrameAxes
{
    private const double Tolerance = 1e-6;

    public static readonly FrameAxis[] All = [FrameAxis.X, FrameAxis.Y, FrameAxis.Z];

    /// <summary>O eixo do referencial paralelo a <paramref name="direction"/>, ou null se for oblíqua.</summary>
    public static FrameAxis? Of(Vector3D direction)
    {
        var d = direction.Normalize();
        if (Math.Abs(Math.Abs(d.X) - 1) < Tolerance) return FrameAxis.X;
        if (Math.Abs(Math.Abs(d.Y) - 1) < Tolerance) return FrameAxis.Y;
        if (Math.Abs(Math.Abs(d.Z) - 1) < Tolerance) return FrameAxis.Z;
        return null;
    }

    public static Vector3D Unit(FrameAxis axis) => axis switch
    {
        FrameAxis.X => Vector3D.UnitX,
        FrameAxis.Y => Vector3D.UnitY,
        _ => Vector3D.UnitZ
    };

    public static double Component(Point3D p, FrameAxis axis) => axis switch
    {
        FrameAxis.X => p.X,
        FrameAxis.Y => p.Y,
        _ => p.Z
    };

    public static double Component(Vector3D v, FrameAxis axis) => axis switch
    {
        FrameAxis.X => v.X,
        FrameAxis.Y => v.Y,
        _ => v.Z
    };
}
