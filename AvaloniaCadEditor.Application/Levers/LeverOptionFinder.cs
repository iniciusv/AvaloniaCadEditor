using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Levers;

/// <summary>Um número de uma opção e quanto ele anda para cada unidade do número principal.</summary>
public sealed record LeverShare(CodeLever Lever, double Weight);

public enum LeverRelationKind
{
    Coaxial,
    Symmetry
}

/// <summary>
/// Uma relação da peça que a opção mantém ou quebra: <paramref name="Fact"/> é o elemento que precisaria
/// acompanhar <paramref name="Partner"/>, o que se move.
/// </summary>
/// <param name="Axis">Para simetria, o eixo normal ao plano.</param>
public sealed record LeverRelation(LeverRelationKind Kind, string Fact, string Partner, FrameAxis? Axis = null);

/// <summary>
/// Uma forma de mudar a cota: um número, ou um grupo de números que andam juntos para não quebrar uma
/// relação (o furo e a porca coaxial a ele; um furo e o simétrico).
/// </summary>
/// <param name="Shares">O primeiro é o número principal (peso 1).</param>
/// <param name="Rate">Quanto a cota muda para cada unidade do número principal.</param>
/// <param name="Changes">O que muda na peça, por unidade do número principal.</param>
/// <param name="Keeps">Relações que os números do grupo preservam.</param>
/// <param name="Breaks">Relações que esta opção quebra.</param>
/// <param name="OtherDimensionsChanged">Quantas das outras cotas observadas também mudam.</param>
public sealed record LeverOption(
    IReadOnlyList<LeverShare> Shares,
    double Rate,
    IReadOnlyList<FactChange> Changes,
    IReadOnlyList<LeverRelation> Keeps,
    IReadOnlyList<LeverRelation> Breaks,
    int OtherDimensionsChanged)
{
    public CodeLever Primary => Shares[0].Lever;

    public bool IsGroup => Shares.Count > 1;

    /// <summary>Elementos que se deslocam.</summary>
    public IReadOnlyList<string> Moved => Changes.Where(c => c.Moves).Select(c => c.Name).ToList();

    /// <summary>Elementos que só mudam de tamanho.</summary>
    public IReadOnlyList<string> Resized => Changes.Where(c => c.Kind == FactChangeKind.Resized).Select(c => c.Name).ToList();
}

/// <summary>
/// Responde, sobre um <see cref="LeverStudy"/>, quais números mudam uma medida e o que cada um arrasta.
/// Para cada número que sozinho quebraria uma relação (coaxial, simetria), procura os números que movem o
/// elemento parceiro na mesma direção e propõe o grupo. As opções saem ordenadas: menos relações quebradas,
/// menos elementos movidos, menos cotas afetadas, controle direto (1 mm no número = 1 mm na cota).
///
/// Sem estado: a mesma entrada dá a mesma resposta.
/// </summary>
public static class LeverOptionFinder
{
    private const double RateTolerance = 1e-6;
    private const double ParallelTolerance = 1e-3;
    private const double MotionTolerance = 1e-6;
    private const double WatchTolerance = 1e-7;

    /// <param name="measure">A medida em questão sobre um conjunto de fatos (a cota selecionada).</param>
    /// <param name="watched">Outras medidas a observar (as demais cotas da folha), para contar os efeitos colaterais.</param>
    public static IReadOnlyList<LeverOption> Find(
        LeverStudy study,
        Func<IReadOnlyList<PrimitiveFact>, double?> measure,
        IReadOnlyList<Func<IReadOnlyList<PrimitiveFact>, double?>>? watched = null,
        int maxOptions = 8)
    {
        ArgumentNullException.ThrowIfNull(study);
        ArgumentNullException.ThrowIfNull(measure);
        watched ??= Array.Empty<Func<IReadOnlyList<PrimitiveFact>, double?>>();

        if (measure(study.Baseline) is not { } baseValue) return Array.Empty<LeverOption>();
        var watchedBase = watched.Select(w => w(study.Baseline)).ToList();

        var rates = new Dictionary<int, double>();
        foreach (var effect in study.Effects)
        {
            if (measure(effect.Facts) is { } value) rates[effect.Lever.Id] = (value - baseValue) / effect.Step;
        }

        var options = new List<LeverOption>();
        foreach (var effect in study.Effects)
        {
            if (!rates.TryGetValue(effect.Lever.Id, out double rate) || Math.Abs(rate) < RateTolerance) continue;

            var perUnit = Scale(effect.Changes, 1 / effect.Step);
            var requirements = Requirements(perUnit, study.Relations);
            options.Add(new LeverOption(
                [new LeverShare(effect.Lever, 1)], rate, perUnit,
                Array.Empty<LeverRelation>(), requirements.Select(r => r.Relation).ToList(),
                CountChanged(watched, watchedBase, effect.Facts)));

            if (requirements.Count > 0 && Group(study, effect, requirements, rates, watched, watchedBase) is { } group)
            {
                options.Add(group);
            }
        }

        return options
            .OrderBy(o => o.Breaks.Count)
            .ThenBy(o => o.Changes.Count)
            .ThenBy(o => o.OtherDimensionsChanged)
            .ThenBy(o => Math.Abs(Math.Abs(o.Rate) - 1) < 1e-6 ? 0 : 1)
            .ThenBy(o => o.Primary.Kind)
            .ThenBy(o => o.Primary.Label, StringComparer.Ordinal)
            .ThenBy(o => o.Shares.Count)
            .Take(maxOptions)
            .ToList();
    }

