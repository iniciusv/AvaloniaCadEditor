using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Analysis;

/// <summary>O que aconteceu com um fato entre duas versões da peça.</summary>
public enum FactChangeKind
{
    Moved,
    Resized,
    MovedAndResized,

    /// <summary>Só existe na versão nova (um <c>if</c> do código passou a criá-lo).</summary>
    Appeared,

    /// <summary>Só existe na versão antiga.</summary>
    Vanished
}

/// <summary>
/// A mudança de um fato, identificado pelo nome estável (<see cref="PrimitiveFact.Name"/>).
/// </summary>
/// <param name="Translation">Deslocamento do centro da caixa do fato.</param>
/// <param name="SizeChange">Variação das dimensões da caixa (X, Y, Z).</param>
public sealed record FactChange(string Name, FactChangeKind Kind, Vector3D Translation, Vector3D SizeChange)
{
    public bool Moves => Kind is FactChangeKind.Moved or FactChangeKind.MovedAndResized;
}

/// <summary>
/// Compara duas extrações de fatos da mesma peça (antes e depois de mudar um número do código) e diz o
/// que se moveu ou mudou de tamanho. Puro e determinístico: a mesma entrada dá sempre a mesma saída.
/// Os fatos são pareados pelo nome estável, que sobrevive à reexecução (os Ids não).
/// </summary>
public static class FactMotion
{
    /// <summary>Abaixo disso, em mm, a diferença é ruído de ponto flutuante.</summary>
    public const double DefaultTolerance = 1e-7;

    private static readonly Vector3D None = new(0, 0, 0);

    /// <summary>As mudanças, na ordem dos fatos de <paramref name="before"/> (os que surgiram vão no fim).
    /// Fatos iguais nas duas versões ficam de fora.</summary>
    public static IReadOnlyList<FactChange> Compare(
        IReadOnlyList<PrimitiveFact> before,
        IReadOnlyList<PrimitiveFact> after,
        double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var afterByName = new Dictionary<string, PrimitiveFact>(StringComparer.Ordinal);
        foreach (var fact in after) afterByName.TryAdd(fact.Name, fact);

        var changes = new List<FactChange>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var old in before)
        {
            if (!seen.Add(old.Name)) continue;
            if (!afterByName.TryGetValue(old.Name, out var now))
            {
                changes.Add(new FactChange(old.Name, FactChangeKind.Vanished, None, None));
                continue;
            }

            var change = Diff(old, now, tolerance);
            if (change != null) changes.Add(change);
        }

        foreach (var fact in after)
        {
            if (seen.Add(fact.Name)) changes.Add(new FactChange(fact.Name, FactChangeKind.Appeared, None, None));
        }
        return changes;
    }

    /// <summary>Centro da caixa do fato.</summary>
    public static Point3D Center(PrimitiveFact fact) =>
        new((fact.BoundsMin.X + fact.BoundsMax.X) / 2, (fact.BoundsMin.Y + fact.BoundsMax.Y) / 2, (fact.BoundsMin.Z + fact.BoundsMax.Z) / 2);

    private static FactChange? Diff(PrimitiveFact old, PrimitiveFact now, double tolerance)
    {
        var translation = Center(now) - Center(old);
        var size = (now.BoundsMax - now.BoundsMin) - (old.BoundsMax - old.BoundsMin);
        bool moved = translation.Length > tolerance;
        bool resized = size.Length > tolerance
            || Math.Abs(now.Radius - old.Radius) > tolerance
            || Math.Abs(now.TopRadius - old.TopRadius) > tolerance
            || Math.Abs(now.Height - old.Height) > tolerance;

        if (!moved && !resized) return null;
        var kind = moved && resized ? FactChangeKind.MovedAndResized : moved ? FactChangeKind.Moved : FactChangeKind.Resized;
        return new FactChange(old.Name, kind, moved ? translation : None, size);
    }
}
