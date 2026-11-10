using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Levers;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Drafting;

/// <summary>
/// A cota selecionada no desenho: o que ela mede, os números do código que a mudam (com o que cada um
/// arrasta) e a aplicação de um valor novo. Só coordena: quem mede é o <see cref="DimensionGauge"/>, quem
/// sonda é o <see cref="LeverStudy"/>, quem escolhe as opções é o <see cref="LeverOptionFinder"/>, quem
/// calcula os valores é o <see cref="LeverSolver"/> e quem escreve o fonte é o <see cref="LeverSourceEditor"/>
/// mais o editor de script (Ctrl+Z).
///
/// O estudo (uma execução da cena por número) roda em segundo plano, uma vez por versão do código e por
/// peça; mudar de cota na mesma peça só refaz a escolha das opções.
/// </summary>
public sealed class DimensionLeversViewModel : INotifyPropertyChanged
{
    private readonly CadViewModel _cad;
    private readonly ILeverProbeFactory? _factory;

    private DraftingSheet? _sheet;
    private IReadOnlyList<DraftDimension> _dimensions = Array.Empty<DraftDimension>();
    private string? _selectedKey;
    private DimensionGauge? _gauge;
    private StudyHandle? _study;
    private int _generation;
    private IReadOnlyList<LeverOption> _found = Array.Empty<LeverOption>();