    /// <summary>O que um elemento precisa fazer para manter uma relação com outro que se moveu.</summary>
    private sealed record Requirement(LeverRelation Relation, Vector3D Translation);

    /// <summary>
    /// As relações que os deslocamentos de <paramref name="perUnit"/> quebram: um membro de um conjunto
    /// coaxial que sai do eixo sem os outros, ou um lado de um par simétrico que anda sem o outro.
    /// </summary>
    private static IReadOnlyList<Requirement> Requirements(IReadOnlyList<FactChange> perUnit, ModelRelations relations)
    {
        var moved = perUnit.Where(c => c.Moves).ToDictionary(c => c.Name, c => c.Translation, StringComparer.Ordinal);
        var result = new List<Requirement>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var set in relations.CoaxialSets)
        {
            var mover = set.Members.FirstOrDefault(m => moved.ContainsKey(m.Name));
            if (mover == null) continue;

            var translation = moved[mover.Name];
            var axis = set.Group.Direction.Normalize();
            var across = translation - axis * translation.DotProduct(axis);
            if (across.Length < MotionTolerance) continue; // deslizar pelo eixo não tira ninguém do eixo

            foreach (var member in set.Members.Where(m => !moved.ContainsKey(m.Name)))
            {
                if (claimed.Add(member.Name))
                {
                    result.Add(new Requirement(new LeverRelation(LeverRelationKind.Coaxial, member.Name, mover.Name), translation));
                }
            }
        }

