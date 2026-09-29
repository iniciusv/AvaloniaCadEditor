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
public class SceneScriptViewModel : INotifyPropertyChanged
{
    private const int ReloadDebounceMs = 300;

    private readonly ISceneEvaluator? _sceneEvaluator;
    private readonly IFilePickerService? _filePickerService;
    private readonly CadViewModel _cadViewModel;
    private readonly CodeHistoryManager _history = new();
    private readonly Action<Action> _uiInvoker;

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
        Action<Action>? uiInvoker = null)
    {
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
    }

    public ICommand GenerateCodeCommand { get; }
    public ICommand RunCodeCommand { get; }
    public ICommand LoadCodeFromFileCommand { get; }
    public ICommand SaveCodeToFileCommand { get; }
    public ICommand NewSceneCommand { get; }
    public ICommand OpenSceneCommand { get; }
    public ICommand CreateFileCommand { get; }
    public ICommand GoToFirstErrorCommand { get; }

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

    /// <summary>Mostra no editor o arquivo onde a feature foi registrada (quando é outro arquivo da cena).</summary>
    public void RevealFeature(IFeature? feature)
    {
        if (_project == null || feature == null) return;
        var origin = FeatureOrigin.Of(feature);
        if (origin == null || string.IsNullOrEmpty(origin.FilePath)) return;

        string file = _project.ToRelativePath(origin.FilePath);
        if (_documents.ContainsKey(file) && !string.Equals(file, _activeFile, StringComparison.OrdinalIgnoreCase))
        {
            SwitchActiveFile(file);
        }
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

        if (result.Success)
        {
            _cadViewModel.MeshManager.ClearCache();
            _cadViewModel.Sketch.ClearFeatures();
            foreach (var feature in result.Features)
            {
                _cadViewModel.Sketch.AddFeature(feature);
            }
            _cadViewModel.ModelTree.RebuildTree();
            _cadViewModel.RefreshProfiles();
            _cadViewModel.RefreshSolidMesh();
            _cadViewModel.StatusText = _project != null
                ? $"✓ {_project.Name}: {result.Features.Count} feature(s) em {_documents.Count} arquivo(s)"
                : $"✓ Código executado: {result.Features.Count} feature(s)";
            SetErrors(Array.Empty<ScriptDiagnostic>());
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
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        GeneratedCSharpCode += codeSnippet;
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
