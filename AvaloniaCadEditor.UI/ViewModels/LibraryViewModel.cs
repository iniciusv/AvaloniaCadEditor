using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Library;
using AvaloniaCadEditor.Application.Scenes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>Filtro de origem da aba Biblioteca.</summary>
public enum LibrarySourceFilter { All, Scene, Library }

/// <summary>
/// A aba Biblioteca: busca, filtro de origem, lista por categoria com miniatura e o detalhe da peça escolhida,
/// com parâmetros, modelos prontos e o código que será gravado. A regra (catálogo, cópia com dependências,
/// código da instrução, miniatura) mora em <c>Application/Library</c>; aqui só se apresenta e se delega.
/// Inserir passa pelo <see cref="SceneScriptViewModel"/>, dono dos arquivos da cena.
/// </summary>
public sealed class LibraryViewModel : ObservableObject
{
    private readonly SceneScriptViewModel _script;
    private readonly IPartLibraryStore _store;
    private readonly PartThumbnailService? _thumbnails;
    private readonly IFilePickerService? _filePicker;
    private readonly Action<Action> _uiInvoker;
    private IReadOnlyDictionary<string, PartLibraryState> _states = new Dictionary<string, PartLibraryState>();

    private IReadOnlyList<ScriptDocument> _libraryDocuments = Array.Empty<ScriptDocument>();
    private IReadOnlyList<ScriptDocument> _sceneDocuments = Array.Empty<ScriptDocument>();
    private List<LibraryItemViewModel> _items = new();
    private CancellationTokenSource? _thumbnailCts;
    private string _searchText = string.Empty;
    private LibrarySourceFilter _filter = LibrarySourceFilter.All;
    private object? _selectedRow;
    private LibraryItemViewModel? _selected;

