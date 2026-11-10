using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Levers;

/// <summary>Um número de uma opção e quanto ele anda para cada unidade do número principal.</summary>
public sealed record LeverShare(CodeLever Lever, double Weight);

public enum LeverRelationKind
{
    Coaxial,
    Symmetry,

    /// <summary>
    /// Pontos seguidos de um contorno com a mesma coordenada: uma aresta reta (ou, quando são todos, o
    /// plano do contorno). Aqui <c>Fact</c> são os pontos ("perfil_base · pontos 1 e 2") e <c>Partner</c>
    /// é a coordenada que eles têm em comum ("x").
    /// </summary>
    Aligned,

    /// <summary>
    /// Um elemento centrado no plano de simetria da peça inteira (o corpo de uma placa simétrica). Aqui
    /// <c>Fact</c> é o elemento e <c>Partner</c> é o plano ("x = 0"). Uma aresta só do corpo que anda tira o
    /// corpo do centro: a peça deixa de ser simétrica.
    /// </summary>
    Centered
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
/// elemento parceiro na mesma direção e propõe o grupo. O mesmo vale para os vértices de um contorno: os
/// pontos seguidos com a mesma coordenada (uma aresta reta) ganham um grupo que os move juntos, porque
/// mudar um só entorta a aresta. Numa peça simétrica, a aresta de um lado ganha um grupo com a do outro,
/// em sentido contrário: a largura muda e o corpo continua centrado no plano de simetria
/// (<see cref="LeverRelationKind.Centered"/>). As opções saem ordenadas: medidas antes das
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

        // As arestas dos contornos, cada uma inteira, e a que aresta cada número pertence.
        var options = new List<LeverOption>();
        var runs = new Dictionary<int, IReadOnlyList<CodeLever>>();
        foreach (var edge in study.Runs)
        {
            foreach (var lever in edge.Levers) runs[lever.Id] = edge.Levers;
        }

        // Quem pode acompanhar um elemento num grupo: uma aresta inteira, ou um número que não é ponto de
        // aresta (um ponto só entortaria a aresta dele).
        var companions = study.Runs.Select(r => new Mover(r.Levers, r.Effect))
            .Concat(study.Effects.Where(e => !runs.ContainsKey(e.Lever.Id)).Select(e => new Mover([e.Lever], e)))
            .ToList();
        var context = new GroupContext(study, measure, baseValue, watched, watchedBase, companions);

        foreach (var edge in study.Runs)
        {
            if (AlignedGroup(study, edge, measure, baseValue, watched, watchedBase) is not { } together) continue;
            options.Add(together);

            var requirements = Requirements(together.Changes, study.Relations);
            if (requirements.Count > 0 && Group(context, new Mover(edge.Levers, edge.Effect), requirements, runs) is { } group)
            {
                options.Add(group);
            }
        }

