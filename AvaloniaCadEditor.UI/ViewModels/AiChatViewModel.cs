using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Context;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>
/// ViewModel do painel de chat multi-round com a IA (DeepSeek) usado para gerar/editar
/// o CadScript da cena. Cada resposta com código válido pode ser aplicada como o script
/// vigente através de <see cref="AiChatMessageItem.ApplyCommand"/>.
/// A chave/modelo/URL da IA são editáveis pela própria UI (<see cref="ApiKeyInput"/> etc.)
/// e persistidos entre execuções via <see cref="IAiSettingsProvider"/>.
/// Com um projeto aberto, a IA recebe só os arquivos relevantes para a seleção atual
/// (<see cref="ContextFiles"/>, calculados por <see cref="IAiContextBuilder"/>) e pode alterar vários deles.
/// </summary>
public class AiChatViewModel : INotifyPropertyChanged
{
    private readonly CadViewModel _cadViewModel;
    private readonly SceneScriptViewModel _scriptEditor;
    private readonly ISceneEvaluator _evaluator;
    private readonly IAiSettingsProvider _settingsProvider;
    private CadScriptChatSession? _session;
    private CancellationTokenSource? _cts;

    private readonly IAiContextBuilder _contextBuilder;

    // Ajustes manuais do contexto: valem até a próxima "New chat" ou troca de projeto.
    private readonly AiContextAdjustments _contextAdjustments = new();
    private string? _contextProjectName;

    public ObservableCollection<AiChatMessageItem> Messages { get; } = new();

    /// <summary>Arquivos que irão, com o código completo, na próxima mensagem à IA.</summary>
    public ObservableCollection<AiContextFileChip> ContextFiles { get; } = new();

    /// <summary>Arquivos do projeto que podem ser adicionados ao contexto (os que estão fora dele).</summary>
    public ObservableCollection<string> AddableContextFiles { get; } = new();

    public bool HasContextFiles => ContextFiles.Count > 0;