    public LibraryViewModel(
        SceneScriptViewModel script,
        IPartLibraryStore store,
        PartThumbnailService? thumbnails = null,
        IFilePickerService? filePicker = null,
        Action<Action>? uiInvoker = null)
    {
        _script = script;
        _store = store;
        _thumbnails = thumbnails;
        _filePicker = filePicker;
        _uiInvoker = uiInvoker ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));

        RefreshCommand = new RelayCommand(Refresh);
        ChangeFolderCommand = new RelayCommand(async () => await ChangeFolderAsync());
        InsertCommand = new RelayCommand(async () => await InsertAsync());
        SendToLibraryCommand = new RelayCommand(async () => { if (_selected != null) await SendToLibraryAsync(_selected.Name); });
        UpdateFromLibraryCommand = new RelayCommand(async () => { if (_selected != null) await UpdateFromLibraryAsync(_selected.Name); });
        CiteCommand = new RelayCommand(() => { if (_selected != null) CiteInChat?.Invoke(_selected.Item); });
        SetFilterCommand = new RelayCommand<string>(f => Filter = Enum.TryParse<LibrarySourceFilter>(f, out var v) ? v : LibrarySourceFilter.All);

        _script.SceneDocumentsChanged += (_, _) => ReloadScene();
        _libraryDocuments = _store.ReadDocuments();
        _sceneDocuments = _script.GetSceneDocuments();
        Rebuild();
    }

    public ICommand RefreshCommand { get; }
    public ICommand ChangeFolderCommand { get; }
    public ICommand InsertCommand { get; }
    public ICommand SetFilterCommand { get; }
    public ICommand SendToLibraryCommand { get; }
    public ICommand UpdateFromLibraryCommand { get; }
    public ICommand CiteCommand { get; }

    /// <summary>"Citar no chat": ligado ao chat da IA na composição. Null sem chat (testes).</summary>
    public Action<PartLibraryItem>? CiteInChat { get; set; }

    /// <summary>Os arquivos atuais da pasta da biblioteca (o chat lê as peças citadas daqui).</summary>
    public IReadOnlyList<ScriptDocument> LibraryDocuments => _libraryDocuments;

    /// <summary>
    /// Pasta da biblioteca para uma peça nova enviada da cena: <c>Projetos/(nome da cena)</c>. Depois ela pode
    /// ser movida para outra subpasta à mão; o que liga as cópias é o nome do tipo, não o caminho.
    /// </summary>
    public string FolderForNewParts => "Projetos/" + (_script.ProjectName ?? "Cena");

    /// <summary>
    /// Enviar a peça da cena para a biblioteca, com as peças de que ela depende (<see cref="PartLibrarySync.PlanSend"/>).
    /// Substitui o arquivo da biblioteca que já a declara: o usuário mandou, e a pasta é versionável.
    /// </summary>
    public Task SendToLibraryAsync(string typeName)
    {
        var scene = _script.GetSceneDocuments();
        var library = _store.ReadDocuments();
        var plan = PartLibrarySync.PlanSend(scene, library, typeName, FolderForNewParts, _script.EntryFile);
        if (plan.Refusal != null)
        {
            _script.ReportStatus($"✗ {plan.Refusal}");
            return Task.CompletedTask;
        }
        if (plan.Writes.Count == 0)
        {
            _script.ReportStatus($"{typeName} já está igual na biblioteca.");
            return Task.CompletedTask;
        }

        try
        {
            foreach (var write in plan.Writes) _store.Write(write.FilePath, write.Text);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _script.ReportStatus($"✗ Não consegui gravar na biblioteca: {ex.Message}");
            Refresh();
            return Task.CompletedTask;
        }

        Refresh();
        string warnings = plan.Warnings.Count > 0 ? " ⚠ " + string.Join(" ", plan.Warnings) : string.Empty;
        _script.ReportStatus($"✓ Enviado para a biblioteca: {string.Join(", ", plan.Writes.Select(w => w.FilePath))}{warnings}");
        return Task.CompletedTask;
    }

    /// <summary>Trazer a versão da biblioteca para a cena (Ctrl+Z desfaz).</summary>
    public async Task UpdateFromLibraryAsync(string typeName)
    {
        _libraryDocuments = _store.ReadDocuments();
        await _script.UpdatePartFromLibraryAsync(typeName, _libraryDocuments);
    }

    /// <summary>Linhas da lista: <see cref="LibraryCategoryRow"/> e <see cref="LibraryItemViewModel"/>, já filtradas.</summary>
    public ObservableCollection<object> Rows { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? string.Empty)) ApplyFilter(); }
    }

    public LibrarySourceFilter Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value)) return;
            OnPropertyChanged(nameof(IsFilterAll));
            OnPropertyChanged(nameof(IsFilterScene));
            OnPropertyChanged(nameof(IsFilterLibrary));
            ApplyFilter();
        }
    }

    public bool IsFilterAll { get => _filter == LibrarySourceFilter.All; set { if (value) Filter = LibrarySourceFilter.All; } }
    public bool IsFilterScene { get => _filter == LibrarySourceFilter.Scene; set { if (value) Filter = LibrarySourceFilter.Scene; } }
    public bool IsFilterLibrary { get => _filter == LibrarySourceFilter.Library; set { if (value) Filter = LibrarySourceFilter.Library; } }

    /// <summary>A linha clicada. Cabeçalho de categoria não seleciona nada.</summary>
    public object? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (value is LibraryCategoryRow)
            {
                OnPropertyChanged();
                return;
            }
            if (SetProperty(ref _selectedRow, value)) Selected = value as LibraryItemViewModel;
        }
    }

    public LibraryItemViewModel? Selected
    {
        get => _selected;
        private set
        {
            if (SetProperty(ref _selected, value)) OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selected != null;

    /// <summary>Todos os itens, sem filtro (testes e contagem).</summary>
    public IReadOnlyList<LibraryItemViewModel> Items => _items;

    public string LibraryPath => _store.RootPath;

    /// <summary>Por que a lista está vazia, ou null.</summary>
    public string? EmptyMessage
    {
        get
        {
            if (Rows.Count > 0) return null;
            if (_items.Any(InFilter)) return "Nenhuma peça com esse texto. A busca olha o nome, a categoria e a descrição.";
            return _filter == LibrarySourceFilter.Scene
                ? "A cena não declara peças (records que herdam de Part)."
                : $"A biblioteca está vazia. Ponha os .cs das peças em {_store.RootPath} (subpasta = categoria) ou troque a pasta.";
        }
    }

    /// <summary>Relê a pasta da biblioteca e os fontes da cena.</summary>
    public void Refresh()
    {
        _libraryDocuments = _store.ReadDocuments();
        _sceneDocuments = _script.GetSceneDocuments();
        Rebuild();
    }

    /// <summary>Depois de cada execução: só remonta a lista se algum fonte da cena mudou.</summary>
    private void ReloadScene()
    {
        var documents = _script.GetSceneDocuments();
        if (documents.SequenceEqual(_sceneDocuments)) return;
        _sceneDocuments = documents;
        Rebuild();
    }

    private async Task ChangeFolderAsync()
    {
        if (_filePicker == null) return;
        var folder = await _filePicker.OpenFolderAsync("Pasta da biblioteca de peças");
        if (string.IsNullOrEmpty(folder)) return;
        _store.SetRootPath(folder);
        OnPropertyChanged(nameof(LibraryPath));
        Refresh();
    }

    private async Task InsertAsync()
    {
        var item = _selected;
        if (item == null) return;
        string? error = item.ValidationError;
        if (error != null)
        {
            _script.ReportStatus($"✗ {error}");
            return;
        }
        await _script.InsertLibraryPartAsync(item.Item, _libraryDocuments, item.PartExpression);
    }

    /// <summary>Remonta os itens das duas origens, mantendo o que o usuário já tinha digitado no item igual.</summary>
    private void Rebuild()
    {
        var sceneItems = PartLibraryCatalog.Read(_sceneDocuments, PartLibrarySource.Scene);
        var libraryItems = PartLibraryCatalog.Read(_libraryDocuments, PartLibrarySource.Library);
        _states = PartLibrarySync.Compare(_sceneDocuments, _libraryDocuments);

        var previous = _items.ToDictionary(i => (i.Item.Source, i.Name, i.Item.FilePath));
        var items = new List<LibraryItemViewModel>();
        foreach (var item in sceneItems.Concat(libraryItems))
        {
            var state = _states.TryGetValue(item.Name, out var known) ? known
                : item.Source == PartLibrarySource.Scene ? PartLibraryState.SceneOnly : PartLibraryState.LibraryOnly;
            if (previous.TryGetValue((item.Source, item.Name, item.FilePath), out var old) && old.Item.Parameters.SequenceEqual(item.Parameters))
            {
                old.Update(item, state);
                items.Add(old);
            }
            else
            {
                items.Add(new LibraryItemViewModel(item, state));
            }
        }
        _items = items;
        OnPropertyChanged(nameof(Items));
        ApplyFilter();
        StartThumbnails();
    }

    private void ApplyFilter()
    {
        string query = _searchText.Trim();
        var visible = _items.Where(InFilter)
            .Where(i => query.Length == 0 || i.Matches(query))
            .ToList();

        var keep = _selected;
        Rows.Clear();
        string? group = null;
        foreach (var item in visible)
        {
            if (item.GroupTitle != group)
            {
                group = item.GroupTitle;
                Rows.Add(new LibraryCategoryRow(group, visible.Count(i => i.GroupTitle == group)));
            }
            Rows.Add(item);
        }

        if (keep != null && visible.Contains(keep))
        {
            _selectedRow = keep;
            OnPropertyChanged(nameof(SelectedRow));
        }
        else
        {
            SelectedRow = visible.FirstOrDefault();
        }
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private bool InFilter(LibraryItemViewModel item) => _filter switch
    {
        LibrarySourceFilter.Scene => item.Item.Source == PartLibrarySource.Scene,
        LibrarySourceFilter.Library => item.Item.Source == PartLibrarySource.Library,
        _ => true
    };

    /// <summary>Pede a miniatura de cada item, uma por vez, em segundo plano. Uma nova lista cancela a anterior.</summary>
    private void StartThumbnails()
    {
        if (_thumbnails == null) return;
        _thumbnailCts?.Cancel();
        var cts = _thumbnailCts = new CancellationTokenSource();
        var items = _items.ToList();
        var scene = _sceneDocuments;
        var library = _libraryDocuments;

        _ = Task.Run(async () =>
        {
            var sceneIndex = PartDependencyIndex.Of(scene);
            var libraryIndex = PartDependencyIndex.Of(library);
            foreach (var item in items)
            {
                if (cts.IsCancellationRequested) return;
                try
                {
                    var png = await _thumbnails.GetAsync(item.Item,
                        item.Item.Source == PartLibrarySource.Scene ? sceneIndex : libraryIndex, cts.Token);
                    if (png != null && !cts.IsCancellationRequested) _uiInvoker(() => item.ThumbnailPng = png);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Biblioteca] Miniatura de {item.Name}: {ex.Message}");
                }
            }
        });
    }
}

/// <summary>Cabeçalho de categoria na lista.</summary>
public sealed record LibraryCategoryRow(string Title, int Count);

/// <summary>Um item da lista e o seu detalhe (parâmetros, modelos prontos, código).</summary>
public sealed class LibraryItemViewModel : ObservableObject
{
    private PartLibraryItem _item;
    private PartLibraryState _state;
    private string? _preset;
    private byte[]? _thumbnailPng;

    public LibraryItemViewModel(PartLibraryItem item, PartLibraryState state)
    {
        _item = item;
        _state = state;
        var initial = PartLibraryInsertion.InitialValues(item);
        Parameters = item.Parameters.Select(p => new LibraryParameterViewModel(p, initial[p.Name], OnParameterEdited)).ToList();
        Presets = item.Presets.Select(p => new LibraryPresetViewModel(p, this)).ToList();
        ChoosePresetCommand = new RelayCommand<string>(ChoosePreset);
    }

    public PartLibraryItem Item => _item;
    public string Name => _item.Name;
    public string Summary => string.IsNullOrEmpty(_item.Summary) ? "Sem descrição (<summary>) no arquivo." : _item.Summary;
    public string ShortSummary => _item.ShortSummary;

    /// <summary>Onde o arquivo está: <c>Pecas/Rolamento.cs</c> na cena, <c>Biblioteca/Perfis/Extrusao2020.cs</c> na biblioteca.</summary>
    public string Location => _item.Source == PartLibrarySource.Scene ? _item.FilePath : "Biblioteca/" + _item.FilePath;

    public string GroupTitle => _item.Source == PartLibrarySource.Scene
        ? "Nesta cena"
        : string.IsNullOrEmpty(_item.Category) ? "Minha biblioteca" : _item.Category;

    public PartLibraryState State => _state;

    /// <summary>
    /// Marca da linha: "na cena" para peça da biblioteca que a cena já tem igual; "alterada" nos dois lados quando
    /// a cópia da cena difere da biblioteca.
    /// </summary>
    public string? Tag => _state switch
    {
        PartLibraryState.Altered => "alterada",
        PartLibraryState.Same when _item.Source == PartLibrarySource.Library => "na cena",
        _ => null
    };
    public bool HasTag => Tag != null;
    public bool IsAltered => _state == PartLibraryState.Altered;

    public string InsertLabel => NeedsCopy ? "Inserir na cena (copia o arquivo)" : "Inserir na cena";

    /// <summary>Peça da cena que a biblioteca não tem, ou que está diferente lá.</summary>
    public bool CanSendToLibrary => _state is PartLibraryState.SceneOnly or PartLibraryState.Altered;

    public string SendLabel => _state == PartLibraryState.Altered ? "Enviar para a biblioteca (substitui)" : "Enviar para a biblioteca";

    /// <summary>A cópia da cena difere da biblioteca: dá para trazer a versão de lá.</summary>
    public bool CanUpdateFromLibrary => _state == PartLibraryState.Altered;

    private bool NeedsCopy => _item.Source == PartLibrarySource.Library && _state == PartLibraryState.LibraryOnly;

    public IReadOnlyList<LibraryParameterViewModel> Parameters { get; }
    public bool HasParameters => Parameters.Count > 0;
    public IReadOnlyList<LibraryPresetViewModel> Presets { get; }
    public bool HasPresets => Presets.Count > 0;
    public ICommand ChoosePresetCommand { get; }

    public string? SelectedPreset => _preset;

    public byte[]? ThumbnailPng
    {
        get => _thumbnailPng;
        set { if (SetProperty(ref _thumbnailPng, value)) OnPropertyChanged(nameof(HasThumbnail)); }
    }

    public bool HasThumbnail => _thumbnailPng != null;

    public string PartExpression => PartLibraryInsertion.PartExpression(_item, Values(), _preset);

    /// <summary>O código que "Inserir" grava no arquivo de entrada (a posição em X é escolhida na hora).</summary>
    public string CodePreview
    {
        get
        {
            string statement = PartLibraryInsertion.AddStatement(PartExpression);
            return NeedsCopy
                ? $"// copia {_item.FilePath} para {PartLibraryImporter.TargetPath(_item.FilePath)}\n{statement}"
                : statement;
        }
    }

    /// <summary>O primeiro valor inválido, ou null.</summary>
    public string? ValidationError => Parameters.Select(p => p.Error).FirstOrDefault(e => e != null);

    public bool Matches(string query) =>
        (_item.Name + " " + _item.Summary + " " + _item.Category).Contains(query, StringComparison.OrdinalIgnoreCase);

    internal void Update(PartLibraryItem item, PartLibraryState state)
    {
        _item = item;
        _state = state;
        OnPropertyChanged(string.Empty);
    }

    private void ChoosePreset(string? name)
    {
        if (name == null || _item.Presets.All(p => p.Name != name)) return;
        _preset = name;
        var values = PartLibraryInsertion.InitialValues(_item, name);
        foreach (var parameter in Parameters) parameter.SetSilently(values[parameter.Name]);
        foreach (var preset in Presets) preset.Refresh();
        OnPropertyChanged(nameof(SelectedPreset));
        OnEdited();
    }

    private void OnParameterEdited()
    {
        // Um valor digitado à mão desfaz a escolha do modelo pronto só se sair dele: PartExpression decide.
        OnEdited();
    }

    private void OnEdited()
    {
        OnPropertyChanged(nameof(PartExpression));
        OnPropertyChanged(nameof(CodePreview));
        OnPropertyChanged(nameof(ValidationError));
    }

    private IReadOnlyDictionary<string, string> Values() =>
        Parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
}

/// <summary>Um parâmetro editável no detalhe.</summary>
public sealed class LibraryParameterViewModel : ObservableObject
{
    private readonly Action _edited;
    private string _value;

    public LibraryParameterViewModel(PartTypeParameter parameter, string value, Action edited)
    {
        Parameter = parameter;
        _value = value;
        _edited = edited;
    }

    public PartTypeParameter Parameter { get; }
    public string Name => Parameter.Name;
    public string TypeName => Parameter.TypeName;
    public bool IsRequired => Parameter.DefaultValue == null;
    public string Tooltip => IsRequired
        ? $"{Parameter.TypeName}, obrigatório"
        : $"{Parameter.TypeName}, padrão {Parameter.DefaultValue}";

    public string Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(Error));
            _edited();
        }
    }

    public string? Error => PartLibraryInsertion.Validate(Parameter, _value);

    internal void SetSilently(string value)
    {
        _value = value;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Error));
    }
}

/// <summary>Um modelo pronto, como botão.</summary>
public sealed class LibraryPresetViewModel : ObservableObject
{
    private readonly LibraryItemViewModel _owner;

    public LibraryPresetViewModel(PartLibraryPreset preset, LibraryItemViewModel owner)
    {
        Preset = preset;
        _owner = owner;
    }

    public PartLibraryPreset Preset { get; }
    public string Name => Preset.Name;
    public string Tooltip => string.IsNullOrEmpty(Preset.Summary) ? Preset.Name : Preset.Summary;
    public bool IsChosen => _owner.SelectedPreset == Preset.Name;
    public ICommand Command => _owner.ChoosePresetCommand;

    internal void Refresh() => OnPropertyChanged(nameof(IsChosen));
}