    /// <param name="factory">Sem sonda (testes, composição sem Roslyn), a cota ainda é selecionada e destacada,
    /// mas não lista números.</param>
    public DimensionLeversViewModel(CadViewModel cad, ILeverProbeFactory? factory)
    {
        _cad = cad ?? throw new ArgumentNullException(nameof(cad));
        _factory = factory;
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(), () => SelectedOption != null && !IsBusy);
        ClearCommand = new RelayCommand(() => Clear());
        ToggleIndirectCommand = new RelayCommand(() => ShowIndirect = !ShowIndirect);
        Options.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOptions));
    }

    /// <summary>Disparado quando o destaque muda (seleção, opção escolhida).</summary>
    public event Action? Changed;

    public ICommand ApplyCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ToggleIndirectCommand { get; }

    public DraftDimension? Selected { get; private set; }

    public bool HasSelection => Selected != null;

    /// <summary>"Cota 10".</summary>
    public string Title => Selected == null ? "" : $"Cota {Selected.Text}";

    /// <summary>"mede corpo → furo_a".</summary>
    public string MeasuredText => MeasuredSources.Count == 0 ? "" : "mede " + string.Join(" → ", MeasuredSources);

    /// <summary>As primitivas que a cota mede (destacadas no desenho).</summary>
    public IReadOnlyList<string> MeasuredSources { get; private set; } = Array.Empty<string>();

    /// <summary>O que a opção escolhida move ou redimensiona (destacado no desenho).</summary>
    public IReadOnlyList<string> AffectedSources =>
        SelectedOption == null ? Array.Empty<string>() : SelectedOption.Option.Changes.Select(c => c.Name).ToList();

    /// <summary>
    /// As opções à vista: as medidas e, a pedido (<see cref="ShowIndirect"/>) ou quando não há medida
    /// nenhuma, as indiretas (<see cref="LeverOption.IsDirect"/>).
    /// </summary>
    public ObservableCollection<LeverOptionItem> Options { get; } = new();

    public bool HasOptions => Options.Count > 0;

    private bool _showIndirect;

    /// <summary>Mostra também os números que mudam a cota por tabela: divisores, direções, multiplicadores.</summary>
    public bool ShowIndirect
    {
        get => _showIndirect;
        set
        {
            if (_showIndirect == value) return;
            _showIndirect = value;
            OnPropertyChanged();
            RebuildOptions(keep: SelectedOption?.Option);
        }
    }

    /// <summary>Quantas opções indiretas a cota tem, à vista ou não.</summary>
    public int IndirectCount => _found.Count(o => !o.IsDirect);

    /// <summary>Há indiretas escondidas atrás das medidas. Sem medida nenhuma elas já aparecem, e não há o que alternar.</summary>
    public bool CanToggleIndirect => IndirectCount > 0 && _found.Any(o => o.IsDirect);

    public string IndirectToggleText => ShowIndirect
        ? "Esconder as indiretas"
        : IndirectCount == 1 ? "+ 1 indireta (fator, direção ou multiplicador)" : $"+ {IndirectCount} indiretas (fatores, direções, multiplicadores)";

    private LeverOptionItem? _selectedOption;

    public LeverOptionItem? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (ReferenceEquals(_selectedOption, value)) return;
            _selectedOption = value;
            OnPropertyChanged();
            RaiseCanExecute();
            RaiseChanged();
        }
    }

    private string _targetText = "";

    /// <summary>O valor novo da cota, como o usuário digita ("12,5" ou "12.5").</summary>
    public string TargetText
    {
        get => _targetText;
        set { if (_targetText != value) { _targetText = value; OnPropertyChanged(); } }
    }

    private string _status = "";

    public string Status
    {
        get => _status;
        private set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); RaiseCanExecute(); } }
    }

    // ------------------------------------------------------------------ Entrada do desenho

    /// <summary>A folha mudou (outra vista, outra execução, cotas escondidas): reencontra a cota pela chave.</summary>
    public void Update(DraftingSheet? sheet, IReadOnlyList<DraftDimension> dimensions)
    {
        _sheet = sheet;
        _dimensions = dimensions ?? Array.Empty<DraftDimension>();
        if (sheet == null) DropStudy();

        if (_selectedKey == null) return;
        var again = _dimensions.FirstOrDefault(d => d.Key == _selectedKey);
        if (again == null) Clear();
        else Select(again);
    }

    /// <summary>Clique no texto da cota: seleciona, ou tira a seleção se já era ela.</summary>
    public void Toggle(DraftDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        if (dimension.Key == _selectedKey) Clear();
        else Select(dimension);
    }

    /// <summary>Tira a seleção. Devolve false se não havia nada selecionado (o ESC segue a cascata).</summary>
    public bool Clear()
    {
        if (_selectedKey == null) return false;
        _generation++;
        _selectedKey = null;
        _gauge = null;
        Selected = null;
        MeasuredSources = Array.Empty<string>();
        _showIndirect = false;
        OnPropertyChanged(nameof(ShowIndirect));
        ShowOptions(Array.Empty<LeverOption>());
        Status = "";
        IsBusy = false;
        RaiseSelectionChanged();
        return true;
    }

    private void Select(DraftDimension dimension)
    {
        bool sameDimension = dimension.Key == _selectedKey;
        _selectedKey = dimension.Key;
        Selected = dimension;
        _gauge = _sheet == null ? null : DimensionGauge.For(_sheet, dimension);
        MeasuredSources = _gauge?.MeasuredSources ?? dimension.SourceNames;
        if (!sameDimension || string.IsNullOrWhiteSpace(TargetText)) TargetText = DimensionFactory.Format(dimension.Value);
        RaiseSelectionChanged();
        _ = RefreshOptionsAsync();
    }

    // ------------------------------------------------------------------ Opções

    private async Task RefreshOptionsAsync()
    {
        int generation = ++_generation;
        ShowOptions(Array.Empty<LeverOption>());

        if (_gauge == null)
        {
            Status = "Esta cota não tem referências que se possam remedir.";
            return;
        }
        if (_factory == null) return;
        if (CurrentTarget() is not { } target)
        {
            Status = "Por enquanto, só peças (Part) isoladas com H.";
            return;
        }

        IsBusy = true;
        Status = "Procurando os números do código que mudam esta cota…";
        var gauge = _gauge;
        var watched = _dimensions
            .Where(d => d.Key != _selectedKey && _sheet != null)
            .Select(d => DimensionGauge.For(_sheet!, d))
            .Where(g => g != null)
            .Select(g => (Func<IReadOnlyList<PrimitiveFact>, double?>)g!.Measure)
            .ToList();

        IReadOnlyList<LeverOption> options;
        try
        {
            var study = await Study(target);
            if (generation != _generation) return;
            if (study == null)
            {
                Status = "Não consegui executar a cena para sondar os números.";
                IsBusy = false;
                return;
            }
            options = await Task.Run(() => LeverOptionFinder.Find(study, gauge.Measure, watched));
        }
        catch (OperationCanceledException)
        {
            // O estudo foi descartado (DropStudy): quem o descartou já cuida do estado novo.
            if (generation == _generation)
            {
                Status = "";
                IsBusy = false;
            }
            return;
        }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            Status = $"✗ {ex.Message}";
            IsBusy = false;
            return;
        }

        if (generation != _generation) return;
        ShowOptions(options);
        Status = options.Count == 0
            ? "Nenhum número do código muda esta cota (ela pode vir de uma conta ou de outra peça)."
            : options.Any(o => o.IsDirect)
                ? "Escolha como mudar e digite o valor novo."
                : "Nenhum número é uma medida desta cota. Os de baixo a mudam por tabela: veja o aviso de cada um antes de escolher.";
        IsBusy = false;
    }

    private void ShowOptions(IReadOnlyList<LeverOption> found)
    {
        _found = found;
        RebuildOptions(keep: null);
    }

    /// <summary>
    /// Refaz a lista à vista a partir do que foi achado. Só uma medida vem escolhida: uma indireta (o
    /// divisor de <c>passo / 2</c>, o sinal de um espelho) o usuário escolhe ele mesmo, sabendo o que é,
    /// porque o Enter aplica a opção escolhida.
    /// </summary>
    private void RebuildOptions(LeverOption? keep)
    {
        bool anyDirect = _found.Any(o => o.IsDirect);
        var visible = _found.Where(o => o.IsDirect || ShowIndirect || !anyDirect).ToList();

        Options.Clear();
        foreach (var option in visible) Options.Add(new LeverOptionItem(option));
        SelectedOption = Options.FirstOrDefault(i => ReferenceEquals(i.Option, keep))
            ?? Options.FirstOrDefault(i => i.Option.IsDirect);

        OnPropertyChanged(nameof(IndirectCount));
        OnPropertyChanged(nameof(CanToggleIndirect));
        OnPropertyChanged(nameof(IndirectToggleText));
    }

    // ------------------------------------------------------------------ Aplicar

    private async Task ApplyAsync()
    {
        if (SelectedOption is not { } item || _gauge is not { } gauge || Selected is not { } dimension) return;
        if (!TryParse(TargetText, out double target))
        {
            Status = $"✗ \"{TargetText}\" não é um número.";
            return;
        }
        if (CurrentTarget() is not { } studyTarget || _cad.ScriptEditor == null) return;

        int generation = _generation;
        IsBusy = true;
        Status = "Calculando os valores novos…";
        try
        {
            var study = await Study(studyTarget);
            if (generation != _generation) return;
            if (study == null)
            {
                Status = "✗ Não consegui executar a cena para sondar os números.";
                return;
            }

            var solution = await Task.Run(() => LeverSolver.Solve(item.Option, target, study.Evaluate, gauge.Measure));
            if (generation != _generation) return;
            if (solution == null)
            {
                Status = $"✗ Esta opção não chega a {DimensionFactory.Format(target)} (a peça recusa o valor ou a medida não responde).";
                return;
            }

            var changes = LeverSourceEditor.Apply(_cad.ScriptEditor.GetSceneDocuments(), solution.Changes);
            if (changes == null)
            {
                DropStudy();
                Status = "✗ O código mudou desde a análise. Selecione a cota de novo.";
                return;
            }

            _cad.StatusText = $"✓ Cota {dimension.Text} → {DimensionFactory.Format(solution.Achieved)}: " +
                string.Join(", ", solution.Changes.Select(c => $"{c.Lever.Label} = {c.Text}"));
            TargetText = DimensionFactory.Format(target);
            await _cad.ScriptEditor.ApplySourceEditAsync(changes);
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation) Status = "";
        }
        catch (Exception ex)
        {
            Status = $"✗ {ex.Message}";
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
        }
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // ------------------------------------------------------------------ Estudo

    /// <summary>O que identifica um estudo: a peça isolada, o referencial e o código naquele momento.</summary>
    private sealed record StudyTarget(string SubjectName, string PartType, Placement WorldToFrame, IReadOnlyList<ScriptDocument> Documents);

    /// <param name="Cancellation">Para o estudo quando ele deixa de servir (ver <see cref="DropStudy"/>).</param>
    private sealed record StudyHandle(StudyTarget Target, Task<LeverStudy?> Task, CancellationTokenSource Cancellation);

    private StudyTarget? CurrentTarget()
    {
        if (_cad.IsolatedFeature is not PartInstance instance || _cad.DisplayFrame is not { } frame || _cad.ScriptEditor == null) return null;
        return new StudyTarget(instance.Name, instance.PartTypeName, frame.WorldToFrame, _cad.ScriptEditor.GetSceneDocuments());
    }

    private Task<LeverStudy?> Study(StudyTarget target)
    {
        if (_study != null && SameTarget(_study.Target, target)) return _study.Task;

        DropStudy();
        var factory = _factory!;
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var task = Task.Run(() =>
        {
            var probe = factory.Create(target.Documents, target.PartType, token);
            return probe == null ? null : LeverStudy.Run(probe, target.SubjectName, target.WorldToFrame, token);
        }, token);
        _study = new StudyHandle(target, task, cancellation);
        return task;
    }

    /// <summary>
    /// Descarta o estudo. Se ainda está rodando, é cancelado: sem isso ele seguiria até o fim em segundo
    /// plano (uma execução da cena por número) para um código que já mudou, e o <see cref="LeverStudy.Run"/>
    /// cancelado descarta a própria sonda. Se já terminou, a cena compilada é descarregada aqui.
    /// </summary>
    private void DropStudy()
    {
        var old = _study;
        _study = null;
        if (old == null) return;

        old.Cancellation.Cancel();
        old.Task.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) t.Result?.Dispose();
            old.Cancellation.Dispose();
        }, TaskScheduler.Default);
    }

    private static bool SameTarget(StudyTarget a, StudyTarget b) =>
        a.SubjectName == b.SubjectName && a.PartType == b.PartType &&
        a.Documents.Count == b.Documents.Count &&
        a.Documents.Zip(b.Documents).All(p => p.First.FilePath == p.Second.FilePath && p.First.Text == p.Second.Text);

    // ------------------------------------------------------------------ Notificação

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(MeasuredSources));
        OnPropertyChanged(nameof(MeasuredText));
        RaiseChanged();
    }

    private void RaiseCanExecute() => (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(AffectedSources));
        if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke();
        else Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Uma opção como aparece no painel: os números, o que se move e as relações.</summary>
public sealed class LeverOptionItem(LeverOption option)
{
    public LeverOption Option { get; } = option;

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
            if (Option.Moved.Count > 0) parts.Add("move " + string.Join(", ", Option.Moved));
            if (Option.Resized.Count > 0) parts.Add("muda o tamanho de " + string.Join(", ", Option.Resized));
            if (Math.Abs(Math.Abs(Option.Rate) - 1) > 1e-6)
            {
                parts.Add($"1 no número = {Option.Rate.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))} na cota");
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
            $"⚠ não é uma medida: 1 no número mexe {Option.Gain.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))} mm na peça (um multiplicador, um sinal ou um ângulo)",
        _ => ""
    };

    public bool HasCaveat => !Option.IsDirect;

    /// <summary>Onde está cada número ("Pecas/Bloco.cs:6"), para a dica.</summary>
    public string Where => string.Join("\n", Option.Shares.Select(s =>
        $"{s.Lever.Location.FilePath}:{s.Lever.Location.Line} ({KindText(s.Lever.Kind)})  {s.Lever.Location.LineText}"));

    private static string Describe(CodeLever lever) => $"{lever.Label} = {lever.Text}";

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
