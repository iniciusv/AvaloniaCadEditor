using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.UI.Services;

namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>
/// Editor de código da cena. Funciona em dois modos:
/// <list type="bullet">
/// <item>arquivo único (como antes): o texto do editor é a cena inteira;</item>
/// <item>projeto (pasta com .csproj): vários arquivos, o disco é a fonte da verdade, e mudanças
/// feitas fora do app (ex.: por uma IA externa) recarregam e reexecutam a cena.</item>
/// </list>
/// </summary>
public class SceneScriptViewModel : INotifyPropertyChanged, IPartCodeEditor, ICodeEditorCommands
{
    private const int ReloadDebounceMs = 300;

    private readonly ISceneEvaluator? _sceneEvaluator;
    private readonly IFilePickerService? _filePickerService;
    private readonly CadViewModel _cadViewModel;
    private readonly CodeHistoryManager _history = new();
    private readonly Action<Action> _uiInvoker;
    private readonly ISceneCodeNavigator _codeNavigator;

    // Modo projeto: texto de cada arquivo em memória e o último conteúdo visto em disco.
    private SceneProject? _project;
    private readonly Dictionary<string, string> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _diskTexts = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeFile;
    private string _entryFile = SceneProject.DefaultEntryFile;
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;

    public SceneScriptViewModel(
        CadViewModel cadViewModel,
        ISceneEvaluator? sceneEvaluator,
        IFilePickerService? filePickerService,
        Action<Action>? uiInvoker = null,
        ISceneCodeNavigator? codeNavigator = null)
    {
        _codeNavigator = codeNavigator ?? new AvaloniaCadEditor.Infrastructure.Scripting.RoslynCodeNavigator();
        _cadViewModel = cadViewModel;
        _sceneEvaluator = sceneEvaluator;
        _filePickerService = filePickerService;
        _uiInvoker = uiInvoker ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));

        GenerateCodeCommand = new RelayCommand(() =>
        {
            // O gerador foi castrado. O código agora é a fonte da verdade.
        });
        RunCodeCommand = new RelayCommand(async () => await RunCodeAsync());

        LoadCodeFromFileCommand = new RelayCommand(async () =>
        {
            if (_filePickerService == null) return;
            var path = await _filePickerService.OpenFileAsync("Carregar Código C#", new[] { "*.cs" });
            if (!string.IsNullOrEmpty(path))
            {
                CloseProject();
                _history.SaveState(GeneratedCSharpCode);
                GeneratedCSharpCode = File.ReadAllText(path);
                RunCodeCommand.Execute(null);
                _cadViewModel.StatusText = $"✓ Modelo carregado de: {Path.GetFileName(path)}";
            }
        });

        SaveCodeToFileCommand = new RelayCommand(async () =>
        {
            if (_filePickerService == null) return;
            var path = await _filePickerService.SaveFileAsync("Salvar Modelo como C#", ".cs", "Model.cs");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, GeneratedCSharpCode);
                _cadViewModel.StatusText = $"✓ Modelo salvo em: {Path.GetFileName(path)}";
            }
        });

        NewSceneCommand = new RelayCommand(async () => await NewSceneAsync());
        OpenSceneCommand = new RelayCommand(async () => await OpenSceneAsync());
        CreateFileCommand = new RelayCommand(async () => await CreateFileAsync());
        GoToFirstErrorCommand = new RelayCommand(GoToFirstError);
        InsertPartCommand = new RelayCommand(async () => await InsertPartAsync(SelectedPartType));
        ConfirmRenameCommand = new RelayCommand(async () => await ConfirmRenameAsync());
        CancelRenameCommand = new RelayCommand(CancelRename);
        CloseReferencesCommand = new RelayCommand(CloseReferences);
    }

    public ICommand GenerateCodeCommand { get; }
    public ICommand RunCodeCommand { get; }
    public ICommand LoadCodeFromFileCommand { get; }
    public ICommand SaveCodeToFileCommand { get; }
    public ICommand NewSceneCommand { get; }
    public ICommand OpenSceneCommand { get; }
    public ICommand CreateFileCommand { get; }
    public ICommand GoToFirstErrorCommand { get; }
    public ICommand InsertPartCommand { get; }
    public ICommand ConfirmRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }
    public ICommand CloseReferencesCommand { get; }

    // ---------------------------------------------------------------- Erros da última execução

    private IReadOnlyList<ScriptDiagnostic> _errors = Array.Empty<ScriptDiagnostic>();

    /// <summary>Erros da última execução. Enquanto houver, a cena mostrada é a última válida.</summary>
    public IReadOnlyList<ScriptDiagnostic> Errors => _errors;

    public bool HasErrors => _errors.Count > 0;

    /// <summary>Primeiro erro formatado (arquivo, linha e mensagem) + quantos outros há.</summary>
    public string? ErrorSummary
    {
        get
        {
            if (_errors.Count == 0) return null;
            var first = _errors[0];
            string where = string.IsNullOrEmpty(first.FilePath) ? string.Empty : $"{first.FilePath}({first.Line}): ";
            string more = _errors.Count > 1 ? $"  (+{_errors.Count - 1} erro(s))" : string.Empty;
            return $"{where}{first.Message}{more}";
        }
    }

    /// <summary>Linhas (1-based) com erro no arquivo mostrado no editor.</summary>
    public IReadOnlyList<int> ActiveFileErrorLines
    {
        get
        {
            string current = _project != null ? _activeFile ?? string.Empty : ScriptDocument.DefaultSceneFile;
            return _errors
                .Where(e => e.Line > 0 && string.Equals(NormalizeErrorPath(e.FilePath), current, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Line)
                .Distinct()
                .ToList();
        }
    }

    private string NormalizeErrorPath(string path) =>
        _project != null && !string.IsNullOrEmpty(path) ? _project.ToRelativePath(path) : path;

    private void SetErrors(IReadOnlyList<ScriptDiagnostic> errors)
    {
        _errors = errors;
        OnPropertyChanged(nameof(Errors));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(ErrorSummary));
        OnPropertyChanged(nameof(ActiveFileErrorLines));
    }

    /// <summary>Abre no editor o arquivo do primeiro erro.</summary>
    public void GoToFirstError()
    {
        var first = _errors.FirstOrDefault(e => !string.IsNullOrEmpty(e.FilePath));
        if (first == null || _project == null) return;
        string file = NormalizeErrorPath(first.FilePath);
        if (_documents.ContainsKey(file) && !string.Equals(file, _activeFile, StringComparison.OrdinalIgnoreCase))
        {
            SwitchActiveFile(file);
        }
    }

    // ---------------------------------------------------------------- Novo arquivo

    private string _newFileName = string.Empty;

    /// <summary>Nome digitado para o próximo arquivo (ex.: "Suporte" cria Pecas/Suporte.cs).</summary>
    public string NewFileName
    {
        get => _newFileName;
        set { _newFileName = value ?? string.Empty; OnPropertyChanged(); }
    }

    /// <summary>Cria um arquivo de peça na cena, abre no editor e reexecuta.</summary>
    public async Task CreateFileAsync()
    {
        if (_project == null)
        {
            _cadViewModel.StatusText = "Abra ou crie uma cena (pasta) para adicionar arquivos.";
            return;
        }

        string relative;
        try
        {
            relative = SceneProjectTemplate.CreatePartFile(_project, NewFileName);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _cadViewModel.StatusText = $"✗ {ex.Message}";
            return;
        }

        string text = File.ReadAllText(_project.ToFullPath(relative));
        _documents[relative] = text;
        _diskTexts[relative] = text;
        RefreshFileList();
        SwitchActiveFile(relative);
        NewFileName = string.Empty;
        _cadViewModel.StatusText = $"✓ Criado {relative}";
        await RunCodeAsync();
    }

    private string _generatedCSharpCode = "// Pressione 'Atualizar Código' para gerar o C#.";
    public string GeneratedCSharpCode
    {
        get => _generatedCSharpCode;
        set
        {
            _generatedCSharpCode = value;
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- Modo projeto

    /// <summary>Arquivos da cena aberta (relativos à pasta), para o seletor do editor.</summary>
    public ObservableCollection<string> ProjectFiles { get; } = new();

    public SceneProject? Project => _project;
    public bool IsProjectOpen => _project != null;
    public string? ProjectName => _project?.Name;

    /// <summary>Arquivo de entrada da cena (o único com statements top-level): Cena.cs numa cena de arquivo único.</summary>
    public string EntryFile => _project != null ? _entryFile : ScriptDocument.DefaultSceneFile;

    /// <summary>Fontes atuais de todos os arquivos da cena (com o texto ainda não salvo do editor), para dar contexto à IA.</summary>
    public IReadOnlyList<ScriptDocument> GetSceneDocuments() => CurrentDocuments();

    /// <summary>Arquivo mostrado no editor. Trocar de arquivo guarda o texto atual em memória.</summary>
    public string? ActiveFile
    {
        get => _activeFile;
        set
        {
            if (_project == null || value == null || string.Equals(value, _activeFile, StringComparison.OrdinalIgnoreCase)) return;
            if (!_documents.ContainsKey(value)) return;
            SwitchActiveFile(value);
        }
    }

    public async Task NewSceneAsync()
    {
        if (_filePickerService == null) return;
        var folder = await _filePickerService.OpenFolderAsync("Escolha uma pasta vazia para a nova cena");
        if (string.IsNullOrEmpty(folder)) return;

        try
        {
            SceneProjectTemplate.Create(folder, SceneToolPaths.FromCurrentProcess());
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _cadViewModel.StatusText = $"✗ {ex.Message}";
            return;
        }

        await OpenProjectAsync(folder);
    }

    public async Task OpenSceneAsync()
    {
        if (_filePickerService == null) return;
        var folder = await _filePickerService.OpenFolderAsync("Abrir pasta da cena");
        if (string.IsNullOrEmpty(folder)) return;
        await OpenProjectAsync(folder);
    }

    /// <summary>Abre a cena da pasta, passa a observá-la e executa.</summary>
    public async Task OpenProjectAsync(string folder)
    {
        SceneProject project;
        IReadOnlyList<ScriptDocument> documents;
        try
        {
            project = SceneProject.Load(folder);
            documents = project.ReadDocuments();
            SceneProjectTemplate.RefreshGeneratedFiles(project);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _cadViewModel.StatusText = $"✗ {ex.Message}";
            return;
        }

        CloseProject();
        _project = project;
        foreach (var doc in documents)
        {
            _documents[doc.FilePath] = doc.Text;
            _diskTexts[doc.FilePath] = doc.Text;
        }

        _entryFile = SceneProject.FindEntryFile(documents);
        if (!_documents.ContainsKey(_entryFile))
        {
            _documents[_entryFile] = string.Empty;
        }

        RefreshFileList();
        SwitchActiveFile(_entryFile);
        StartWatcher();

        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(IsProjectOpen));
        OnPropertyChanged(nameof(ProjectName));

        await RunCodeAsync();
    }

    /// <summary>Volta ao modo de arquivo único (o texto atual do editor permanece).</summary>
    public void CloseProject()
    {
        if (_project == null) return;

        _watcher?.Dispose();
        _watcher = null;
        _reloadTimer?.Dispose();
        _reloadTimer = null;

        _project = null;
        _documents.Clear();
        _diskTexts.Clear();
        _activeFile = null;
        ProjectFiles.Clear();

        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(IsProjectOpen));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ActiveFile));
    }

    /// <summary>
    /// Relê a pasta e aplica o que mudou fora do app. O disco vence: um arquivo alterado
    /// externamente substitui a versão em memória. Reexecuta a cena se algo mudou.
    /// </summary>
    public async Task ReloadFromDiskAsync()
    {
        var project = _project;
        if (project == null) return;

        SyncActiveDocument();

        IReadOnlyList<ScriptDocument> disk;
        try
        {
            disk = project.ReadDocuments();
        }
        catch (IOException)
        {
            // Arquivo ainda sendo gravado por outro processo: tenta de novo em seguida.
            _reloadTimer?.Change(ReloadDebounceMs, Timeout.Infinite);
            return;
        }

        var changed = new List<string>();
        var diskMap = disk.ToDictionary(d => d.FilePath, d => d.Text, StringComparer.OrdinalIgnoreCase);

        foreach (var (file, text) in diskMap)
        {
            if (!_diskTexts.TryGetValue(file, out var known) || known != text)
            {
                changed.Add(file);
                _diskTexts[file] = text;
                _documents[file] = text;
            }
        }

        foreach (var removed in _diskTexts.Keys.Where(k => !diskMap.ContainsKey(k)).ToList())
        {
            changed.Add(removed);
            _diskTexts.Remove(removed);
            _documents.Remove(removed);
        }

        if (changed.Count == 0) return;

        _entryFile = SceneProject.FindEntryFile(_documents.Select(kv => new ScriptDocument(kv.Key, kv.Value)));
        RefreshFileList();

        if (_activeFile == null || !_documents.ContainsKey(_activeFile))
        {
            _activeFile = null;
            string? next = _documents.ContainsKey(_entryFile) ? _entryFile : ProjectFiles.FirstOrDefault();
            if (next != null) SwitchActiveFile(next);
            else GeneratedCSharpCode = string.Empty;
        }
        else if (changed.Contains(_activeFile, StringComparer.OrdinalIgnoreCase))
        {
            _history.Clear();
            GeneratedCSharpCode = _documents[_activeFile];
        }

        _cadViewModel.StatusText = $"↻ Alterado fora do app: {string.Join(", ", changed)}";
        await RunCodeAsync();
    }

    /// <summary>
    /// Mostra no editor onde a feature foi registrada: troca de arquivo (numa cena com vários) e,
    /// para instâncias de peça, rola até a linha do <c>Add(...)</c>.
    /// </summary>
    public void RevealFeature(IFeature? feature)
    {
        if (feature == null) return;
        var origin = FeatureOrigin.Of(feature);
        if (origin == null || string.IsNullOrEmpty(origin.FilePath)) return;

        if (_project != null)
        {
            string file = _project.ToRelativePath(origin.FilePath);
            if (!_documents.ContainsKey(file)) return;
            if (!string.Equals(file, _activeFile, StringComparison.OrdinalIgnoreCase))
            {
                SwitchActiveFile(file);
            }
        }

        if (feature is PartInstance && origin.Line > 0)
        {
            RevealLine = new EditorLineRequest(origin.Line);
        }
    }

    /// <summary>
    /// Seleciona no editor a declaração de um sólido criado dentro do <c>Build()</c> de uma peça
    /// (ver <see cref="PartSourceEditor.FindToolDeclaration"/>); sem ela, abre a declaração do record.
    /// </summary>
    public void RevealPartTool(string partTypeName, string toolName)
    {
        var location = PartSourceEditor.FindToolDeclaration(CurrentDocuments(), partTypeName, toolName);
        if (location == null)
        {
            GoToPartDefinition(partTypeName);
            _cadViewModel.StatusText = $"{toolName}: declaração não encontrada em {partTypeName} (nome gerado em laço?).";
            return;
        }
        NavigateTo(location);
    }

    /// <summary>Seleciona no editor a linha onde uma feature solta foi registrada (AddFeature).</summary>
    public void RevealLooseFeatureLine(IFeature feature)
    {
        var origin = FeatureOrigin.Of(feature);
        if (origin == null || origin.Line <= 0) return;
        RevealFeature(feature);
        _cadViewModel.SidebarTabIndex = CadViewModel.CodeTabIndex;
        RevealLine = new EditorLineRequest(origin.Line);
    }

    private void SwitchActiveFile(string file)
    {
        SyncActiveDocument();
        _activeFile = file;
        _history.Clear();
        GeneratedCSharpCode = _documents.TryGetValue(file, out var text) ? text : string.Empty;
        OnPropertyChanged(nameof(ActiveFile));
        OnPropertyChanged(nameof(ActiveFileErrorLines));
    }

    private void SyncActiveDocument()
    {
        if (_project != null && _activeFile != null)
        {
            _documents[_activeFile] = GeneratedCSharpCode;
        }
    }

    /// <summary>Código gerado por ferramentas e pela IA vai sempre para o arquivo de entrada da cena.</summary>
    private void EnsureEntryDocumentActive()
    {
        if (_project != null && !string.Equals(_activeFile, _entryFile, StringComparison.OrdinalIgnoreCase))
        {
            if (!_documents.ContainsKey(_entryFile)) _documents[_entryFile] = string.Empty;
            SwitchActiveFile(_entryFile);
        }
    }

    private void RefreshFileList()
    {
        var files = _documents.Keys.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.SequenceEqual(ProjectFiles, StringComparer.OrdinalIgnoreCase)) return;

        ProjectFiles.Clear();
        foreach (var f in files) ProjectFiles.Add(f);
        OnPropertyChanged(nameof(ActiveFile));
    }

    /// <summary>Grava no disco os arquivos cujo texto difere do último conteúdo lido/gravado.</summary>
    private bool SaveDocumentsToDisk()
    {
        if (_project == null) return true;
        try
        {
            foreach (var (file, text) in _documents)
            {
                if (_diskTexts.TryGetValue(file, out var known) && known == text) continue;

                // Registra antes de gravar: o watcher verá o próprio arquivo e não o tratará como externo.
                _diskTexts[file] = text;
                string path = _project.ToFullPath(file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _cadViewModel.StatusText = $"✗ Não foi possível salvar a cena: {ex.Message}";
            return false;
        }
    }

    private void StartWatcher()
    {
        if (_project == null) return;
        try
        {
            _watcher = new FileSystemWatcher(_project.RootPath, "*.cs")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName
            };
            _watcher.Changed += (_, e) => OnDiskChanged(e.FullPath);
            _watcher.Created += (_, e) => OnDiskChanged(e.FullPath);
            _watcher.Deleted += (_, e) => OnDiskChanged(e.FullPath);
            _watcher.Renamed += (_, e) => OnDiskChanged(e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            _cadViewModel.StatusText = $"Aviso: mudanças externas não serão detectadas ({ex.Message})";
        }
    }

    private void OnDiskChanged(string fullPath)
    {
        var project = _project;
        if (project == null || project.IsIgnoredPath(fullPath)) return;

        // Debounce: editores e IAs costumam gravar em várias etapas.
        _reloadTimer ??= new Timer(_ =>
        {
            // Callback de Timer não pode lançar: derrubaria o processo.
            try { _uiInvoker(() => _ = ReloadFromDiskAsync()); }
            catch (Exception ex) { Console.WriteLine("Falha ao agendar recarga da cena: " + ex.Message); }
        });
        _reloadTimer.Change(ReloadDebounceMs, Timeout.Infinite);
    }

    // ---------------------------------------------------------------- Execução

    public async Task RunCodeAsync()
    {
        if (_sceneEvaluator == null)
        {
            _cadViewModel.StatusText = "Aviso: Evaluator não configurado.";
            return;
        }
        _cadViewModel.StatusText = "Avaliando código...";

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        SceneEvaluationResult result;
        if (_project != null)
        {
            SyncActiveDocument();
            if (!SaveDocumentsToDisk()) return;
            var documents = _documents
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new ScriptDocument(kv.Key, kv.Value))
                .ToList();
            result = await _sceneEvaluator.EvaluateAsync(documents);
        }
        else
        {
            result = await _sceneEvaluator.EvaluateAsync(GeneratedCSharpCode);
        }
        long evaluateMs = stopwatch.ElapsedMilliseconds;

        if (result.Success)
        {
            // Reaproveita malhas de geometria idêntica da execução anterior (ex.: só uma peça mudou de lugar).
            _cadViewModel.MeshManager.ResetForNewScene();
            _cadViewModel.Sketch.ClearFeatures();
            foreach (var feature in result.Features)
            {
                _cadViewModel.Sketch.AddFeature(feature);
            }
            // RefreshProfiles já refaz a malha composta e a árvore, uma vez cada.
            _cadViewModel.RefreshProfiles();
            Console.WriteLine($"[Run] avaliação {evaluateMs} ms, geometria/árvore {stopwatch.ElapsedMilliseconds - evaluateMs} ms, {result.Features.Count} feature(s)");
            _cadViewModel.StatusText = _project != null
                ? $"✓ {_project.Name}: {result.Features.Count} feature(s) em {_documents.Count} arquivo(s)"
                : $"✓ Código executado: {result.Features.Count} feature(s)";
            SetErrors(Array.Empty<ScriptDiagnostic>());
            RefreshPartTypes();
        }
        else
        {
            string error = (result.ErrorMessage ?? "Erro desconhecido").Replace("\r", "").Replace("\n", " ");
            _cadViewModel.StatusText = $"✗ Erro: {error}";
            Console.WriteLine("Evaluation Error:\n" + result.ErrorMessage);

            var errors = result.Diagnostics.Where(d => d.Severity == ScriptDiagnosticSeverity.Error).ToList();
            if (errors.Count == 0)
            {
                errors.Add(new ScriptDiagnostic(string.Empty, 0, 0, ScriptDiagnosticSeverity.Error, "CAD0000", error));
            }
            SetErrors(errors);
            RefreshPartTypes();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ---------------------------------------------------------------- Peças: parâmetros, definição e inserção

    private IReadOnlyList<PartTypeInfo> _partTypeInfos = Array.Empty<PartTypeInfo>();
    private string? _selectedPartType;
    private EditorLineRequest? _revealLine;

    /// <summary>Tipos de peça declarados nos arquivos da cena (records que herdam de Part).</summary>
    public ObservableCollection<string> PartTypes { get; } = new();

    public bool HasPartTypes => PartTypes.Count > 0;

    /// <summary>Peça escolhida para o botão "Inserir".</summary>
    public string? SelectedPartType
    {
        get => _selectedPartType;
        set { _selectedPartType = value; OnPropertyChanged(); }
    }

    /// <summary>Linha que o editor deve mostrar (definição de uma peça, chamada de uma instância).</summary>
    public EditorLineRequest? RevealLine
    {
        get => _revealLine;
        private set { _revealLine = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<PartArgument>? ReadPartArguments(PartInstance instance)
    {
        var origin = FeatureOrigin.Of(instance);
        if (origin == null) return null;
        string? source = GetDocumentText(SourceFileOf(origin));
        if (source == null) return null;

        var names = instance.Part.GetParameters().Select(p => p.Name).ToList();
        return PartSourceEditor.ReadArguments(source, origin.Line, names);
    }

    public int CountInstancesFromSameCall(PartInstance instance)
    {
        var origin = FeatureOrigin.Of(instance);
        if (origin == null) return 1;
        return _cadViewModel.Sketch.Features
            .OfType<PartInstance>()
            .Count(other => FeatureOrigin.Of(other) is { } o && o.Line == origin.Line &&
                            string.Equals(o.FilePath, origin.FilePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Troca o valor de um parâmetro no código que criou a instância e reexecuta a cena.
    /// Quando a chamada está num laço, todas as instâncias criadas por ela mudam juntas: o
    /// inspector edita o código, não uma cópia solta da peça.
    /// </summary>
    public async Task<string?> SetPartParameterAsync(PartInstance instance, string parameterName, string text)
    {
        var origin = FeatureOrigin.Of(instance);
        if (origin == null) return "Não sei de onde esta instância veio no código.";

        var parameters = instance.Part.GetParameters();
        int index = parameters.Select(p => p.Name).ToList().IndexOf(parameterName);
        if (index < 0) return $"{instance.PartTypeName} não tem o parâmetro {parameterName}.";
        var parameter = parameters[index];

        if (!PartSourceEditor.IsEditableType(parameter.Type))
            return $"Parâmetros do tipo {parameter.Type.Name} só podem ser editados no código.";
        if (!PartSourceEditor.TryParseValue(parameter.Type, text, out var value, out var parseError))
            return parseError;
        if (parameter.Range != null && !parameter.Range.IsValid(value))
            return $"{parameterName} deve ficar entre {FormatInvariant(parameter.Range.Minimum)} e {FormatInvariant(parameter.Range.Maximum)}.";

        string file = SourceFileOf(origin);
        string? source = GetDocumentText(file);
        if (source == null) return $"O arquivo {file} não faz parte da cena aberta.";

        string valueCode = PartSourceEditor.FormatValue(value!);
        string updated;
        try
        {
            updated = PartSourceEditor.SetArgument(source, origin.Line, parameterName, index, valueCode);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
        if (updated == source) return null;

        string instanceName = instance.Name;
        SetDocumentText(file, updated);
        await RunCodeAsync();
        if (HasErrors) return ErrorSummary;

        SelectFeatureByName(instanceName);
        _cadViewModel.StatusText = $"✓ {instance.PartTypeName}.{parameterName} = {valueCode}  ({file}, linha {origin.Line})";
        return null;
    }

    /// <summary>
    /// Aplica <paramref name="edit"/> à chamada (Add/AddFeature) de cada feature, agrupando por
    /// (arquivo, linha) para não repetir a edição quando várias features vêm da mesma linha (ex.:
    /// um laço). Não reexecuta; devolve true se algum código mudou.
    /// </summary>
    /// <param name="bottomUp">
    /// Cópias: processa de baixo para cima, para a instrução inserida por uma cópia não deslocar as
    /// linhas das chamadas que ainda faltam no mesmo arquivo.
    /// </param>
    private bool EditFeatureCalls(IEnumerable<IFeature> features, Func<string, int, string> edit, bool bottomUp = false)
    {
        var origins = features
            .Select(FeatureOrigin.Of)
            .Where(o => o != null && o.Line > 0)
            .Select(o => (File: SourceFileOf(o!), o!.Line))
            .Distinct();
        var calls = (bottomUp ? origins.OrderByDescending(c => c.Line) : origins).ToList();

        bool changed = false;
        foreach (var (file, line) in calls)
        {
            string? source = GetDocumentText(file);
            if (source == null) continue;
            try
            {
                string updated = edit(source, line);
                if (updated == source) continue;
                SetDocumentText(file, updated);
                changed = true;
            }
            catch (InvalidOperationException ex)
            {
                _cadViewModel.StatusText = $"✗ {ex.Message}";
            }
        }
        return changed;
    }

    /// <summary>
    /// Move instâncias de peça reescrevendo o <c>at:</c> da chamada que as criou (uma edição por
    /// chamada, mesmo que ela crie várias instâncias num laço). Não reexecuta; devolve true se
    /// algum código mudou.
    /// </summary>
    public bool MovePartInstances(IEnumerable<PartInstance> instances, AvaloniaCadEditor.Domain.ValueObjects.Vector3D offset) =>
        EditFeatureCalls(instances, (source, line) => PartSourceEditor.TranslateInstance(source, line, offset.X, offset.Y, offset.Z));

    /// <summary>
    /// Gira instâncias de peça em torno de <paramref name="pivot"/>/<paramref name="axis"/>,
    /// reescrevendo o <c>at:</c> da chamada que as criou (mesmo padrão de <see cref="MovePartInstances"/>).
    /// </summary>
    public bool RotatePartInstances(IEnumerable<PartInstance> instances, AvaloniaCadEditor.Domain.ValueObjects.Point3D pivot, AvaloniaCadEditor.Domain.ValueObjects.Vector3D axis, double degrees) =>
        EditFeatureCalls(instances, (source, line) => PartSourceEditor.RotateInstance(source, line, pivot, axis, degrees));

    /// <summary>
    /// Copia instâncias de peça: duplica a chamada <c>Add(...)</c> de cada uma e aplica
    /// <paramref name="placeCopy"/> (ex.: <see cref="PartSourceEditor.TranslateInstance"/>) só na cópia.
    /// </summary>
    public bool CopyPartInstances(IEnumerable<PartInstance> instances, Func<string, int, string> placeCopy) =>
        EditFeatureCalls(instances, (source, line) =>
        {
            var (duplicated, copyLine) = PartSourceEditor.DuplicateInstance(source, line);
            return placeCopy(duplicated, copyLine);
        }, bottomUp: true);

    /// <summary>
    /// Move uma feature solta (registrada por <c>AddFeature(x)</c>, não <c>Add(...)</c>), envolvendo
    /// (ou somando a) um <c>x.Transformed(Placement.Translate(...))</c> — sem reescrever as
    /// coordenadas originais de <c>x</c>. Mesmo padrão de <see cref="MovePartInstances"/>.
    /// </summary>
    public bool MoveLooseFeatures(IEnumerable<IFeature> features, AvaloniaCadEditor.Domain.ValueObjects.Vector3D offset) =>
        EditFeatureCalls(features, (source, line) => PartSourceEditor.TranslateFeature(source, line, offset.X, offset.Y, offset.Z));

    /// <summary>Gira uma feature solta em torno de <paramref name="pivot"/>/<paramref name="axis"/> (ver <see cref="MoveLooseFeatures"/>).</summary>
    public bool RotateLooseFeatures(IEnumerable<IFeature> features, AvaloniaCadEditor.Domain.ValueObjects.Point3D pivot, AvaloniaCadEditor.Domain.ValueObjects.Vector3D axis, double degrees) =>
        EditFeatureCalls(features, (source, line) => PartSourceEditor.RotateFeature(source, line, pivot, axis, degrees));

    /// <summary>
    /// Copia uma feature solta: acrescenta <c>var nome = x.Transformed(placementCode, name: "nome"); AddFeature(nome);</c>
    /// logo depois da instrução original (ver <see cref="PartSourceEditor.CopyFeature"/>).
    /// </summary>
    public bool CopyLooseFeatures(IEnumerable<IFeature> features, string placementCode) =>
        EditFeatureCalls(features, (source, line) => PartSourceEditor.CopyFeature(source, line, placementCode).Source, bottomUp: true);

    public bool GoToPartDefinition(string typeName)
    {
        var type = PartSourceEditor.FindPartTypes(CurrentDocuments()).FirstOrDefault(t => t.Name == typeName);
        if (type == null)
        {
            _cadViewModel.StatusText = $"Não achei a declaração de {typeName} nos arquivos da cena.";
            return false;
        }

        if (_project != null && !string.Equals(type.FilePath, _activeFile, StringComparison.OrdinalIgnoreCase))
        {
            SwitchActiveFile(type.FilePath);
        }
        _cadViewModel.SidebarTabIndex = CadViewModel.CodeTabIndex;
        RevealLine = new EditorLineRequest(type.Line);
        _cadViewModel.StatusText = $"{typeName}: {type.FilePath}, linha {type.Line}";
        return true;
    }

    /// <summary>Acrescenta ao arquivo de entrada uma instância da peça, ao lado do que já existe, e executa.</summary>
    public async Task InsertPartAsync(string? typeName)
    {
        var type = _partTypeInfos.FirstOrDefault(t => t.Name == typeName);
        if (type == null)
        {
            _cadViewModel.StatusText = "Escolha uma peça declarada na cena (record que herda de Part).";
            return;
        }

        EnsureEntryDocumentActive();
        string statement = PartSourceEditor.BuildAddStatement(type, NextFreeX());
        string current = GeneratedCSharpCode.StartsWith("// Pressione") ? string.Empty : GeneratedCSharpCode;

        _history.SaveState(GeneratedCSharpCode);
        GeneratedCSharpCode = PartSourceEditor.InsertTopLevelStatement(current, statement);
        if (_project != null && _activeFile != null) _documents[_activeFile] = GeneratedCSharpCode;

        await RunCodeAsync();
        if (HasErrors) return;

        var inserted = _cadViewModel.Sketch.Features.OfType<PartInstance>().LastOrDefault(i => i.PartTypeName == type.Name);
        if (inserted != null)
        {
            _cadViewModel.SelectionService.SelectFeature(inserted);
        }
        _cadViewModel.StatusText = $"✓ Inserido: {statement}";
    }

    private void RefreshPartTypes()
    {
        _partTypeInfos = PartSourceEditor.FindPartTypes(CurrentDocuments());
        var names = _partTypeInfos.Select(t => t.Name).Distinct().ToList();
        if (!names.SequenceEqual(PartTypes))
        {
            PartTypes.Clear();
            foreach (var name in names) PartTypes.Add(name);
            OnPropertyChanged(nameof(HasPartTypes));
        }
        if (SelectedPartType == null || !PartTypes.Contains(SelectedPartType))
        {
            SelectedPartType = PartTypes.FirstOrDefault();
        }
    }

    /// <summary>Fontes atuais da cena (com o texto ainda não salvo do editor).</summary>
    private IReadOnlyList<ScriptDocument> CurrentDocuments()
    {
        if (_project == null)
        {
            return new[] { new ScriptDocument(ScriptDocument.DefaultSceneFile, GeneratedCSharpCode) };
        }
        SyncActiveDocument();
        return _documents
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ScriptDocument(kv.Key, kv.Value))
            .ToList();
    }

    private string SourceFileOf(SourceOrigin origin) =>
        _project != null ? _project.ToRelativePath(origin.FilePath) : ScriptDocument.DefaultSceneFile;

    private string? GetDocumentText(string file)
    {
        if (_project == null)
        {
            return string.Equals(file, ScriptDocument.DefaultSceneFile, StringComparison.OrdinalIgnoreCase) ? GeneratedCSharpCode : null;
        }
        if (string.Equals(file, _activeFile, StringComparison.OrdinalIgnoreCase)) return GeneratedCSharpCode;
        return _documents.TryGetValue(file, out var text) ? text : null;
    }

    private void SetDocumentText(string file, string text)
    {
        if (_project == null || string.Equals(file, _activeFile, StringComparison.OrdinalIgnoreCase))
        {
            _history.SaveState(GeneratedCSharpCode);
            GeneratedCSharpCode = text;
        }
        if (_project != null)
        {
            _documents[file] = text;
        }
    }

    private void SelectFeatureByName(string name)
    {
        var feature = _cadViewModel.Sketch.Features.FirstOrDefault(f => f.Name == name);
        if (feature != null)
        {
            _cadViewModel.SelectionService.SelectFeature(feature);
        }
    }

    /// <summary>X livre à direita do modelo atual, para a peça nova não nascer sobreposta.</summary>
    private double NextFreeX()
    {
        double maxX = double.NegativeInfinity;
        try
        {
            foreach (var feature in _cadViewModel.Sketch.Features)
            {
                if (!feature.IsVisible || feature.IsTransientTool) continue;
                foreach (var v in feature.GetVertices()) maxX = Math.Max(maxX, v.X);
            }
        }
        catch (Exception ex)
        {
            // Só uma heurística de posição: se alguma feature não souber listar vértices, usa a origem.
            Console.WriteLine("Não foi possível medir a cena para posicionar a peça: " + ex.Message);
            return 0;
        }
        return double.IsNegativeInfinity(maxX) ? 0 : Math.Ceiling((maxX + 50) / 10) * 10;
    }

    private static string FormatInvariant(object? value) =>
        value is IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : value?.ToString() ?? "?";

    // ---------------------------------------------------------------- Navegação: F12, Shift+F12, F2

    private string _renameFile = ScriptDocument.DefaultSceneFile;
    private int _renameOffset;
    private bool _isRenaming;
    private string _renameText = string.Empty;
    private string? _renameTitle;
    private string? _renameError;
    private string? _referencesTitle;
    private CodeLocation? _selectedReference;

    /// <summary>Ocorrências do último Shift+F12; clicar numa delas abre o arquivo na linha.</summary>
    public ObservableCollection<CodeLocation> References { get; } = new();

    public bool HasReferences => References.Count > 0;

    public string? ReferencesTitle
    {
        get => _referencesTitle;
        private set { _referencesTitle = value; OnPropertyChanged(); }
    }

    public CodeLocation? SelectedReference
    {
        get => _selectedReference;
        set
        {
            _selectedReference = value;
            OnPropertyChanged();
            if (value != null) NavigateTo(value);
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        private set { _isRenaming = value; OnPropertyChanged(); }
    }

    public string RenameText
    {
        get => _renameText;
        set { _renameText = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string? RenameTitle
    {
        get => _renameTitle;
        private set { _renameTitle = value; OnPropertyChanged(); }
    }

    public string? RenameError
    {
        get => _renameError;
        private set { _renameError = value; OnPropertyChanged(); }
    }

    /// <summary>Arquivo mostrado no editor, como a compilação o conhece.</summary>
    private string CurrentFile => _project != null ? _activeFile ?? _entryFile : ScriptDocument.DefaultSceneFile;

    public bool GoToDefinitionAt(int offset)
    {
        var found = _codeNavigator.FindReferences(CurrentDocuments(), CurrentFile, offset);
        if (found == null)
        {
            _cadViewModel.StatusText = "F12: posicione o cursor sobre um nome.";
            return false;
        }
        if (found.Definitions.Count == 0)
        {
            _cadViewModel.StatusText = $"{found.SymbolName} faz parte da API do AvaloniaCad (veja .cad/api.md).";
            return false;
        }

        var target = found.Definitions[0];
        NavigateTo(target);
        _cadViewModel.StatusText = $"{found.Kind} {found.SymbolName}: {target.FilePath}, linha {target.Line}";
        return true;
    }

    public int FindReferencesAt(int offset)
    {
        var found = _codeNavigator.FindReferences(CurrentDocuments(), CurrentFile, offset);
        References.Clear();
        _selectedReference = null;
        OnPropertyChanged(nameof(SelectedReference));

        if (found == null)
        {
            ReferencesTitle = null;
            OnPropertyChanged(nameof(HasReferences));
            _cadViewModel.StatusText = "Shift+F12: posicione o cursor sobre um nome.";
            return 0;
        }

        foreach (var occurrence in found.Occurrences) References.Add(occurrence);
        int files = found.Occurrences.Select(o => o.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        ReferencesTitle = $"{found.Occurrences.Count} ocorrência(s) de {found.Kind} {found.SymbolName} em {files} arquivo(s)";
        OnPropertyChanged(nameof(HasReferences));
        _cadViewModel.StatusText = ReferencesTitle;
        return found.Occurrences.Count;
    }

    public bool BeginRenameAt(int offset)
    {
        var found = _codeNavigator.FindReferences(CurrentDocuments(), CurrentFile, offset);
        if (found == null)
        {
            _cadViewModel.StatusText = "F2: posicione o cursor sobre um nome para renomear.";
            return false;
        }
        if (!found.IsInScene)
        {
            _cadViewModel.StatusText = $"{found.SymbolName} faz parte da API do AvaloniaCad e não pode ser renomeado.";
            return false;
        }

        _renameFile = CurrentFile;
        _renameOffset = offset;
        RenameTitle = $"Renomear {found.Kind} {found.SymbolName} ({found.Occurrences.Count} ocorrência(s)) para:";
        RenameText = found.SymbolName;
        RenameError = null;
        IsRenaming = true;
        return true;
    }

    /// <summary>Aplica o nome digitado em todos os arquivos e reexecuta. Retorna o erro, ou null.</summary>
    public async Task<string?> ConfirmRenameAsync()
    {
        if (!IsRenaming) return null;

        RenameResult result;
        try
        {
            result = _codeNavigator.Rename(CurrentDocuments(), _renameFile, _renameOffset, RenameText);
        }
        catch (InvalidOperationException ex)
        {
            RenameError = ex.Message;
            return ex.Message;
        }

        IsRenaming = false;
        RenameError = null;
        if (result.ChangedDocuments.Count == 0) return null;

        foreach (var (file, text) in result.ChangedDocuments)
        {
            SetDocumentText(file, text);
        }
        CloseReferences();
        await RunCodeAsync();
        _cadViewModel.StatusText =
            $"✓ {result.OldName} → {result.NewName}: {result.Occurrences} ocorrência(s) em {result.ChangedDocuments.Count} arquivo(s)";
        return null;
    }

    public void CancelRename()
    {
        IsRenaming = false;
        RenameError = null;
    }

    public void CloseReferences()
    {
        References.Clear();
        ReferencesTitle = null;
        _selectedReference = null;
        OnPropertyChanged(nameof(SelectedReference));
        OnPropertyChanged(nameof(HasReferences));
    }

    /// <summary>Abre o arquivo da ocorrência (numa cena com vários) e seleciona o nome.</summary>
    private void NavigateTo(CodeLocation location)
    {
        if (_project != null && !string.Equals(location.FilePath, _activeFile, StringComparison.OrdinalIgnoreCase))
        {
            if (!_documents.ContainsKey(location.FilePath)) return;
            SwitchActiveFile(location.FilePath);
        }
        _cadViewModel.SidebarTabIndex = CadViewModel.CodeTabIndex;
        RevealLine = new EditorLineRequest(location.Line, location.Column, location.Length);
    }

    public async Task<string?> MovePartInstanceAsync(PartInstance instance, AvaloniaCadEditor.Domain.ValueObjects.Vector3D offset)
    {
        string instanceName = instance.Name;
        if (!MovePartInstances(new[] { instance }, offset)) return "Não foi possível mover: a chamada Add(...) não foi encontrada.";

        await RunCodeAsync();
        if (HasErrors) return ErrorSummary;
        SelectFeatureByName(instanceName);
        return null;
    }

    public async Task<string?> SetPartInstanceColorAsync(PartInstance instance, string? color)
    {
        string instanceName = instance.Name;
        if (!EditFeatureCalls(new[] { instance }, (source, line) => PartSourceEditor.SetInstanceColor(source, line, color)))
            return null;

        await RunCodeAsync();
        if (HasErrors) return ErrorSummary;
        SelectFeatureByName(instanceName);
        return null;
    }

    public async Task<string?> RenameFeatureAsync(IFeature feature, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return "O nome não pode ficar vazio.";
        if (newName == feature.Name) return null;
        if (_cadViewModel.Sketch.Features.Any(f => f != feature && f.Name == newName))
            return $"Já existe um elemento chamado \"{newName}\".";

        var origin = FeatureOrigin.Of(feature);
        if (origin == null || origin.Line <= 0) return "Não sei de onde este elemento veio no código.";
        if (feature is PartInstance instance && CountInstancesFromSameCall(instance) > 1)
            return "Esta linha cria várias instâncias; um nome fixo repetiria em todas. Renomeie no código.";

        string file = SourceFileOf(origin);
        string? source = GetDocumentText(file);
        if (source == null) return $"O arquivo {file} não faz parte da cena aberta.";

        string updated;
        try
        {
            updated = feature is PartInstance
                ? PartSourceEditor.RenameInstance(source, origin.Line, newName)
                : PartSourceEditor.RenameFeature(source, origin.Line, feature.Name, newName);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
        if (updated == source) return null;

        SetDocumentText(file, updated);
        await RunCodeAsync();
        if (HasErrors) return ErrorSummary;
        SelectFeatureByName(newName);
        return null;
    }

    // ---------------------------------------------------------------- Edições geradas pelo app

    public void AppendCode(string codeSnippet)
    {
        EnsureEntryDocumentActive();
        _history.SaveState(GeneratedCSharpCode);
        if (GeneratedCSharpCode.StartsWith("// Pressione"))
        {
            GeneratedCSharpCode = string.Empty;
        }

        GeneratedCSharpCode = HoistMovedOperands(GeneratedCSharpCode + codeSnippet);
    }

    /// <summary>
    /// Um sólido movido vira <c>AddFeature(x.Transformed(P))</c>; se o código novo (ex.: uma booleana)
    /// passa a usar <c>x</c>, o <c>P</c> sobe para a declaração de <c>x</c> — senão a operação usaria o
    /// sólido na posição original (ver <see cref="PartSourceEditor.HoistTransformedAddFeatures"/>).
    /// </summary>
    private static string HoistMovedOperands(string code)
    {
        try
        {
            return PartSourceEditor.HoistTransformedAddFeatures(code);
        }
        catch (Exception)
        {
            return code;
        }
    }

    public void RemoveCodeForFeature(string variableName)
    {
        EnsureEntryDocumentActive();
        _history.SaveState(GeneratedCSharpCode);
        if (string.IsNullOrWhiteSpace(variableName)) return;

        GeneratedCSharpCode = RoslynFeatureRemover.RemoveFeatureCode(GeneratedCSharpCode, variableName);
    }

    public void MutateCoordinates(IEnumerable<string> rootVariables, AvaloniaCadEditor.Domain.ValueObjects.Vector3D translation)
    {
        EnsureEntryDocumentActive();
        _history.SaveState(GeneratedCSharpCode);
        GeneratedCSharpCode = RoslynCoordinateMutator.MutateCoordinates(GeneratedCSharpCode, rootVariables, translation);
    }

    /// <summary>
    /// Acrescenta uma ou mais ferramentas a uma <c>BooleanFeature</c> já existente no script (ver
    /// <see cref="RoslynBooleanOperandAppender"/>), em vez de encadear uma nova operação em cima do
    /// resultado. Devolve <c>false</c> se o código não estiver num formato reconhecido; nesse caso o
    /// script não é alterado e quem chamou deve cair para o caminho antigo (gerar uma feature nova).
    /// </summary>
    public bool TryAppendBooleanTool(string booleanVar, IReadOnlyList<string> toolVars)
    {
        if (!RoslynBooleanOperandAppender.TryAppendTools(GeneratedCSharpCode, booleanVar, toolVars, out var result))
        {
            return false;
        }

        EnsureEntryDocumentActive();
        _history.SaveState(GeneratedCSharpCode);
        GeneratedCSharpCode = HoistMovedOperands(result);
        return true;
    }

    /// <summary>
    /// Aplica um código gerado por fonte externa (ex.: o chat de IA) como o script vigente,
    /// preservando o histórico de Undo/Redo, e o executa imediatamente.
    /// </summary>
    public async Task ApplyExternalCode(string code)
    {
        EnsureEntryDocumentActive();
        _history.SaveState(GeneratedCSharpCode);
        GeneratedCSharpCode = code;
        await RunCodeAsync();
    }

    public void Undo()
    {
        if (_history.TryUndo(GeneratedCSharpCode, out var previousCode))
        {
            GeneratedCSharpCode = previousCode;
            RunCodeCommand.Execute(null);
        }
    }

    public void Redo()
    {
        if (_history.TryRedo(GeneratedCSharpCode, out var nextCode))
        {
            GeneratedCSharpCode = nextCode;
            RunCodeCommand.Execute(null);
        }
    }
}
