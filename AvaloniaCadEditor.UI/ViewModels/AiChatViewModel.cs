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
using AvaloniaCadEditor.Application.Ai.Prompts;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.UI.ViewModels.Harness;

namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>
/// ViewModel do painel de chat multi-round com a IA usado para gerar/editar
/// o CadScript da cena. Cada resposta com código válido pode ser aplicada como o script
/// vigente através de <see cref="AiChatMessageItem.ApplyCommand"/>.
/// Os endpoints (DeepSeek ou servidores locais OpenAI-compatíveis) são perfis editáveis pela
/// própria UI (<see cref="Profiles"/>) e persistidos entre execuções via <see cref="IAiSettingsProvider"/>.
/// Com um projeto aberto, a IA recebe só os arquivos relevantes para a seleção atual
/// (<see cref="ContextFiles"/>, calculados por <see cref="IAiContextBuilder"/>) e pode alterar vários deles.
/// </summary>
public class AiChatViewModel : INotifyPropertyChanged
{
    private readonly CadViewModel _cadViewModel;
    private readonly SceneScriptViewModel _scriptEditor;
    private readonly ISceneEvaluator _evaluator;
    private readonly IAiSettingsProvider _settingsProvider;
    private ICadScriptChatSession? _session;
    private CadScriptProtocol? _protocol;
    private CancellationTokenSource? _cts;

    private readonly IAiContextBuilder _contextBuilder;

    // Ajustes manuais do contexto: valem até a próxima "New chat" ou troca de projeto.
    private readonly AiContextAdjustments _contextAdjustments = new();
    private string? _contextProjectName;

    // Id crescente do turno (par pergunta/resposta), usado para deletar o turno inteiro.
    private int _nextTurnId;

    // Os rounds da conversa atual, para gravá-la como caso de teste (botão 💾).
    private readonly AiConversationRecorder _recorder = new();
    private string _sessionModelId = string.Empty;

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

    private string? _recordingStatus;
    /// <summary>Onde a última conversa foi gravada (some ao enviar o próximo pedido).</summary>
    public string? RecordingStatus
    {
        get => _recordingStatus;
        private set { _recordingStatus = value; OnPropertyChanged(); }
    }

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

    /// <summary>
    /// Modelos DeepSeek atualmente disponíveis (ids exatos do campo "model" da API), sugeridos
    /// para perfis não locais. Fonte: https://api-docs.deepseek.com/quick_start/pricing — V4.1 Flash e V4 Pro.
    /// </summary>
    private static readonly string[] DeepSeekModels = { "deepseek-v4-pro", "deepseek-flash" };

    /// <summary>
    /// Cópia de trabalho dos perfis de endpoint (DeepSeek, servidores locais): o painel edita
    /// estes objetos e só o Save grava. Reabrir o painel descarta edições não salvas.
    /// </summary>
    public ObservableCollection<AiEndpointProfile> Profiles { get; } = new();

