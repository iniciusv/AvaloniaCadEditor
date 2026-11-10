using AvaloniaCadEditor.Analysis;

namespace AvaloniaCadEditor.Application.Levers;

/// <summary>Um número e o valor novo dele.</summary>
public sealed record LeverChange(CodeLever Lever, double NewValue)
{
    /// <summary>O texto que vai para o fonte.</summary>
    public string Text => Lever.Format(NewValue);
}

/// <summary>Os valores que levam a cota ao alvo, já arredondados como serão gravados.</summary>
/// <param name="Achieved">A medida conferida numa execução com os valores arredondados.</param>
public sealed record LeverSolution(IReadOnlyList<LeverChange> Changes, double Achieved, IReadOnlyList<PrimitiveFact> Facts);

/// <summary>
/// Acha quanto os números de uma <see cref="LeverOption"/> precisam andar para a medida chegar a um valor.
/// Começa pela taxa do estudo e corrige pela secante, conferindo cada passo numa execução: um <c>if</c> ou
/// um ângulo no código não enganam a resposta. Sem estado.
/// </summary>
public static class LeverSolver
{
    /// <param name="evaluate">Os fatos com valores absolutos por <see cref="CodeLever.Id"/> (<see cref="LeverStudy.Evaluate"/>).</param>
    /// <param name="measure">A medida sobre os fatos (a cota).</param>
    /// <param name="tolerance">Erro aceito na medida, em mm.</param>
    /// <returns><c>null</c> se a medida não chega ao alvo (a execução falha ou não converge).</returns>
    public static LeverSolution? Solve(
        LeverOption option,
        double target,
        Func<IReadOnlyDictionary<int, double>, IReadOnlyList<PrimitiveFact>?> evaluate,
        Func<IReadOnlyList<PrimitiveFact>, double?> measure,
        double tolerance = 1e-3,
        int maxIterations = 8)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentNullException.ThrowIfNull(measure);

        double? Error(double t) => measure(evaluate(ValuesAt(option, t)) ?? []) is { } value ? value - target : null;

        double t0 = 0;
        if (Error(t0) is not { } e0) return null;
        if (Math.Abs(e0) <= tolerance) return Finish(option, 0, evaluate, measure);

        double t1 = -e0 / option.Rate;
        for (int i = 0; i < maxIterations; i++)
        {
            if (Error(t1) is not { } e1) return null;
            if (Math.Abs(e1) <= tolerance) return Finish(option, t1, evaluate, measure);
            if (Math.Abs(e1 - e0) < 1e-12) return null;

            double next = t1 - e1 * (t1 - t0) / (e1 - e0);
            (t0, e0, t1) = (t1, e1, next);
        }
        return null;
    }

    private static Dictionary<int, double> ValuesAt(LeverOption option, double t) =>
        option.Shares.ToDictionary(s => s.Lever.Id, s => s.Lever.Value + s.Weight * t);

    private static LeverSolution? Finish(
        LeverOption option,
        double t,
        Func<IReadOnlyDictionary<int, double>, IReadOnlyList<PrimitiveFact>?> evaluate,
        Func<IReadOnlyList<PrimitiveFact>, double?> measure)
    {
        var changes = option.Shares
            .Select(s => new LeverChange(s.Lever, CodeLever.Round(s.Lever.Value + s.Weight * t)))
            .ToList();
        var facts = evaluate(changes.ToDictionary(c => c.Lever.Id, c => c.NewValue));
        if (facts == null || measure(facts) is not { } achieved) return null;
        return new LeverSolution(changes, achieved, facts);
    }
}
