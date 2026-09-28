using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AvaloniaCadEditor.Application.ReferenceGuides;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ReferenceGuides;

namespace AvaloniaCadEditor.UI.ReferenceGuides;

/// <summary>
/// UI-facing state for the reference-guide feature. Deliberately its own view model
/// (rather than more properties on <see cref="AvaloniaCadEditor.UI.ViewModels.CadViewModel"/>)
/// so the whole feature - model, service, view model, render pass, overlay - lives in one
/// place and can be lifted out cleanly.
///
/// The analysis never runs on its own: it only runs when <see cref="AnalyzeCommand"/> is
/// invoked (including the first time <see cref="IsVisible"/> is turned on). Scene changes
/// only flip <see cref="IsStale"/> via <see cref="MarkStale"/> - cheap, no recomputation -
/// so a busy editing session never pays the analysis cost until the user actually asks for it.
/// </summary>
public sealed class ReferenceGuidesViewModel : INotifyPropertyChanged
{
    private readonly IReferenceGuideService _service;
    private readonly Func<IReadOnlyList<IFeature>> _snapshotFeatures;
    private readonly ReferenceGuideOptions _options;
    private CancellationTokenSource? _cts;

    public ReferenceGuidesViewModel(
        IReferenceGuideService service,
        Func<IReadOnlyList<IFeature>> snapshotFeatures,
        ReferenceGuideOptions? options = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _snapshotFeatures = snapshotFeatures ?? throw new ArgumentNullException(nameof(snapshotFeatures));
        _options = options ?? new ReferenceGuideOptions();
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsAnalyzing);
    }

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
            if (_isVisible && Result == null && !IsAnalyzing)
            {
                _ = AnalyzeCommand.ExecuteAsync();
            }
        }
    }

    private ReferenceGuideSet? _result;
    public ReferenceGuideSet? Result
    {
        get => _result;
        private set { _result = value; OnPropertyChanged(); }
    }

    private bool _isStale;
    /// <summary>True once the scene has changed since the last analysis. The last result
    /// keeps being shown (dimmed by the caller if desired) until the user re-analyzes.</summary>
    public bool IsStale
    {
        get => _isStale;
        private set
        {
            if (_isStale == value) return;
            _isStale = value;
            OnPropertyChanged();
        }
    }

    private bool _isAnalyzing;
    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (_isAnalyzing == value) return;
            _isAnalyzing = value;
            OnPropertyChanged();
            AnalyzeCommand.RaiseCanExecuteChanged();
        }
    }

    public AsyncRelayCommand AnalyzeCommand { get; }

    /// <summary>Call after any scene rebuild. O(1): only raises a flag, never recomputes.</summary>
    public void MarkStale()
    {
        if (Result != null) IsStale = true;
    }

    private async Task AnalyzeAsync()
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        IsAnalyzing = true;
        try
        {
            var snapshot = _snapshotFeatures();
            var result = await Task.Run(() => _service.Analyze(snapshot, _options, cts.Token), cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested) return;

            Result = result;
            IsStale = false;
        }
        catch (OperationCanceledException)
        {
            // A newer analysis request superseded this one; it owns IsAnalyzing now.
        }
        finally
        {
            if (!cts.IsCancellationRequested) IsAnalyzing = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Minimal async ICommand, kept local to this feature to avoid changing the shared
/// <see cref="AvaloniaCadEditor.UI.ViewModels.RelayCommand"/>.</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

    public async void Execute(object? parameter) => await ExecuteAsync();

    public Task ExecuteAsync() => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