    /// <summary>Escolher um item aqui fixa o arquivo no contexto (e o combo volta a ficar vazio).</summary>
    public string? FileToAdd
    {
        get => null;
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            _contextAdjustments.Pin(value);
            RefreshContextFiles();
            OnPropertyChanged();
        }
    }

    public bool IsAvailable => _session != null;

    private string? _configError;
    public string? ConfigError
    {
        get => _configError;
        private set { _configError = value; OnPropertyChanged(); }
    }

    private string _modelName = string.Empty;
    public string ModelName
    {
        get => _modelName;
        private set { _modelName = value; OnPropertyChanged(); }
    }

    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set { _isSettingsOpen = value; OnPropertyChanged(); }
    }

    private string _apiKeyInput = string.Empty;
    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set { _apiKeyInput = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Modelos DeepSeek atualmente disponíveis (ids exatos do campo "model" da API).
    /// Fonte: https://api-docs.deepseek.com/quick_start/pricing — V4.1 Flash e V4 Pro.
    /// </summary>
    public IReadOnlyList<AiModelOption> ModelOptions { get; } = new[]
    {
        new AiModelOption("DeepSeek V4 Pro", "deepseek-v4-pro"),
        new AiModelOption("DeepSeek V4.1 Flash", "deepseek-flash")
    };

    private string _modelInput = string.Empty;
    public string ModelInput
    {
        get => _modelInput;
        private set { _modelInput = value; OnPropertyChanged(); }
    }

    private AiModelOption _selectedModelOption;
    public AiModelOption SelectedModelOption
    {
        get => _selectedModelOption;
        set
        {
            _selectedModelOption = value;
            ModelInput = value.ModelId;
            OnPropertyChanged();
        }
    }

    private static AiModelOption ResolveModelOption(IReadOnlyList<AiModelOption> options, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return options[0];

        // "deepseek-v4-flash" é o nome legado (ainda aceito pela API, porém descontinuado);
        // hoje ele é servido pelo mesmo modelo do id atual "deepseek-flash".
        if (modelId.Equals("deepseek-v4-flash", StringComparison.OrdinalIgnoreCase))
        {
            return options.First(o => o.ModelId == "deepseek-flash");
        }

        return options.FirstOrDefault(o => o.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            ?? options[0];
    }

    private string _baseUrlInput = string.Empty;
    public string BaseUrlInput
    {
        get => _baseUrlInput;
        set { _baseUrlInput = value; OnPropertyChanged(); }
    }

    private string _inputText = string.Empty;
    public string InputText
    {
        get => _inputText;
        set
        {
            _inputText = value;
            OnPropertyChanged();
            RaiseCommandsCanExecuteChanged();
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            RaiseCommandsCanExecuteChanged();
        }
    }

    public ICommand SendCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand NewConversationCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public AiChatViewModel(
        CadViewModel cadViewModel,
        SceneScriptViewModel scriptEditor,
        ISceneEvaluator evaluator,
        IAiSettingsProvider settingsProvider,
        IAiContextBuilder? contextBuilder = null)
    {
        _cadViewModel = cadViewModel;
        _scriptEditor = scriptEditor;
        _evaluator = evaluator;
        _settingsProvider = settingsProvider;
        _contextBuilder = contextBuilder ?? new AiContextBuilder();

        var settings = _settingsProvider.GetSettings();
        _apiKeyInput = settings.ApiKey;
        _selectedModelOption = ResolveModelOption(ModelOptions, settings.Model);
        _modelInput = _selectedModelOption.ModelId;
        _baseUrlInput = settings.BaseUrl;

        InitializeSession();

        SendCommand = new RelayCommand(async () => await SendAsync(), () => IsAvailable && !IsBusy && !string.IsNullOrWhiteSpace(InputText));
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        NewConversationCommand = new RelayCommand(() =>
        {
            _session?.NewConversation();
            Messages.Clear();
            _contextAdjustments.Reset();
            RefreshContextFiles();
        }, () => IsAvailable && !IsBusy);
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        SaveSettingsCommand = new RelayCommand(SaveSettings);

        _cadViewModel.SelectionService.SelectionChanged += RefreshContextFiles;
        _scriptEditor.ProjectFiles.CollectionChanged += (_, _) => RefreshContextFiles();
        _scriptEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SceneScriptViewModel.ActiveFile) or nameof(SceneScriptViewModel.ProjectName))
            {
                RefreshContextFiles();
            }
        };
        RefreshContextFiles();
    }

    // ---------------------------------------------------------------- Contexto (arquivos enviados)

    /// <summary>O estado do editor de que o contexto depende (o builder faz o resto).</summary>
    private AiContextRequest CurrentContextRequest() => new(
        _scriptEditor.GetSceneDocuments(),
        _scriptEditor.EntryFile,
        _scriptEditor.IsProjectOpen ? _scriptEditor.ActiveFile : null,
        _cadViewModel.Sketch.Features.Where(f => f.IsSelected).ToList(),
        _scriptEditor.IsProjectOpen,
        _scriptEditor.ToSceneRelativePath,
        _contextAdjustments);

    /// <summary>Recalcula os chips a partir da seleção, do arquivo ativo e dos ajustes manuais.</summary>
    private void RefreshContextFiles()
    {
        if (_contextProjectName != _scriptEditor.ProjectName)
        {
            _contextProjectName = _scriptEditor.ProjectName;
            _contextAdjustments.Reset();
        }

        var scope = _contextBuilder.ResolveScope(CurrentContextRequest());

        ContextFiles.Clear();
        foreach (var file in scope.Files)
        {
            ContextFiles.Add(new AiContextFileChip(file, _contextAdjustments.IsPinned(file.FilePath), RemoveContextFile, TogglePinContextFile));
        }

        AddableContextFiles.Clear();
        if (_scriptEditor.IsProjectOpen)
        {
            foreach (var path in _scriptEditor.ProjectFiles.Where(p => !scope.Contains(p)))
            {
                AddableContextFiles.Add(path);
            }
        }
        OnPropertyChanged(nameof(HasContextFiles));
    }

    private void RemoveContextFile(AiContextFileChip chip)
    {
        _contextAdjustments.Exclude(chip.FilePath);
        RefreshContextFiles();
    }

    /// <summary>Fixar mantém o arquivo no contexto mesmo quando a seleção muda.</summary>
    private void TogglePinContextFile(AiContextFileChip chip)
    {
        _contextAdjustments.TogglePin(chip.FilePath);
        RefreshContextFiles();
    }

    /// <summary>A conversa inteira (prompts e respostas) como texto, para debug.</summary>
    public string GetConversationText() =>
        string.Join("\n\n", Messages.Select(m => m.ToClipboardText(includeRole: true)));

    private void InitializeSession()
    {
        var (options, error) = CadScriptChatSession.LoadOptions(_settingsProvider);
        if (options != null)
        {
            if (_session == null)
            {
                _session = new CadScriptChatSession(options, new CadScriptProtocol(), _evaluator);
            }
            else
            {
                _session.UpdateOptions(options);
            }
            ModelName = ResolveModelOption(ModelOptions, options.Model).DisplayName;
            ConfigError = null;
        }
        else
        {
            _session = null;
            ModelName = string.Empty;
            ConfigError = error;
        }

        OnPropertyChanged(nameof(IsAvailable));
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Salva a chave/modelo/URL digitados no painel de configurações e reinicia a conversa com eles.</summary>
    private void SaveSettings()
    {
        var settings = new AiSettings
        {
            ApiKey = ApiKeyInput.Trim(),
            Model = ModelInput.Trim(),
            BaseUrl = BaseUrlInput.Trim()
        };
        _settingsProvider.SaveSettings(settings);

        Messages.Clear();
        InitializeSession();
        IsSettingsOpen = false;
    }

    private void RaiseCommandsCanExecuteChanged()
    {
        (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NewConversationCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task SendAsync()
    {
        if (_session == null || IsBusy || string.IsNullOrWhiteSpace(InputText)) return;

        string instruction = InputText.Trim();
        InputText = string.Empty;

        Messages.Add(new AiChatMessageItem(isUser: true, text: instruction));
        var thinking = new AiChatMessageItem(isUser: false, text: "Thinking...");
        Messages.Add(thinking);

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            RefreshContextFiles();
            var context = _contextBuilder.Build(CurrentContextRequest());

            var result = await _session.SendAsync(instruction, context, _cts.Token);

            Messages.Remove(thinking);

            var changes = result.Changes;
            string? code = !context.IsMultiFile
                ? result.Code
                : changes.Count == 0 ? null : string.Join("\n\n", changes.Select(c => $"// ===== {c.FilePath} =====\n{c.Text}"));

            var item = new AiChatMessageItem(isUser: false, text: result.ChatText, code: code, reasoning: result.Reasoning);
            if (code != null)
            {
                item.IsValid = result.IsValid;
                string kindNoun = context.IsMultiFile
                    ? $"change in {string.Join(", ", changes.Select(c => c.FilePath))}"
                    : context.Kind == CadFileKind.PartFile ? "part" : "script";
                string autoFixSuffix = result.Attempts > 1 ? $" — auto-fixed ({result.Attempts - 1}x)" : "";
                item.ValidationText = result.IsValid
                    ? $"✓ Valid {kindNoun} ({result.FeatureCount} feature(s)){autoFixSuffix}"
                    : $"✗ {result.Error}";

                if (result.IsValid)
                {
                    item.ApplyCommand = new RelayCommand(async () =>
                    {
                        await _scriptEditor.ApplyExternalChangesAsync(changes);
                        item.IsApplied = true;
                    });
                }
            }

            Messages.Add(item);
        }
        catch (OperationCanceledException)
        {
            Messages.Remove(thinking);
            Messages.Add(new AiChatMessageItem(isUser: false, text: "Cancelled."));
        }
        catch (Exception ex)
        {
            Messages.Remove(thinking);
            Messages.Add(new AiChatMessageItem(isUser: false, text: $"Error talking to the AI: {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