    private AiEndpointProfile? _selectedProfile;
    /// <summary>Perfil em edição no painel; ao salvar, vira o perfil ativo do chat.</summary>
    public AiEndpointProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfileNameInput));
            OnPropertyChanged(nameof(ApiKeyInput));
            OnPropertyChanged(nameof(BaseUrlInput));
            OnPropertyChanged(nameof(ModelInput));
            OnPropertyChanged(nameof(IsLocalInput));
            ModelsStatus = null;
            ResetModelSuggestions();
            (RemoveProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string ProfileNameInput
    {
        get => _selectedProfile?.DisplayName ?? string.Empty;
        set { if (_selectedProfile == null) return; _selectedProfile.DisplayName = value; OnPropertyChanged(); }
    }

    public string ApiKeyInput
    {
        get => _selectedProfile?.ApiKey ?? string.Empty;
        set { if (_selectedProfile == null) return; _selectedProfile.ApiKey = value; OnPropertyChanged(); }
    }

    public string ModelInput
    {
        get => _selectedProfile?.Model ?? string.Empty;
        set { if (_selectedProfile == null) return; _selectedProfile.Model = value ?? string.Empty; OnPropertyChanged(); }
    }

    /// <summary>Servidor local: chave opcional e sem o campo <c>thinking</c> da DeepSeek.</summary>
    public bool IsLocalInput
    {
        get => _selectedProfile?.IsLocal ?? false;
        set
        {
            if (_selectedProfile == null) return;
            _selectedProfile.IsLocal = value;
            OnPropertyChanged();
            ResetModelSuggestions();
        }
    }

    /// <summary>Sugestões do campo de modelo: os ids da DeepSeek ou os listados pelo servidor (↻).</summary>
    public ObservableCollection<string> ModelSuggestions { get; } = new();

    private string? _modelsStatus;
    /// <summary>Resultado da última consulta de modelos ao servidor do perfil.</summary>
    public string? ModelsStatus
    {
        get => _modelsStatus;
        private set { _modelsStatus = value; OnPropertyChanged(); }
    }

    /// <summary>As versões de pré-prompt (system prompt) que podem ser escolhidas.</summary>
    public IReadOnlyList<ICadScriptPromptProfile> PromptProfileOptions => CadScriptPromptProfiles.All;

    private ICadScriptPromptProfile _selectedPromptProfile = CadScriptPromptProfiles.Default;
    /// <summary>Perfil de pré-prompt (aplicado ao salvar as configurações; recomeça a conversa).</summary>
    public ICadScriptPromptProfile SelectedPromptProfile
    {
        get => _selectedPromptProfile;
        set { _selectedPromptProfile = value; OnPropertyChanged(); }
    }

    public string BaseUrlInput
    {
        get => _selectedProfile?.BaseUrl ?? string.Empty;
        set { if (_selectedProfile == null) return; _selectedProfile.BaseUrl = value; OnPropertyChanged(); }
    }

    private bool _keepHistoryInput = true;
    /// <summary>Multi-round: manter o histórico da conversa entre turnos (aplicado ao salvar as configurações).</summary>
    public bool KeepHistoryInput
    {
        get => _keepHistoryInput;
        set { _keepHistoryInput = value; OnPropertyChanged(); }
    }

    private bool _enableReasoningInput;
    /// <summary>Reasoning/thinking mode (aplicado ao salvar as configurações).</summary>
    public bool EnableReasoningInput
    {
        get => _enableReasoningInput;
        set { _enableReasoningInput = value; OnPropertyChanged(); }
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
    public ICommand SaveConversationCommand { get; }
    public ICommand AddLocalProfileCommand { get; }
    public ICommand RemoveProfileCommand { get; }
    public ICommand RefreshModelsCommand { get; }

    /// <summary>Ferramentas e Inspetor (harness); null quando o composition root não liga o harness.</summary>
    public HarnessSettingsViewModel? Harness { get; }

    public bool HasHarness => Harness != null;

    public AiChatViewModel(
        CadViewModel cadViewModel,
        SceneScriptViewModel scriptEditor,
        ISceneEvaluator evaluator,
        IAiSettingsProvider settingsProvider,
        IAiContextBuilder? contextBuilder = null,
        HarnessSettingsViewModel? harness = null)
    {
        Harness = harness;
        _cadViewModel = cadViewModel;
        _scriptEditor = scriptEditor;
        _evaluator = evaluator;
        _settingsProvider = settingsProvider;
        _contextBuilder = contextBuilder ?? new AiContextBuilder();

        AddLocalProfileCommand = new RelayCommand(AddLocalProfile);
        RemoveProfileCommand = new RelayCommand(RemoveSelectedProfile, () => Profiles.Count > 1 && SelectedProfile != null);
        RefreshModelsCommand = new RelayCommand(async () => await RefreshModelsAsync(), () => SelectedProfile != null);

        LoadSettingsInputs();
        InitializeSession();

        SendCommand = new RelayCommand(async () => await SendAsync(), () => IsAvailable && !IsBusy && !string.IsNullOrWhiteSpace(InputText));
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        NewConversationCommand = new RelayCommand(() =>
        {
            _session?.NewConversation();
            Messages.Clear();
            _recorder.Clear();
            _nextTurnId = 0;
            _contextAdjustments.Reset();
            RefreshContextFiles();
        }, () => IsAvailable && !IsBusy);
        ToggleSettingsCommand = new RelayCommand(() =>
        {
            if (!IsSettingsOpen) LoadSettingsInputs();
            IsSettingsOpen = !IsSettingsOpen;
        });
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        SaveConversationCommand = new RelayCommand(SaveConversation, () => _recorder.Count > 0 && !IsBusy);

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

    /// <summary>O que foi enviado à IA neste turno (arquivos de contexto + sólidos selecionados), para registro no balão.</summary>
    private IReadOnlyList<AiSentContextItem> BuildSentContextSnapshot(CadScriptContext context)
    {
        var items = new List<AiSentContextItem>();

        foreach (var chip in ContextFiles)
        {
            items.Add(new AiSentContextItem(chip.DisplayName, chip.ToolTip, isSolid: false));
        }

        var solids = context.Scope?.SelectedFeatures;
        if (solids is { Count: > 0 })
        {
            foreach (var solid in solids)
            {
                items.Add(new AiSentContextItem(FirstToken(solid), solid, isSolid: true));
            }
        }
        else if (!string.IsNullOrWhiteSpace(context.SelectedFeatureName))
        {
            items.Add(new AiSentContextItem(FirstToken(context.SelectedFeatureName!), context.SelectedFeatureName!, isSolid: true));
        }

        return items;
    }

    /// <summary>O nome curto de uma descrição de sólido (ex.: "trilho_a (peça...) — Cena.cs" → "trilho_a").</summary>
    private static string FirstToken(string description)
    {
        string trimmed = description.Trim();
        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>Apaga o turno inteiro (pergunta + resposta) dos balões e do histórico da API.</summary>
    private void DeleteTurn(int turnId)
    {
        if (IsBusy) return;

        // Posição ordinal do turno entre os turnos ainda presentes (= índice no histórico da sessão).
        int position = Messages
            .Where(m => m.IsUser)
            .Select(m => m.TurnId)
            .Distinct()
            .TakeWhile(id => id != turnId)
            .Count();

        bool removedAny = false;
        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i].TurnId == turnId)
            {
                Messages.RemoveAt(i);
                removedAny = true;
            }
        }

        if (removedAny)
        {
            _session?.DeleteTurn(position);
            _recorder.Remove(turnId);
            RaiseCommandsCanExecuteChanged();
        }
    }

    /// <summary>Grava a conversa (contexto exato de cada round + respostas) para reexecutá-la com outros pré-prompts.</summary>
    private void SaveConversation()
    {
        try
        {
            var record = _recorder.ToRecord(_sessionModelId, _protocol?.Profile.Id ?? string.Empty, DateTimeOffset.Now);
            string path = AiConversationRecordStore.Save(record, AiConversationRecordStore.DefaultDirectory);
            RecordingStatus = $"Conversa gravada em {path}";
        }
        catch (Exception ex)
        {
            RecordingStatus = $"Falha ao gravar a conversa: {ex.Message}";
        }
    }

    /// <summary>A conversa inteira (prompts e respostas) como texto, para debug.</summary>
    public string GetConversationText() =>
        string.Join("\n\n", Messages.Select(m => m.ToClipboardText(includeRole: true)));

    private void InitializeSession()
    {
        var (options, error) = CadScriptChatSession.LoadOptions(_settingsProvider);
        if (options != null)
        {
            var settings = _settingsProvider.GetSettings();
            var profile = CadScriptPromptProfiles.Find(settings.PromptProfile);
            var endpoint = AiSettingsMigration.Upgrade(settings).GetActiveProfile();

            // O system prompt é fixado quando a sessão começa: trocar de perfil exige uma sessão nova.
            // Com harness, a sessão é sempre nova: o tipo dela (com ou sem ferramentas) e os papéis
            // vêm do harness_settings.json, que pode ter mudado no Save.
            if (_session == null || _protocol?.Profile.Id != profile.Id || Harness != null)
            {
                _protocol = new CadScriptProtocol(profile);
                _session = Harness?.CreateSession(options, _protocol) ?? new CadScriptChatSession(options, _protocol, _evaluator);
            }
            else
            {
                _session.UpdateOptions(options);
            }
            _session.KeepHistory = settings.KeepConversationHistory;
            _sessionModelId = options.Model ?? string.Empty;
            string tools = Harness?.SessionLabel is { Length: > 0 } label ? $" · {label}" : "";
            ModelName = $"{endpoint} · {options.Model} · {profile.Id}{tools}";
            ConfigError = null;
        }
        else
        {
            _session = null;
            _protocol = null;
            ModelName = string.Empty;
            ConfigError = error;
        }

        OnPropertyChanged(nameof(IsAvailable));
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Preenche o painel com as settings gravadas (descarta edições não salvas).</summary>
    private void LoadSettingsInputs()
    {
        var settings = AiSettingsMigration.Upgrade(_settingsProvider.GetSettings());

        Profiles.Clear();
        foreach (var profile in settings.Profiles) Profiles.Add(profile.Clone());
        var active = settings.GetActiveProfile();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == active?.Id) ?? Profiles.FirstOrDefault();

        KeepHistoryInput = settings.KeepConversationHistory;
        EnableReasoningInput = settings.EnableReasoning;
        SelectedPromptProfile = CadScriptPromptProfiles.Find(settings.PromptProfile);
        Harness?.Load(Profiles);
    }

    private void ResetModelSuggestions()
    {
        ModelSuggestions.Clear();
        if (_selectedProfile is { IsLocal: false })
            foreach (var model in DeepSeekModels) ModelSuggestions.Add(model);
    }

    private void AddLocalProfile()
    {
        var profile = AiEndpointProfile.Local(AiEndpointProfile.NewId(Profiles, "Local"), "Local");
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    private void RemoveSelectedProfile()
    {
        if (SelectedProfile == null || Profiles.Count <= 1) return;
        int index = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[Math.Min(index, Profiles.Count - 1)];
    }

    /// <summary>Pergunta ao servidor do perfil quais modelos ele tem (<c>GET /v1/models</c>).</summary>
    private async Task RefreshModelsAsync()
    {
        var profile = SelectedProfile;
        if (profile == null) return;

        ModelsStatus = "Consultando o servidor…";
        try
        {
            var models = await AiEndpointOptions.ListModelsAsync(profile);
            if (!ReferenceEquals(profile, SelectedProfile)) return;

            ModelSuggestions.Clear();
            foreach (var model in models) ModelSuggestions.Add(model);
            ModelsStatus = models.Count == 0 ? "O servidor não listou nenhum modelo." : $"{models.Count} modelo(s) no servidor.";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(profile, SelectedProfile)) ModelsStatus = $"Falha ao listar modelos: {ex.Message}";
        }
    }

    /// <summary>Salva os perfis e opções do painel de configurações e reinicia a conversa com o perfil selecionado.</summary>
    private void SaveSettings()
    {
        var settings = new AiSettings
        {
            SchemaVersion = AiSettings.CurrentSchemaVersion,
            Profiles = Profiles.Select(p => Trimmed(p.Clone())).ToList(),
            ActiveProfileId = SelectedProfile?.Id ?? string.Empty,
            KeepConversationHistory = KeepHistoryInput,
            EnableReasoning = EnableReasoningInput,
            PromptProfile = SelectedPromptProfile.Id
        };
        _settingsProvider.SaveSettings(settings);
        Harness?.Save();

        // Trocar de modo (ex.: multi-round ↔ stateless) reinicia a conversa para manter Messages e o
        // histórico da API em sincronia.
        _session?.NewConversation();
        Messages.Clear();
        _recorder.Clear();
        _nextTurnId = 0;
        InitializeSession();
        IsSettingsOpen = false;
    }

    private static AiEndpointProfile Trimmed(AiEndpointProfile profile)
    {
        profile.DisplayName = profile.DisplayName.Trim();
        profile.ApiKey = profile.ApiKey.Trim();
        profile.BaseUrl = profile.BaseUrl.Trim();
        profile.Model = profile.Model.Trim();
        return profile;
    }

    private void RaiseCommandsCanExecuteChanged()
    {
        (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NewConversationCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveConversationCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task SendAsync()
    {
        if (_session == null || IsBusy || string.IsNullOrWhiteSpace(InputText)) return;

        string instruction = InputText.Trim();
        InputText = string.Empty;
        RecordingStatus = null;

        int turnId = _nextTurnId++;
        var userItem = new AiChatMessageItem(isUser: true, text: instruction) { TurnId = turnId };
        userItem.DeleteCommand = new RelayCommand(() => DeleteTurn(turnId), () => !IsBusy);
        Messages.Add(userItem);
        var thinking = new AiChatMessageItem(isUser: false, text: "Thinking...") { TurnId = turnId };
        Messages.Add(thinking);

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            RefreshContextFiles();
            var context = _contextBuilder.Build(CurrentContextRequest());
            userItem.SentContext = BuildSentContextSnapshot(context);

            var result = await _session.SendAsync(instruction, context, _cts.Token);
            _recorder.Record(turnId, instruction, context, _protocol!.BuildUserMessage(context, instruction), result,
                _session.History.LastOrDefault(m => m.Role == "assistant")?.Content ?? string.Empty);

            Messages.Remove(thinking);

            var changes = result.Changes;
            string? code = !context.IsMultiFile
                ? result.Code
                : changes.Count == 0 ? null : string.Join("\n\n", changes.Select(c => $"// ===== {c.FilePath} =====\n{c.Text}"));

            var item = new AiChatMessageItem(isUser: false, text: result.ChatText, code: code, reasoning: result.Reasoning)
            {
                TurnId = turnId,
                DeleteCommand = new RelayCommand(() => DeleteTurn(turnId), () => !IsBusy)
            };
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
                        // Só a última resposta aplicada fica marcada; as demais podem ser reaplicadas.
                        foreach (var other in Messages) other.IsApplied = false;
                        item.IsApplied = true;
                    });
                }
            }

            Messages.Add(item);
        }
        catch (OperationCanceledException)
        {
            Messages.Remove(thinking);
            Messages.Add(new AiChatMessageItem(isUser: false, text: "Cancelled.")
            {
                TurnId = turnId,
                DeleteCommand = new RelayCommand(() => DeleteTurn(turnId), () => !IsBusy)
            });
        }
        catch (Exception ex)
        {
            Messages.Remove(thinking);
            Messages.Add(new AiChatMessageItem(isUser: false, text: $"Error talking to the AI: {ex.Message}")
            {
                TurnId = turnId,
                DeleteCommand = new RelayCommand(() => DeleteTurn(turnId), () => !IsBusy)
            });
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
