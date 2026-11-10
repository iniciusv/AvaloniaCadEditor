using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Levers;

/// <summary>
/// O efeito de um número sobre a peça: os fatos com ele acrescido de <see cref="Step"/>, e o que mudou.
/// </summary>
public sealed record LeverEffect(CodeLever Lever, double Step, IReadOnlyList<PrimitiveFact> Facts, IReadOnlyList<FactChange> Changes);

/// <summary>
/// O estudo de uma peça: os fatos como estão no código e, para cada número que a sonda sabe trocar, o que
/// acontece quando ele muda um pouco. Uma execução da cena por número, feita uma vez; depois disso as
/// perguntas sobre qualquer cota da peça (<see cref="LeverOptionFinder"/>) só leem o estudo.
///
/// É dono da sonda: descartar o estudo descarrega a cena compilada.
/// </summary>
public sealed class LeverStudy : IDisposable
{
    private readonly ILeverProbe _probe;
    private readonly string _subjectName;
    private readonly Placement _worldToFrame;
    private readonly Lazy<ModelRelations> _relations;

    private LeverStudy(ILeverProbe probe, string subjectName, Placement worldToFrame, IReadOnlyList<PrimitiveFact> baseline)
    {
        _probe = probe;
        _subjectName = subjectName;
        _worldToFrame = worldToFrame;
        Baseline = baseline;
        _relations = new Lazy<ModelRelations>(() => RelationAnalyzer.Analyze(Baseline));
    }

    /// <summary>Os fatos da peça como estão no código, no referencial da folha.</summary>
    public IReadOnlyList<PrimitiveFact> Baseline { get; }

    /// <summary>Só os números que mudam alguma coisa na peça estudada.</summary>
    public IReadOnlyList<LeverEffect> Effects { get; private set; } = Array.Empty<LeverEffect>();

    /// <summary>Relações da peça como está (coaxiais, simetrias), calculadas na primeira pergunta.</summary>
    public ModelRelations Relations => _relations.Value;

    /// <summary>
    /// Estuda a feature de topo <paramref name="subjectName"/> (a peça isolada). <c>null</c> se a cena não
    /// executar ou a peça não estiver nela; nesse caso a sonda é descartada aqui.
    /// </summary>
    /// <param name="worldToFrame">Leva o mundo para o referencial da folha (o mesmo das cotas).</param>
    public static LeverStudy? Run(ILeverProbe probe, string subjectName, Placement worldToFrame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrEmpty(subjectName);
        ArgumentNullException.ThrowIfNull(worldToFrame);

        var baseline = Facts(probe, subjectName, worldToFrame, new Dictionary<int, double>());
        if (baseline == null)
        {
            probe.Dispose();
            return null;
        }

        var study = new LeverStudy(probe, subjectName, worldToFrame, baseline);
        var effects = new List<LeverEffect>();
        foreach (var lever in probe.Levers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double step = StepFor(lever.Value);
            var facts = study.Evaluate(new Dictionary<int, double> { [lever.Id] = lever.Value + step });
            if (facts == null) continue;

            var changes = FactMotion.Compare(baseline, facts);
            if (changes.Count > 0) effects.Add(new LeverEffect(lever, step, facts, changes));
        }
        study.Effects = effects;
        return study;
    }

    /// <summary>
    /// Os fatos da peça com os valores dados (por <see cref="CodeLever.Id"/>, valores absolutos; os que
    /// faltam ficam como no código). <c>null</c> se a execução falhar ou a peça sumir.
    /// </summary>
    public IReadOnlyList<PrimitiveFact>? Evaluate(IReadOnlyDictionary<int, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Facts(_probe, _subjectName, _worldToFrame, values);
    }

    private static IReadOnlyList<PrimitiveFact>? Facts(ILeverProbe probe, string subjectName, Placement worldToFrame, IReadOnlyDictionary<int, double> values)
    {
        var subject = probe.Run(values)?.FirstOrDefault(f => f.Name == subjectName);
        return subject == null ? null : FactExtractor.Extract([AnalysisSubject.For(subject, worldToFrame)]);
    }

    /// <summary>O passo da sondagem: pequeno, para a taxa valer em torno do valor atual, e proporcional,
    /// para não sumir em números grandes.</summary>
    public static double StepFor(double value) => Math.Max(1e-3, Math.Abs(value) * 1e-3);

    public void Dispose() => _probe.Dispose();
}