        foreach (var effect in study.Effects)
        {
            runs.TryGetValue(effect.Lever.Id, out var run);
            if (!rates.TryGetValue(effect.Lever.Id, out double rate) || Math.Abs(rate) < RateTolerance) continue;

            var perUnit = Scale(effect.Changes, 1 / effect.Step);
            var requirements = Requirements(perUnit, study.Relations);
            var breaks = requirements.Select(r => r.Relation).ToList();
            if (run != null) breaks.Add(Alignment(run));
            var (caveat, gain) = CaveatOf([effect.Lever], [effect]);
            options.Add(new LeverOption(
                [new LeverShare(effect.Lever, 1)], rate, perUnit,
                Array.Empty<LeverRelation>(), breaks,
                CountChanged(watched, watchedBase, effect.Facts))
            {
                Caveat = caveat,
                Gain = gain
            });

            if (requirements.Count > 0 && Group(context, new Mover([effect.Lever], effect), requirements, runs) is { } group)
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

        // A aresta direita com a esquerda em sentido contrário é o mesmo movimento que a esquerda com a
        // direita: fica a primeira.
        var distinct = new List<LeverOption>();
        foreach (var option in ordered)
        {
            if (!distinct.Any(d => SameMotion(d, option))) distinct.Add(option);
        }
        ordered = distinct;

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
    /// <param name="levers">Os números da opção.</param>
    /// <param name="effects">O que eles fazem: um efeito por número, ou o efeito do grupo inteiro.</param>
    private static (LeverCaveat Caveat, double Gain) CaveatOf(IReadOnlyList<CodeLever> levers, IReadOnlyList<LeverEffect> effects)
    {
        double gain = effects.Count > 0 ? effects[0].Gain : 0;

        if (levers.Any(l => l.Role == CodeLeverRole.Factor)) return (LeverCaveat.Factor, gain);
        if (levers.Any(l => l.Role == CodeLeverRole.Direction)) return (LeverCaveat.Direction, gain);

        var scaled = effects.FirstOrDefault(e => !RespondsAsMeasure(e));
        return scaled == null ? (LeverCaveat.None, gain) : (LeverCaveat.Scaled, scaled.Gain);
    }

    // ------------------------------------------------------------------ vértices alinhados

    /// <summary>"perfil_base · pontos 1 e 2" alinhados em "x".</summary>
    private static LeverRelation Alignment(IReadOnlyList<CodeLever> run)
    {
        var points = run.Select(l => (l.Vertex!.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        string list = points.Count == 2
            ? $"{points[0]} e {points[1]}"
            : string.Join(", ", points.Take(points.Count - 1)) + " e " + points[^1];
        return new LeverRelation(LeverRelationKind.Aligned, $"{run[0].Owner} · pontos {list}", run[0].Path);
    }

    /// <summary>
    /// Os pontos de uma aresta movidos juntos. O efeito é o que o estudo mediu numa execução com todos
    /// trocados, e não a soma dos efeitos de cada um: numa aresta que é a borda da peça, cada ponto sozinho
    /// já leva a borda (a taxa de cada um é 1, e a dos dois juntos também é 1, não 2), ou não leva nada
    /// (a borda de baixo, quando só um ponto sobe).
    /// </summary>
    private static LeverOption? AlignedGroup(
        LeverStudy study,
        LeverRunEffect edge,
        Func<IReadOnlyList<PrimitiveFact>, double?> measure,
        double baseValue,
        IReadOnlyList<Func<IReadOnlyList<PrimitiveFact>, double?>> watched,
        IReadOnlyList<double?> watchedBase)
    {
        var effect = edge.Effect;
        if (measure(effect.Facts) is not { } value) return null;

        double rate = (value - baseValue) / effect.Step;
        if (Math.Abs(rate) < RateTolerance) return null;

        var perUnit = Scale(effect.Changes, 1 / effect.Step);
        var (caveat, gain) = CaveatOf(edge.Levers, [effect]);
        return new LeverOption(
            edge.Levers.Select(l => new LeverShare(l, 1)).ToList(),
            rate,
            perUnit,
            [Alignment(edge.Levers)],
            Requirements(perUnit, study.Relations).Select(r => r.Relation).ToList(),
            CountChanged(watched, watchedBase, effect.Facts))
        {
            Caveat = caveat,
            Gain = gain
        };
    }

    private static bool RespondsAsMeasure(LeverEffect effect)
    {
        if (effect.Changes.Any(c => c.Kind is FactChangeKind.Appeared or FactChangeKind.Vanished)) return false;

        double gain = effect.Gain;
        if (gain < MotionTolerance) return true; // mudou só por dentro da caixa (o raio de cima de um cone): não dá para dizer que não é
        return MeasureGains.Any(g => Math.Abs(gain - g) <= GainTolerance * g);
    }

    /// <summary>
    /// O que um elemento precisa fazer, a mais, para manter uma relação com outro que mudou.
    /// </summary>
    /// <param name="Size">A mudança de tamanho que ele também precisa ter; <c>null</c> quando o tamanho é
    /// livre (o furo e a porca coaxial têm tamanhos diferentes; o corpo centrado pode crescer).</param>
    private sealed record Requirement(LeverRelation Relation, Vector3D Translation, Vector3D? Size = null);

    /// <summary>
    /// As relações que os deslocamentos de <paramref name="perUnit"/> quebram: um membro de um conjunto
    /// coaxial que sai do eixo sem os outros, um lado de um par simétrico que não é mais o espelho do
    /// outro (andou ou mudou de tamanho sozinho), ou um elemento que sai do plano de simetria da peça (o
    /// corpo, quando só uma aresta anda).
    /// </summary>
    private static IReadOnlyList<Requirement> Requirements(IReadOnlyList<FactChange> perUnit, ModelRelations relations)
    {
        var moved = perUnit.Where(c => c.Moves).ToDictionary(c => c.Name, c => c.Translation, StringComparer.Ordinal);
        var changed = perUnit.Where(c => c.Kind is not (FactChangeKind.Appeared or FactChangeKind.Vanished))
            .ToDictionary(c => c.Name, c => (c.Translation, Size: c.SizeChange), StringComparer.Ordinal);
        var result = new List<Requirement>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var centered = new HashSet<string>(StringComparer.Ordinal);

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
                // O espelho num plano do referencial inverte o deslocamento na normal e mantém o tamanho.
                // Quem mudou é o modelo; o outro precisa fazer a diferença.
                bool aChanged = changed.TryGetValue(pair.A.Name, out var a);
                bool bChanged = changed.TryGetValue(pair.B.Name, out var b);
                if (!aChanged && !bChanged) continue;

                var (mover, other, m, o) = aChanged ? (pair.A, pair.B, a, b) : (pair.B, pair.A, b, a);
                var mirrored = m.Translation - normal * (2 * m.Translation.DotProduct(normal));
                var translation = mirrored - o.Translation;
                var size = m.Size - o.Size;
                if (translation.Length < MotionTolerance && size.Length < MotionTolerance) continue;

                if (claimed.Add(other.Name))
                {
                    result.Add(new Requirement(new LeverRelation(LeverRelationKind.Symmetry, other.Name, mover.Name, plane.Normal), translation, size));
                }
            }

            // O que estava sobre o plano de uma peça simétrica precisa continuar nele. Só o plano da peça
            // inteira: num plano local (um grupo de furos), o que calha de estar no meio não é uma regra.
            if (!plane.IsGlobal) continue;
            foreach (var fact in plane.OnPlane)
            {
                if (!moved.TryGetValue(fact.Name, out var translation)) continue;
                double across = translation.DotProduct(normal);
                if (Math.Abs(across) < MotionTolerance || !centered.Add(fact.Name)) continue;

                result.Add(new Requirement(new LeverRelation(LeverRelationKind.Centered, fact.Name, PlaneText(plane), plane.Normal), normal * -across));
            }
        }

        return result;
    }

    /// <summary>"x = 0".</summary>
    private static string PlaneText(SymmetryPlane plane) =>
        $"{plane.Normal.ToString().ToLowerInvariant()} = {plane.Coordinate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>Um jeito de mover: um número, ou os números de uma aresta inteira, com o efeito medido.</summary>
    private sealed record Mover(IReadOnlyList<CodeLever> Levers, LeverEffect Effect);

    /// <summary>O que toda busca de grupo usa da mesma pergunta.</summary>
    private sealed record GroupContext(
        LeverStudy Study,
        Func<IReadOnlyList<PrimitiveFact>, double?> Measure,
        double BaseValue,
        IReadOnlyList<Func<IReadOnlyList<PrimitiveFact>, double?>> Watched,
        IReadOnlyList<double?> WatchedBase,
        IReadOnlyList<Mover> Companions);

    /// <summary>
    /// O grupo de <paramref name="primary"/> com, para cada elemento que precisa acompanhar, o companheiro
    /// (um número ou uma aresta) que o move na direção certa e mexe em menos coisas. O grupo é executado de
    /// verdade: a taxa e o que muda vêm dessa execução, e o que ela ainda quebra fica em
    /// <see cref="LeverOption.Breaks"/> (um elemento sem companheiro, ou um que o companheiro tirou do
    /// lugar). <c>null</c> se nenhum elemento tiver companheiro.
    /// </summary>
    /// <param name="runs">A aresta de cada número que é ponto de uma.</param>
    private static LeverOption? Group(
        GroupContext context,
        Mover primary,
        IReadOnlyList<Requirement> requirements,
        IReadOnlyDictionary<int, IReadOnlyList<CodeLever>> runs)
    {
        var shares = new List<(Mover Mover, double Weight)> { (primary, 1) };

        foreach (var requirement in requirements)
        {
            if (Companions(context.Companions, primary, requirement) is not { } best) continue; // fica quebrada; a execução abaixo a acusa

            foreach (var (mover, weight) in best)
            {
                int existing = shares.FindIndex(s => ReferenceEquals(s.Mover, mover));
                if (existing < 0)
                {
                    shares.Add((mover, weight));
                }
                else if (Math.Abs(shares[existing].Weight - weight) > 1e-6 * Math.Max(1, Math.Abs(weight)))
                {
                    return null; // o mesmo número precisaria andar duas medidas diferentes
                }
            }
        }
        if (shares.Count == 1) return null;

        var study = context.Study;
        double step = primary.Effect.Step;
        var values = new Dictionary<int, double>();
        foreach (var (mover, weight) in shares)
        {
            foreach (var lever in mover.Levers) values[lever.Id] = lever.Value + weight * step;
        }
        if (study.Evaluate(values) is not { } after || context.Measure(after) is not { } value) return null;

        double rate = (value - context.BaseValue) / step;
        if (Math.Abs(rate) < RateTolerance) return null;

        var changes = Scale(FactMotion.Compare(study.Baseline, after), 1 / step);
        var breaks = Requirements(changes, study.Relations).Select(r => r.Relation).ToList();
        if (primary.Levers.Count == 1 && runs.TryGetValue(primary.Levers[0].Id, out var run)) breaks.Add(Alignment(run));

        var keeps = requirements.Select(r => r.Relation)
            .Where(k => !breaks.Any(b => b.Kind == k.Kind && b.Fact == k.Fact))
            .Concat(shares.Where(s => s.Mover.Levers.Count > 1).Select(s => Alignment(s.Mover.Levers)))
            .ToList();
        if (keeps.Count == 0) return null;

        var (caveat, gain) = CaveatOf(shares.SelectMany(s => s.Mover.Levers).ToList(), shares.Select(s => s.Mover.Effect).ToList());
        return new LeverOption(
            shares.SelectMany(s => s.Mover.Levers.Select(l => new LeverShare(l, s.Weight))).ToList(),
            rate,
            changes,
            keeps,
            breaks,
            CountChanged(context.Watched, context.WatchedBase, after))
        {
            Caveat = caveat,
            Gain = gain
        };
    }

    /// <summary>Os mesmos números, andando na mesma proporção entre si (o sinal e a escala não importam).</summary>
    private static bool SameMotion(LeverOption a, LeverOption b)
    {
        if (a.Shares.Count != b.Shares.Count) return false;
        var weights = a.Shares.ToDictionary(s => s.Lever.Id, s => s.Weight);
        if (b.Shares.Any(s => !weights.ContainsKey(s.Lever.Id))) return false;

        double k = b.Shares[0].Weight / weights[b.Shares[0].Lever.Id];
        return b.Shares.All(s => Math.Abs(s.Weight - k * weights[s.Lever.Id]) <= 1e-6 * Math.Max(1, Math.Abs(s.Weight)));
    }

    /// <summary>
    /// Quem faz o elemento de <paramref name="requirement"/> cumprir o pedido, e quanto cada um anda: um
    /// companheiro, ou dois quando nenhum sozinho serve (a caixa espelhada precisa da largura e do canto
    /// para crescer para o lado de fora). Entre os que servem, o que mexe em menos coisas. <c>null</c> se
    /// ninguém serve.
    /// </summary>
    private static IReadOnlyList<(Mover Mover, double Weight)>? Companions(IReadOnlyList<Mover> companions, Mover primary, Requirement requirement)
    {
        var wanted = Wanted(requirement);
        var able = new List<(Mover Mover, double[] Does)>();
        foreach (var candidate in companions)
        {
            if (candidate.Levers.Any(l => primary.Levers.Any(p => p.Id == l.Id))) continue;
            var change = candidate.Effect.Changes.FirstOrDefault(c => c.Name == requirement.Relation.Fact
                && c.Kind is not (FactChangeKind.Appeared or FactChangeKind.Vanished));
            if (change != null) able.Add((candidate, Does(change, candidate.Effect.Step, requirement)));
        }

        IReadOnlyList<(Mover, double)>? best = null;
        int bestCost = int.MaxValue;
        foreach (var (mover, does) in able)
        {
            if (Fit([does], wanted) is not { } w || mover.Effect.Changes.Count >= bestCost) continue;
            best = [(mover, w[0])];
            bestCost = mover.Effect.Changes.Count;
        }
        if (best != null) return best;

        for (int i = 0; i < able.Count; i++)
        {
            for (int j = i + 1; j < able.Count; j++)
            {
                int cost = able[i].Mover.Effect.Changes.Count + able[j].Mover.Effect.Changes.Count;
                if (cost >= bestCost || Fit([able[i].Does, able[j].Does], wanted) is not { } w) continue;
                best = [(able[i].Mover, w[0]), (able[j].Mover, w[1])];
                bestCost = cost;
            }
        }
        return best;
    }

    /// <summary>O pedido como vetor: o deslocamento e, quando ele conta, o tamanho.</summary>
    private static double[] Wanted(Requirement requirement)
    {
        var t = requirement.Translation;
        return requirement.Size is { } size ? [t.X, t.Y, t.Z, size.X, size.Y, size.Z] : [t.X, t.Y, t.Z];
    }

    /// <summary>O que o companheiro faz com o elemento, por unidade, no mesmo formato de <see cref="Wanted"/>.</summary>
    private static double[] Does(FactChange change, double step, Requirement requirement)
    {
        var t = change.Translation * (1 / step);
        if (requirement.Size is null) return [t.X, t.Y, t.Z];
        var s = change.SizeChange * (1 / step);
        return [t.X, t.Y, t.Z, s.X, s.Y, s.Z];
    }

    /// <summary>
    /// Os pesos que somam <paramref name="parts"/> (um ou dois) em <paramref name="wanted"/>, por mínimos
    /// quadrados, ou <c>null</c> se a soma não chega lá (o que sobra passa da tolerância) ou se um deles não
    /// ajuda (peso zero, ou os dois fazem a mesma coisa).
    /// </summary>
    private static double[]? Fit(IReadOnlyList<double[]> parts, double[] wanted)
    {
        double ww = Dot(wanted, wanted);
        if (ww < MotionTolerance * MotionTolerance) return null;

        double[] weights;
        if (parts.Count == 1)
        {
            double gg = Dot(parts[0], parts[0]);
            if (gg < MotionTolerance * MotionTolerance) return null;
            weights = [Dot(parts[0], wanted) / gg];
        }
        else
        {
            double a = Dot(parts[0], parts[0]), b = Dot(parts[0], parts[1]), c = Dot(parts[1], parts[1]);
            double det = a * c - b * b;
            if (det <= ParallelTolerance * a * c) return null; // os dois fazem (quase) a mesma coisa
            double p = Dot(parts[0], wanted), q = Dot(parts[1], wanted);
            weights = [(c * p - b * q) / det, (a * q - b * p) / det];
        }
        if (weights.Any(w => Math.Abs(w) < 1e-9)) return null;

        double residual = 0;
        for (int i = 0; i < wanted.Length; i++)
        {
            double sum = 0;
            for (int k = 0; k < parts.Count; k++) sum += weights[k] * parts[k][i];
            residual += (wanted[i] - sum) * (wanted[i] - sum);
        }
        return residual <= ParallelTolerance * ParallelTolerance * ww ? weights : null;
    }

    private static double Dot(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
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
}