        foreach (var plane in relations.SymmetryPlanes)
        {
            var normal = FrameAxes.Unit(plane.Normal);
            foreach (var pair in plane.Pairs)
            {
                bool aMoved = moved.ContainsKey(pair.A.Name);
                bool bMoved = moved.ContainsKey(pair.B.Name);
                if (aMoved == bMoved) continue;

                var (mover, other) = aMoved ? (pair.A, pair.B) : (pair.B, pair.A);
                var translation = moved[mover.Name];
                var mirrored = translation - normal * (2 * translation.DotProduct(normal));
                if (claimed.Add(other.Name))
                {
                    result.Add(new Requirement(new LeverRelation(LeverRelationKind.Symmetry, other.Name, mover.Name, plane.Normal), mirrored));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// O grupo do número de <paramref name="primary"/> com, para cada elemento que precisa acompanhar, o
    /// número que o move na direção certa e mexe em menos coisas. <c>null</c> se algum não tiver número.
    /// </summary>
    private static LeverOption? Group(
        LeverStudy study,
        LeverEffect primary,
        IReadOnlyList<Requirement> requirements,
        IReadOnlyDictionary<int, double> rates,
        IReadOnlyList<Func<IReadOnlyList<PrimitiveFact>, double?>> watched,
        IReadOnlyList<double?> watchedBase)
    {
        var shares = new List<(LeverEffect Effect, double Weight)> { (primary, 1) };

        foreach (var requirement in requirements)
        {
            (LeverEffect Effect, double Weight)? best = null;
            foreach (var effect in study.Effects)
            {
                if (effect.Lever.Id == primary.Lever.Id) continue;
                var change = effect.Changes.FirstOrDefault(c => c.Name == requirement.Relation.Fact && c.Moves);
                if (change == null) continue;

                var perUnit = change.Translation * (1 / effect.Step);
                if (!Parallel(perUnit, requirement.Translation)) continue;
                double weight = requirement.Translation.DotProduct(perUnit) / perUnit.DotProduct(perUnit);
                if (best == null || effect.Changes.Count < best.Value.Effect.Changes.Count) best = (effect, weight);
            }
            if (best == null) return null;

            int existing = shares.FindIndex(s => s.Effect.Lever.Id == best.Value.Effect.Lever.Id);
            if (existing < 0)
            {
                shares.Add(best.Value);
            }
            else if (Math.Abs(shares[existing].Weight - best.Value.Weight) > 1e-6 * Math.Max(1, Math.Abs(best.Value.Weight)))
            {
                return null; // o mesmo número precisaria andar duas medidas diferentes
            }
        }

        double rate = shares.Sum(s => s.Weight * (rates.TryGetValue(s.Effect.Lever.Id, out var r) ? r : 0));
        if (Math.Abs(rate) < RateTolerance) return null;

        var changes = Combine(shares.Select(s => (Scale(s.Effect.Changes, 1 / s.Effect.Step), s.Weight)));
        double step = primary.Step;
        var values = shares.ToDictionary(s => s.Effect.Lever.Id, s => s.Effect.Lever.Value + s.Weight * step);
        var after = study.Evaluate(values);

        return new LeverOption(
            shares.Select(s => new LeverShare(s.Effect.Lever, s.Weight)).ToList(),
            rate,
            changes,
            requirements.Select(r => r.Relation).ToList(),
            Array.Empty<LeverRelation>(),
            after == null ? 0 : CountChanged(watched, watchedBase, after));
    }

    private static bool Parallel(Vector3D a, Vector3D b)
    {
        double la = a.Length, lb = b.Length;
        if (la < MotionTolerance || lb < MotionTolerance) return false;
        return a.CrossProduct(b).Length <= ParallelTolerance * la * lb && a.DotProduct(b) != 0;
    }

    private static int CountChanged(
        IReadOnlyList<Func<IReadOnlyList<PrimitiveFact>, double?>> watched,
        IReadOnlyList<double?> watchedBase,
        IReadOnlyList<PrimitiveFact> facts)
    {
        int count = 0;
        for (int i = 0; i < watched.Count; i++)
        {
            var now = watched[i](facts);
            var then = watchedBase[i];
            if (now.HasValue != then.HasValue || (now.HasValue && Math.Abs(now.Value - then!.Value) > WatchTolerance)) count++;
        }
        return count;
    }

    private static IReadOnlyList<FactChange> Scale(IReadOnlyList<FactChange> changes, double factor) =>
        changes.Select(c => c with { Translation = c.Translation * factor, SizeChange = c.SizeChange * factor }).ToList();

    /// <summary>Soma ponderada das mudanças de vários números, por elemento.</summary>
    private static IReadOnlyList<FactChange> Combine(IEnumerable<(IReadOnlyList<FactChange> Changes, double Weight)> parts)
    {
        var order = new List<string>();
        var byName = new Dictionary<string, (Vector3D Translation, Vector3D Size, bool Resized, FactChangeKind? Special)>(StringComparer.Ordinal);
        foreach (var (changes, weight) in parts)
        {
            foreach (var change in changes)
            {
                if (!byName.TryGetValue(change.Name, out var sum))
                {
                    order.Add(change.Name);
                    sum = (new Vector3D(0, 0, 0), new Vector3D(0, 0, 0), false, null);
                }
                bool special = change.Kind is FactChangeKind.Appeared or FactChangeKind.Vanished;
                byName[change.Name] = (
                    sum.Translation + change.Translation * weight,
                    sum.Size + change.SizeChange * weight,
                    sum.Resized || change.Kind is FactChangeKind.Resized or FactChangeKind.MovedAndResized,
                    special ? change.Kind : sum.Special);
            }
        }

        var result = new List<FactChange>();
        foreach (var name in order)
        {
            var (translation, size, resized, special) = byName[name];
            bool moves = translation.Length > MotionTolerance;
            var kind = special ?? (moves && resized ? FactChangeKind.MovedAndResized
                : moves ? FactChangeKind.Moved
                : resized ? FactChangeKind.Resized
                : (FactChangeKind?)null);
            if (kind != null) result.Add(new FactChange(name, kind.Value, translation, size));
        }
        return result;
    }
}
