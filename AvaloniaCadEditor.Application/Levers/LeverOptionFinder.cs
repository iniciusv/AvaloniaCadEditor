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

/// <summary>Por que uma opção muda a cota só por tabela, e não como uma medida.</summary>
public enum LeverCaveat
{
    /// <summary>É uma medida: o número desloca ou redimensiona 1 para 1.</summary>
    None,

    /// <summary>O número é o divisor de uma conta (<see cref="CodeLeverRole.Factor"/>).</summary>
    Factor,

    /// <summary>O número é componente de uma direção (<see cref="CodeLeverRole.Direction"/>).</summary>
    Direction,

    /// <summary>
    /// A sintaxe não acusa nada, mas a peça não responde como a uma medida: 1 no número mexe
    /// <see cref="LeverOption.Gain"/> no elemento que mais muda. É o caso do multiplicador (o sinal de um
    /// espelho, o <c>2</c> de <c>2 * raio</c>) e de um ângulo.
    /// </summary>
    Scaled
}

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

    /// <summary>Por que a opção não é uma medida direta; <see cref="LeverCaveat.None"/> quando é.</summary>
    public LeverCaveat Caveat { get; init; }

    /// <summary>O <see cref="LeverEffect.Gain"/> do número que deu o <see cref="Caveat"/> (ou do principal).</summary>
    public double Gain { get; init; }

    /// <summary>Uma medida: entra na lista padrão e pode vir escolhida. As outras só aparecem a pedido.</summary>
    public bool IsDirect => Caveat == LeverCaveat.None;

    /// <summary>Elementos que se deslocam.</summary>
    public IReadOnlyList<string> Moved => Changes.Where(c => c.Moves).Select(c => c.Name).ToList();

    /// <summary>Elementos que só mudam de tamanho.</summary>
    public IReadOnlyList<string> Resized => Changes.Where(c => c.Kind == FactChangeKind.Resized).Select(c => c.Name).ToList();
}

/// <summary>
/// Responde, sobre um <see cref="LeverStudy"/>, quais números mudam uma medida e o que cada um arrasta.
/// Para cada número que sozinho quebraria uma relação (coaxial, simetria), procura os números que movem o
/// elemento parceiro na mesma direção e propõe o grupo. As opções saem ordenadas: medidas antes das
/// indiretas (<see cref="LeverCaveat"/>), depois menos relações quebradas, menos elementos movidos, menos
/// cotas afetadas, controle direto (1 mm no número = 1 mm na cota).
///
/// Sem estado: a mesma entrada dá a mesma resposta.
/// </summary>
public static class LeverOptionFinder
{
    private const double RateTolerance = 1e-6;
    private const double ParallelTolerance = 1e-3;
    private const double MotionTolerance = 1e-6;
    private const double WatchTolerance = 1e-7;

    /// <summary>O que uma medida faz com o elemento que mais muda, por unidade (ver <see cref="LeverEffect.Gain"/>).</summary>
    private static readonly double[] MeasureGains = [0.5, 1, 2];

    private const double GainTolerance = 1e-4;

    /// <param name="measure">A medida em questão sobre um conjunto de fatos (a cota selecionada).</param>
    /// <param name="watched">Outras medidas a observar (as demais cotas da folha), para contar os efeitos colaterais.</param>
    /// <param name="maxOptions">O teto vale para as medidas e, à parte, para as indiretas: uma lista longa de
    /// fatores não tira o lugar de uma medida.</param>
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
            var (caveat, gain) = CaveatOf([effect]);
            options.Add(new LeverOption(
                [new LeverShare(effect.Lever, 1)], rate, perUnit,
                Array.Empty<LeverRelation>(), requirements.Select(r => r.Relation).ToList(),
                CountChanged(watched, watchedBase, effect.Facts))
            {
                Caveat = caveat,
                Gain = gain
            });

            if (requirements.Count > 0 && Group(study, effect, requirements, rates, watched, watchedBase) is { } group)
            {
                options.Add(group);
            }
        }

        var ordered = options
            .OrderBy(o => o.Breaks.Count)
            .ThenBy(o => o.Changes.Count)
            .ThenBy(o => o.OtherDimensionsChanged)
            .ThenBy(o => Math.Abs(Math.Abs(o.Rate) - 1) < 1e-6 ? 0 : 1)
            .ThenBy(o => o.Primary.Kind)
            .ThenBy(o => o.Primary.Label, StringComparer.Ordinal)
            .ThenBy(o => o.Shares.Count)
            .ToList();
        return ordered.Where(o => o.IsDirect).Take(maxOptions)
            .Concat(ordered.Where(o => !o.IsDirect).Take(maxOptions))
            .ToList();
    }

    /// <summary>
    /// Se os números da opção são medidas. A sintaxe acusa o divisor e a direção
    /// (<see cref="CodeLever.Role"/>); o resto vem da resposta da peça: uma medida mexe 1, 2 ou 0,5 por
    /// unidade no elemento que mais muda, e um multiplicador mexe o valor do que ele multiplica. Quando
    /// esse valor calha de ser 1 ou 2, o multiplicador passa por medida: é o limite de quem só sonda.
    /// </summary>
    private static (LeverCaveat Caveat, double Gain) CaveatOf(IEnumerable<LeverEffect> effects)
    {
        LeverEffect? first = null;
        foreach (var effect in effects)
        {
            first ??= effect;
            var caveat = effect.Lever.Role switch
            {
                CodeLeverRole.Factor => LeverCaveat.Factor,
                CodeLeverRole.Direction => LeverCaveat.Direction,
                _ => RespondsAsMeasure(effect) ? LeverCaveat.None : LeverCaveat.Scaled
            };
            if (caveat != LeverCaveat.None) return (caveat, effect.Gain);
        }
        return (LeverCaveat.None, first?.Gain ?? 0);
    }

    private static bool RespondsAsMeasure(LeverEffect effect)
    {
        if (effect.Changes.Any(c => c.Kind is FactChangeKind.Appeared or FactChangeKind.Vanished)) return false;

        double gain = effect.Gain;
        if (gain < MotionTolerance) return true; // mudou só por dentro da caixa (o raio de cima de um cone): não dá para dizer que não é
        return MeasureGains.Any(g => Math.Abs(gain - g) <= GainTolerance * g);
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

        var (caveat, gain) = CaveatOf(shares.Select(s => s.Effect));
        return new LeverOption(
            shares.Select(s => new LeverShare(s.Effect.Lever, s.Weight)).ToList(),
            rate,
            changes,
            requirements.Select(r => r.Relation).ToList(),
            Array.Empty<LeverRelation>(),
            after == null ? 0 : CountChanged(watched, watchedBase, after))
        {
            Caveat = caveat,
            Gain = gain
        };
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
