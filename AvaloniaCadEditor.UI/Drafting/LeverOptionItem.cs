using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Application.Levers;
using AvaloniaCadEditor.Drafting;

namespace AvaloniaCadEditor.UI.Drafting;

/// <summary>Quem mais muda quando um número da peça muda: as outras instâncias dela na cena.</summary>
/// <param name="PartType">O record da peça isolada.</param>
/// <param name="InstanceCount">Quantas instâncias de topo da cena usam esse record (contando a isolada).</param>
public sealed record LeverScope(string PartType, int InstanceCount)
{
    public static readonly LeverScope Single = new("", 1);
}

/// <summary>Uma opção como aparece no painel: os números, o que se move, as relações e o alcance.</summary>
public sealed class LeverOptionItem(LeverOption option, LeverScope? scope = null)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Acima disto os nomes viram "a, b e mais N": o sinal de um espelho move a peça inteira.</summary>
    private const int MaxNames = 3;

    private readonly LeverScope _scope = scope ?? LeverScope.Single;

    public LeverOption Option { get; } = option;

    /// <summary>
    /// Identifica a opção entre uma busca e outra: os números, pelo rótulo, e quanto cada um anda. O
    /// <see cref="CodeLever.Id"/> não serve, porque só vale dentro da sonda que o criou.
    /// </summary>
    public string Key { get; } = string.Join("|", option.Shares.Select(s =>
        $"{s.Lever.Kind}:{s.Lever.Label}:{s.Weight.ToString("0.###", CultureInfo.InvariantCulture)}"));

    /// <summary>"furo_a · globalBaseCenter.x = 10".</summary>
    public string Title => Describe(Option.Primary);

    /// <summary>"+ porca_a · globalBaseCenter.x = 10" para os outros números do grupo.</summary>
    public string Companions => string.Join("\n", Option.Shares.Skip(1).Select(s => "+ " + Describe(s.Lever)));

    public bool HasCompanions => Option.IsGroup;

    /// <summary>"move furo_a, porca_a · muda mais 1 cota".</summary>
    public string Effect
    {
        get
        {
            var parts = new List<string>();
            if (Option.Moved.Count > 0) parts.Add("move " + Names(Option.Moved));
            if (Option.Resized.Count > 0) parts.Add("muda o tamanho de " + Names(Option.Resized));
            if (Math.Abs(Math.Abs(Option.Rate) - 1) > 1e-6)
            {
                parts.Add($"1 no número = {Option.Rate.ToString("0.##", PtBr)} na cota");
            }
            if (Option.OtherDimensionsChanged > 0)
            {
                parts.Add(Option.OtherDimensionsChanged == 1 ? "muda mais 1 cota" : $"muda mais {Option.OtherDimensionsChanged} cotas");
            }
            return string.Join(" · ", parts);
        }
    }

    public string Keeps => string.Join("\n", Option.Keeps.Select(r => "✓ " + Kept(r)));

    public bool HasKeeps => Option.Keeps.Count > 0;

    public string Breaks => string.Join("\n", Option.Breaks.Select(r => "⚠ " + Broken(r)));

    public bool HasBreaks => Option.Breaks.Count > 0;

    /// <summary>Por que a opção é indireta: o aviso que vai junto dela na lista.</summary>
    public string Caveat => Option.Caveat switch
    {
        LeverCaveat.Factor => "⚠ é o divisor de uma conta: muda a escala, e o código deixa de dizer a medida",
        LeverCaveat.Direction => "⚠ é componente de uma direção: inclina o elemento em vez de deslocá-lo",
        LeverCaveat.Scaled =>
            $"⚠ não é uma medida: 1 no número mexe {Option.Gain.ToString("0.##", PtBr)} mm na peça (um multiplicador, um sinal ou um ângulo)",
        _ => ""
    };

    public bool HasCaveat => !Option.IsDirect;

    /// <summary>
    /// A opção muda as outras instâncias da peça: um número do record (ou o padrão de um parâmetro) vale
    /// para todas. Vazio quando a peça só tem uma instância ou a opção só mexe em argumentos da cena.
    /// </summary>
    public string WideScope
    {
        get
        {
            if (_scope.InstanceCount <= 1) return "";
            if (Option.Shares.Any(s => s.Lever.Kind == CodeLeverKind.Literal))
            {
                return $"⚠ muda as {_scope.InstanceCount} instâncias de {_scope.PartType} na cena: o número é da peça";
            }
            var padrao = Option.Shares.FirstOrDefault(s => s.Lever.Kind == CodeLeverKind.Default);
            return padrao == null
                ? ""
                : $"⚠ muda toda instância de {_scope.PartType} que não passa {padrao.Lever.Owner} (são {_scope.InstanceCount} na cena)";
        }
    }

    public bool HasWideScope => WideScope.Length > 0;

    /// <summary>A opção só mexe no argumento desta instância, numa peça que tem outras.</summary>
    public string NarrowScope =>
        _scope.InstanceCount > 1 && Option.Shares.All(s => s.Lever.Kind == CodeLeverKind.Argument)
            ? $"só esta instância (há {_scope.InstanceCount} de {_scope.PartType} na cena)"
            : "";

    public bool HasNarrowScope => NarrowScope.Length > 0;

    /// <summary>Onde está cada número ("Pecas/Bloco.cs:6"), para a dica.</summary>
    public string Where => string.Join("\n", Option.Shares.Select(s =>
        $"{s.Lever.Location.FilePath}:{s.Lever.Location.Line} ({KindText(s.Lever.Kind)})  {s.Lever.Location.LineText}"));

    private static string Describe(CodeLever lever) => $"{lever.Label} = {lever.Text}";

    /// <summary>"a, b, c" ou, quando são muitos, "a, b e mais 20".</summary>
    internal static string Names(IReadOnlyList<string> names) => names.Count <= MaxNames
        ? string.Join(", ", names)
        : $"{string.Join(", ", names.Take(MaxNames - 1))} e mais {names.Count - (MaxNames - 1)}";

    private static string KindText(CodeLeverKind kind) => kind switch
    {
        CodeLeverKind.Literal => "literal da peça",
        CodeLeverKind.Argument => "argumento na cena",
        _ => "valor padrão do parâmetro"
    };

    private static string Kept(LeverRelation relation) => relation.Kind switch
    {
        LeverRelationKind.Coaxial => $"{relation.Fact} continua coaxial com {relation.Partner}",
        LeverRelationKind.Aligned => $"{relation.Fact} continuam alinhados em {relation.Partner}",
        _ => $"{relation.Fact} continua simétrico a {relation.Partner} em {relation.Axis}"
    };

    private static string Broken(LeverRelation relation) => relation.Kind switch
    {
        LeverRelationKind.Coaxial => $"{relation.Fact} deixa de ser coaxial com {relation.Partner}",
        LeverRelationKind.Aligned => $"{relation.Fact} deixam de estar alinhados em {relation.Partner}: a aresta entorta",
        _ => $"{relation.Fact} deixa de ser simétrico a {relation.Partner} em {relation.Axis}"
    };
}

/// <summary>
/// O que aplicar um valor faria, antes de gravar: os números que mudam, as outras cotas que mudam junto, o
/// que some ou aparece, e o contorno do que se mexe já no lugar novo. Imutável; montado por
/// <see cref="Build"/> a partir da solução da sonda, sem tocar no código.
/// </summary>
public sealed class LeverPreviewItem
{
    private LeverPreviewItem(LeverSolution? solution, string edits, string others, string warning, IReadOnlyList<ViewEntity> ghost)
    {
        Solution = solution;
        Edits = edits;
        Others = others;
        Warning = warning;
        Ghost = ghost;
    }

    /// <summary>Os valores que seriam gravados; <c>null</c> quando a opção não chega ao valor pedido.</summary>
    public LeverSolution? Solution { get; }

    /// <summary>"furo_a · globalBaseCenter.x: 10 → 12", um número por linha.</summary>
    public string Edits { get; }

    public bool HasEdits => Edits.Length > 0;

    /// <summary>"também muda: 18 → 16 · 28 → 26": as outras cotas da folha, com o valor de antes e o de depois.</summary>
    public string Others { get; }

    public bool HasOthers => Others.Length > 0;

    /// <summary>O que pede atenção: a opção não chega ao valor, um elemento some, a medida não fecha.</summary>
    public string Warning { get; }

    public bool HasWarning => Warning.Length > 0;

    /// <summary>As primitivas que mudam, projetadas na vista como ficam depois.</summary>
    public IReadOnlyList<ViewEntity> Ghost { get; }

    /// <param name="solution">O que <see cref="LeverSolver.Solve"/> devolveu; <c>null</c> se não chegou ao alvo.</param>
    /// <param name="baseline">Os fatos da peça como está (<see cref="LeverStudy.Baseline"/>).</param>
    /// <param name="basis">A vista da folha, para projetar o fantasma.</param>
    /// <param name="others">As demais cotas da folha, cada uma com o seu medidor.</param>
    public static LeverPreviewItem Build(
        LeverOption option,
        LeverSolution? solution,
        double target,
        IReadOnlyList<PrimitiveFact> baseline,
        ViewBasis basis,
        IReadOnlyList<(DraftDimension Dimension, DimensionGauge Gauge)> others)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(others);

        if (solution == null)
        {
            return new LeverPreviewItem(null, "", "",
                $"✗ esta opção não chega a {DimensionFactory.Format(target)}: a peça recusa o valor ou a medida para de responder",
                Array.Empty<ViewEntity>());
        }

        string edits = string.Join("\n", solution.Changes.Select(c => $"{c.Lever.Label}: {c.Lever.Text} → {c.Text}"));

        var changed = new List<string>();
        foreach (var (dimension, gauge) in others)
        {
            // A medida sobre os fatos de antes e de depois, e não o valor do desenho: a borda da peça é medida
            // pela caixa dos corpos, que pode diferir do contorno da malha por um chanfro.
            if (gauge.Measure(baseline) is not { } before || gauge.Measure(solution.Facts) is not { } after) continue;
            if (Math.Abs(after - before) > 5e-4) changed.Add($"{DimensionFactory.Format(before)} → {DimensionFactory.Format(after)}");
        }
        string othersText = changed.Count == 0 ? "" : "também muda: " + string.Join(" · ", changed);

        var motion = FactMotion.Compare(baseline, solution.Facts);
        var warnings = new List<string>();
        var vanished = motion.Where(c => c.Kind == FactChangeKind.Vanished).Select(c => c.Name).ToList();
        var appeared = motion.Where(c => c.Kind == FactChangeKind.Appeared).Select(c => c.Name).ToList();
        if (vanished.Count > 0) warnings.Add("⚠ some: " + LeverOptionItem.Names(vanished));
        if (appeared.Count > 0) warnings.Add("⚠ aparece: " + LeverOptionItem.Names(appeared));
        if (Math.Abs(solution.Achieved - target) > 5e-3)
        {
            warnings.Add($"⚠ a cota fica em {DimensionFactory.Format(solution.Achieved)}, não em {DimensionFactory.Format(target)}");
        }

        var moved = new HashSet<string>(motion.Where(c => c.Kind is not (FactChangeKind.Vanished or FactChangeKind.Appeared)).Select(c => c.Name), StringComparer.Ordinal);
        IReadOnlyList<ViewEntity> ghost = moved.Count == 0
            ? Array.Empty<ViewEntity>()
            : ViewProjector.Project(solution.Facts, basis).Entities.Where(e => moved.Contains(e.SourceName)).ToList();

        return new LeverPreviewItem(solution, edits, othersText, string.Join("\n", warnings), ghost);
    }
}
