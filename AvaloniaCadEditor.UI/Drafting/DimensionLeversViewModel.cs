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
using AvaloniaCadEditor.Application.EditContext;
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
/// <para>
/// Com uma opção escolhida e um valor digitado, a mudança é calculada antes de gravar
/// (<see cref="Preview"/>): que números mudam e para quanto, que outras cotas mudam junto, o que some, e
/// o contorno do que se mexe já no lugar novo (<see cref="GhostEntities"/>).
/// </para>
/// </summary>
public sealed class DimensionLeversViewModel : INotifyPropertyChanged, IDisposable
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
    private IReadOnlyList<(DraftDimension Dimension, DimensionGauge Gauge)> _others = Array.Empty<(DraftDimension, DimensionGauge)>();
    private LeverScope _scope = LeverScope.Single;

    /// <summary>A opção que o usuário escolheu, para reencontrá-la quando a lista é refeita (o código foi
    /// reexecutado, a vista mudou). Sem isso cada reexecução voltava para a primeira.</summary>
    private string? _chosenKey;

    /// <summary>A lista está sendo refeita: quem muda a opção escolhida é o app, não o usuário.</summary>
    private bool _rebuilding;

    private int _previewGeneration;

    /// <summary>
    /// A cota que acabou de receber um valor novo. A reexecução refaz a folha, e a cota pode voltar com outra
    /// chave (a distância entre dois furos simétricos deixa de ser "simétrica" quando só um anda) ou passar
    /// por uma folha vazia no meio do caminho. Na primeira folha depois de aplicar, ela é reencontrada pelo
    /// que mede e pelo valor, e continua selecionada com a opção que o usuário tinha escolhido.
    /// </summary>
    private sealed record FollowUp(string Key, DimensionKind Kind, IReadOnlyList<string> Sources, double Value, string? ChosenKey);

    private FollowUp? _followUp;

    /// <param name="factory">Sem sonda (testes, composição sem Roslyn), a cota ainda é selecionada e destacada,
    /// mas não lista números.</param>
    public DimensionLeversViewModel(CadViewModel cad, ILeverProbeFactory? factory)
    {
        _cad = cad ?? throw new ArgumentNullException(nameof(cad));
        _factory = factory;
        ApplyCommand = new RelayCommand(() => Pending = ApplyAsync(), () => SelectedOption != null && !IsBusy);
        ClearCommand = new RelayCommand(() => Clear());
        ToggleIndirectCommand = new RelayCommand(() => ShowIndirect = !ShowIndirect);
        ShowInCodeCommand = new RelayCommand(ShowInCode, () => SelectedOption != null);
        Options.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOptions));
    }

    /// <summary>Disparado quando o destaque muda (seleção, opção escolhida).</summary>
    public event Action? Changed;

    public ICommand ApplyCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ToggleIndirectCommand { get; }

    /// <summary>Seleciona no editor o número principal da opção escolhida.</summary>
    public ICommand ShowInCodeCommand { get; }

    /// <summary>
    /// O trabalho em segundo plano mais recente (a busca das opções ou a prévia). Quem precisa do resultado
    /// espera por ela; termina sempre sem lançar.
    /// </summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

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
            if (!_rebuilding && value != null) _chosenKey = value.Key;
            OnPropertyChanged();
            RaiseCanExecute();
            RaiseChanged();
            SchedulePreview();
        }
    }

    private string _targetText = "";

    /// <summary>O valor novo da cota, como o usuário digita ("12,5" ou "12.5").</summary>
    public string TargetText
    {
        get => _targetText;
        set
        {
            if (_targetText == value) return;
            _targetText = value;
            OnPropertyChanged();
            SchedulePreview();
        }
    }

    private LeverPreviewItem? _preview;

    /// <summary>
    /// O que aplicar faria, calculado numa execução da sonda antes de gravar qualquer coisa. <c>null</c>
    /// enquanto não há opção escolhida, o valor não é um número ou é o que a cota já mede.
    /// </summary>
    public LeverPreviewItem? Preview
    {
        get => _preview;
        private set
        {
            if (ReferenceEquals(_preview, value)) return;
            _preview = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreview));
            RaiseChanged();
        }
    }

    public bool HasPreview => Preview != null;

    /// <summary>O contorno do que a prévia move ou redimensiona, já no lugar novo (o fantasma do desenho).</summary>
    public IReadOnlyList<ViewEntity> GhostEntities => Preview?.Ghost ?? Array.Empty<ViewEntity>();

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

    /// <summary>
    /// A folha mudou (outra vista, outra execução, cotas escondidas): reencontra a cota pela chave ou, logo
    /// depois de aplicar um valor, pelo que ela mede (<see cref="FollowUp"/>).
    /// </summary>
    public void Update(DraftingSheet? sheet, IReadOnlyList<DraftDimension> dimensions)
    {
        _sheet = sheet;
        _dimensions = dimensions ?? Array.Empty<DraftDimension>();
        if (sheet == null) DropStudy();

        var again = _selectedKey == null ? null : _dimensions.FirstOrDefault(d => d.Key == _selectedKey);
        if (again == null && sheet != null && _followUp is { } follow)
        {
            again = _dimensions.FirstOrDefault(d => d.Key == follow.Key) ?? SameMeasure(sheet, follow);
            if (again != null)
            {
                // É a mesma cota: o Select não a trata como outra, e a escolha do usuário continua valendo.
                _selectedKey = again.Key;
                _chosenKey = follow.ChosenKey;
            }
        }
        if (sheet != null) _followUp = null;

        if (again != null) Select(again);
        else ClearSelection();
    }

    /// <summary>A cota da folha nova que mede as mesmas primitivas, do mesmo jeito, com o valor que foi aplicado.</summary>
    private DraftDimension? SameMeasure(DraftingSheet sheet, FollowUp follow) =>
        _dimensions.FirstOrDefault(d =>
            d.Kind == follow.Kind &&
            Math.Abs(d.Value - follow.Value) < 5e-3 &&
            DimensionGauge.For(sheet, d) is { } gauge &&
            gauge.MeasuredSources.Count == follow.Sources.Count &&
            gauge.MeasuredSources.All(follow.Sources.Contains));

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
        _followUp = null; // quem tira a seleção de propósito não a quer de volta na próxima folha
        return ClearSelection();
    }

    private bool ClearSelection()
    {
        if (_selectedKey == null) return false;
        _generation++;
        _selectedKey = null;
        _gauge = null;
        Selected = null;
        MeasuredSources = Array.Empty<string>();
        _showIndirect = false;
        _chosenKey = null;
        _others = Array.Empty<(DraftDimension, DimensionGauge)>();
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
        if (!sameDimension)
        {
            // Outra cota, outras opções: a escolha e as indiretas à vista eram da anterior.
            _chosenKey = null;
            if (_showIndirect)
            {
                _showIndirect = false;
                OnPropertyChanged(nameof(ShowIndirect));
            }
        }
        _selectedKey = dimension.Key;
        Selected = dimension;
        _gauge = _sheet == null ? null : DimensionGauge.For(_sheet, dimension);
        MeasuredSources = _gauge?.MeasuredSources ?? dimension.SourceNames;
        if (!sameDimension || string.IsNullOrWhiteSpace(TargetText)) TargetText = DimensionFactory.Format(dimension.Value);
        RaiseSelectionChanged();
        Pending = RefreshOptionsAsync();
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
        _others = _dimensions
            .Where(d => d.Key != _selectedKey && _sheet != null)
            .Select(d => (Dimension: d, Gauge: DimensionGauge.For(_sheet!, d)))
            .Where(o => o.Gauge != null)
            .Select(o => (o.Dimension, o.Gauge!))
            .ToList();
        var watched = _others.Select(o => (Func<IReadOnlyList<PrimitiveFact>, double?>)o.Gauge.Measure).ToList();
        _scope = target.Scope;

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

        _rebuilding = true;
        try
        {
            Options.Clear();
            foreach (var option in visible) Options.Add(new LeverOptionItem(option, _scope));
            SelectedOption = Options.FirstOrDefault(i => ReferenceEquals(i.Option, keep))
                ?? Options.FirstOrDefault(i => _chosenKey != null && i.Key == _chosenKey)
                ?? Options.FirstOrDefault(i => i.Option.IsDirect);
        }
        finally
        {
            _rebuilding = false;
        }

        OnPropertyChanged(nameof(IndirectCount));
        OnPropertyChanged(nameof(CanToggleIndirect));
        OnPropertyChanged(nameof(IndirectToggleText));
    }

    // ------------------------------------------------------------------ Prévia

    /// <summary>
    /// Recalcula a prévia para a opção e o valor de agora. Cada pedido invalida o anterior: quem digita
    /// "12,5" passa por "1", "12" e "12," e só o último resultado aparece.
    /// </summary>
    private void SchedulePreview()
    {
        int generation = ++_previewGeneration;
        Preview = null;

        if (SelectedOption is not { } item || _gauge is not { } gauge || Selected is not { } dimension || _sheet is not { } sheet) return;
        if (_study is not { Task.IsCompletedSuccessfully: true } handle || handle.Task.Result is not { } study) return;
        if (!TryParse(TargetText, out double target)) return;
        if (TargetText.Trim() == DimensionFactory.Format(dimension.Value) || Math.Abs(target - dimension.Value) < 1e-9) return;

        var others = _others;
        Pending = PreviewAsync(generation, item.Option, gauge, study, target, sheet.Basis, others);
    }

    private async Task PreviewAsync(int generation, LeverOption option, DimensionGauge gauge, LeverStudy study, double target,
        ViewBasis basis, IReadOnlyList<(DraftDimension Dimension, DimensionGauge Gauge)> others)
    {
        LeverPreviewItem? preview;
        try
        {
            preview = await Task.Run(() =>
            {
                var solution = LeverSolver.Solve(option, target, study.Evaluate, gauge.Measure);
                return LeverPreviewItem.Build(option, solution, target, study.Baseline, basis, others);
            });
        }
        catch (Exception)
        {
            // A prévia é um conforto: se a sonda falhar aqui, o Aplicar diz por quê.
            preview = null;
        }

        if (generation == _previewGeneration) Preview = preview;
    }

    private void ShowInCode()
    {
        if (SelectedOption is { } item) _cad.ScriptEditor?.RevealLocation(item.Option.Primary.Location);
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
            _followUp = new FollowUp(dimension.Key, dimension.Kind, gauge.MeasuredSources, solution.Achieved, _chosenKey ?? item.Key);
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
    private sealed record StudyTarget(string SubjectName, string PartType, Placement WorldToFrame, IReadOnlyList<ScriptDocument> Documents, LeverScope Scope);

    /// <param name="Cancellation">Para o estudo quando ele deixa de servir (ver <see cref="DropStudy"/>).</param>
    private sealed record StudyHandle(StudyTarget Target, Task<LeverStudy?> Task, CancellationTokenSource Cancellation);

    private StudyTarget? CurrentTarget()
    {
        if (_cad.IsolatedFeature is not PartInstance instance || _cad.DisplayFrame is not { } frame || _cad.ScriptEditor == null) return null;
        int instances = PartEditContext.For(instance, _cad.Sketch.Features)?.InstanceCount ?? 1;
        return new StudyTarget(instance.Name, instance.PartTypeName, frame.WorldToFrame, _cad.ScriptEditor.GetSceneDocuments(),
            new LeverScope(instance.PartTypeName, instances));
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
        if (old.Task.IsCompleted) Release(old);
        else old.Task.ContinueWith(_ => Release(old), TaskScheduler.Default);
    }

    private static void Release(StudyHandle handle)
    {
        if (handle.Task.IsCompletedSuccessfully) handle.Task.Result?.Dispose();
        handle.Cancellation.Dispose();
    }

    /// <summary>
    /// Tira a seleção e descarta o estudo: a cena compilada da sonda fica carregada enquanto o estudo
    /// existir, e ninguém mais o alcança depois que o ViewModel sai de uso.
    /// </summary>
    public void Dispose()
    {
        Clear();
        DropStudy();
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

    private void RaiseCanExecute()
    {
        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowInCodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(AffectedSources));
        OnPropertyChanged(nameof(GhostEntities));
        if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke();
        else Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
