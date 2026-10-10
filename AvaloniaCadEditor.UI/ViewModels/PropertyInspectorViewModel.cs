using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.UI.ViewModels;

public class PropertyInspectorViewModel : INotifyPropertyChanged
{
    private readonly ISelectionService _selectionService;
    private readonly Action _onModelParametricModified;
    private readonly Func<IPartCodeEditor?> _partCodeEditor;

    /// <param name="partCodeEditor">
    /// Acesso ao editor de código, usado para ler e reescrever os parâmetros de peças. É uma função
    /// porque o editor é criado depois do inspector.
    /// </param>
    public PropertyInspectorViewModel(ISelectionService selectionService, Action onModelParametricModified,
        Func<IPartCodeEditor?>? partCodeEditor = null)
    {
        _selectionService = selectionService;
        _onModelParametricModified = onModelParametricModified;
        _partCodeEditor = partCodeEditor ?? (() => null);
        _selectionService.SelectionChanged += RefreshProperties;
        _colorWriteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _colorWriteTimer.Tick += async (_, _) => await WritePartColorAsync();
        ResetPartColorCommand = new RelayCommand(() =>
        {
            _pendingColor = null;
            _colorWriteTimer.Stop();
            _colorWriteTimer.Start();
        });
        RenameCommand = new RelayCommand(async () => await RenameAsync());
        ConvertToPartCommand = new RelayCommand(async () => await ConvertToPartAsync());
        MergeIntoPartCommand = new RelayCommand(async () => await MergeIntoPartAsync());
        OpenPartDefinitionCommand = new RelayCommand(() =>
        {
            if (_selectionService.SelectedObject is PartInstance instance)
            {
                _partCodeEditor()?.GoToPartDefinition(instance.PartTypeName);
            }
        });
    }

    // --- Peças paramétricas ---
    private bool _isPartInstance;
    public bool IsPartInstance
    {
        get => _isPartInstance;
        private set { _isPartInstance = value; OnPropertyChanged(); }
    }

    /// <summary>Parâmetros da peça selecionada; editar e apertar Enter reescreve o código.</summary>
    public ObservableCollection<PartParameterItem> PartParameters { get; } = new();

    private string _partNote = "";
    /// <summary>De onde a instância veio no código e quantas instâncias a mesma linha cria.</summary>
    public string PartNote
    {
        get => _partNote;
        private set { _partNote = value; OnPropertyChanged(); }
    }

    public ICommand OpenPartDefinitionCommand { get; }

    // --- Nome: editar + Enter reescreve o nome no código e reexecuta ---
    private bool _isFeature;
    public bool IsFeature
    {
        get => _isFeature;
        private set { _isFeature = value; OnPropertyChanged(); }
    }

    private string _featureName = "";
    public string FeatureName
    {
        get => _featureName;
        set { _featureName = value ?? ""; OnPropertyChanged(); }
    }

    private string? _nameError;
    public string? NameError
    {
        get => _nameError;
        private set { _nameError = value; OnPropertyChanged(); }
    }

    public ICommand RenameCommand { get; }

    /// <summary>Nome atual da feature selecionada (o que o botão de copiar copia).</summary>
    public string? SelectedFeatureName => (_selectionService.SelectedObject as IFeature)?.Name;

    private async System.Threading.Tasks.Task RenameAsync()
    {
        if (_selectionService.SelectedObject is not IFeature feature) return;
        if (_partCodeEditor() is not { } editor)
        {
            NameError = "Editor de código indisponível.";
            return;
        }
        NameError = await editor.RenameFeatureAsync(feature, FeatureName);
    }

    // --- Converter em peça: um sólido solto vira um record em Pecas/ ---
    private bool _canConvertToPart;
    public bool CanConvertToPart
    {
        get => _canConvertToPart;
        private set { _canConvertToPart = value; OnPropertyChanged(); }
    }

    private string _newPartTypeName = "";
    public string NewPartTypeName
    {
        get => _newPartTypeName;
        set { _newPartTypeName = value ?? ""; OnPropertyChanged(); }
    }

    private string? _convertError;
    public string? ConvertError
    {
        get => _convertError;
        private set { _convertError = value; OnPropertyChanged(); }
    }

    public ICommand ConvertToPartCommand { get; }

    private async System.Threading.Tasks.Task ConvertToPartAsync()
    {
        if (_selectionService.SelectedObject is not IFeature feature) return;
        if (_partCodeEditor() is not { } editor)
        {
            ConvertError = "Editor de código indisponível.";
            return;
        }
        ConvertError = await editor.ConvertToPartAsync(feature, NewPartTypeName);
    }

    // --- Incorporar numa peça existente: o sólido solto vai para o Build() de uma peça da cena ---
    /// <summary>Uma instância de peça que pode receber o sólido.</summary>
    public sealed record MergeTargetItem(PartInstance Instance, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Um modo de incorporação, com o texto do combo.</summary>
    public sealed record MergeModeItem(PartMergeMode Mode, string Label, string Hint)
    {
        public override string ToString() => Label;
    }

    public ObservableCollection<MergeTargetItem> MergeTargets { get; } = new();

    public static IReadOnlyList<MergeModeItem> MergeModes { get; } = new[]
    {
        new MergeModeItem(PartMergeMode.UnionAfterCuts, "Unir depois dos cortes",
            "Une ao resultado final da peça: os vazios que ela já tem não apagam o sólido novo."),
        new MergeModeItem(PartMergeMode.UnionBeforeCuts, "Unir antes dos cortes",
            "Entra nas bases da CreateDifference final: os vazios da peça também furam o sólido novo."),
        new MergeModeItem(PartMergeMode.SubtractAsTool, "Subtrair (furo/rasgo)",
            "O sólido vira ferramenta de corte da peça.")
    };

    private bool _canMergeIntoPart;
    public bool CanMergeIntoPart
    {
        get => _canMergeIntoPart;
        private set { _canMergeIntoPart = value; OnPropertyChanged(); }
    }

    private MergeTargetItem? _selectedMergeTarget;
    public MergeTargetItem? SelectedMergeTarget
    {
        get => _selectedMergeTarget;
        set { _selectedMergeTarget = value; OnPropertyChanged(); }
    }

    private MergeModeItem _selectedMergeMode = MergeModes[0];
    public MergeModeItem SelectedMergeMode
    {
        get => _selectedMergeMode;
        set { _selectedMergeMode = value ?? MergeModes[0]; OnPropertyChanged(); }
    }

    private string? _mergeError;
    public string? MergeError
    {
        get => _mergeError;
        private set { _mergeError = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// True quando a seleção é o par "sólido solto + instância de peça" (Ctrl+clique): o alvo já está
    /// decidido pela própria seleção, então o combo de peças some e fica só o modo e o botão.
    /// </summary>
    private bool _isMergePair;
    public bool IsMergePair
    {
        get => _isMergePair;
        private set { _isMergePair = value; OnPropertyChanged(); }
    }

    private string _mergeCaption = "";
    /// <summary>O que vai acontecer, no modo par: "Incorporar dente em bloco (BlocoBelt)".</summary>
    public string MergeCaption
    {
        get => _mergeCaption;
        private set { _mergeCaption = value; OnPropertyChanged(); }
    }

    public ICommand MergeIntoPartCommand { get; }

    /// <summary>
    /// O sólido solto a incorporar. Não é sempre a seleção primária: no par, a primária é o último
    /// Ctrl+clique, que pode ter sido a peça.
    /// </summary>
    private IFeature? _mergeSolid;

    private async System.Threading.Tasks.Task MergeIntoPartAsync()
    {
        if (_mergeSolid is not { } feature || SelectedMergeTarget is not { } target) return;
        if (_partCodeEditor() is not { } editor)
        {
            MergeError = "Editor de código indisponível.";
            return;
        }
        MergeError = await editor.MergeIntoPartAsync(feature, target.Instance, SelectedMergeMode.Mode);
    }

    private static bool IsLooseSolid(IFeature feature) => feature is not PartInstance and not WorkPlaneFeature;

    /// <summary>
    /// Prepara a incorporação para a seleção atual:
    /// <list type="bullet">
    ///   <item>um sólido solto + uma instância de peça (Ctrl+clique, em qualquer ordem): o alvo é essa peça;</item>
    ///   <item>só um sólido solto: lista as peças da cena, já com a mais próxima dele selecionada (a que o
    ///   envolve ou encosta nele costuma ser a certa).</item>
    /// </list>
    /// </summary>
    private void RefreshMergeTargets(IFeature? primary)
    {
        MergeTargets.Clear();
        SelectedMergeTarget = null;
        MergeError = null;
        _mergeSolid = null;
        IsMergePair = false;
        MergeCaption = "";
        CanMergeIntoPart = false;
        if (primary == null || _partCodeEditor() is not { } editor) return;

        var selected = editor.SelectedSceneFeatures();
        if (selected.Count == 2 && selected.Count(f => f is PartInstance) == 1 && selected.Count(IsLooseSolid) == 1)
        {
            var instance = selected.OfType<PartInstance>().Single();
            _mergeSolid = selected.Single(IsLooseSolid);
            var item = new MergeTargetItem(instance, $"{instance.Name} ({instance.PartTypeName})");
            MergeTargets.Add(item);
            SelectedMergeTarget = item;
            MergeCaption = $"Incorporar {_mergeSolid.Name} em {item.Label}";
            IsMergePair = true;
            CanMergeIntoPart = true;
            return;
        }

        if (selected.Count > 1 || !IsLooseSolid(primary)) return;
        _mergeSolid = primary;

        var solidBox = Bounds(primary);
        var ranked = editor.ScenePartInstances()
            .Select(i => (Instance: i, Distance: solidBox is { } a && Bounds(i) is { } b ? Gap(a, b) : double.MaxValue))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Instance.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var (instance, _) in ranked)
            MergeTargets.Add(new MergeTargetItem(instance, $"{instance.Name} ({instance.PartTypeName})"));

        SelectedMergeTarget = MergeTargets.FirstOrDefault();
        CanMergeIntoPart = MergeTargets.Count > 0;
    }

    private static (Point3D Min, Point3D Max)? Bounds(IFeature feature)
    {
        try
        {
            var vertices = feature.GetVertices();
            if (vertices.Count == 0) return null;
            return (new Point3D(vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Min(v => v.Z)),
                    new Point3D(vertices.Max(v => v.X), vertices.Max(v => v.Y), vertices.Max(v => v.Z)));
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or NotImplementedException)
        {
            return null;
        }
    }

    /// <summary>Distância entre duas caixas alinhadas aos eixos (0 se se tocam ou se sobrepõem).</summary>
    private static double Gap((Point3D Min, Point3D Max) a, (Point3D Min, Point3D Max) b)
    {
        double dx = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        double dy = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        double dz = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Sugestão de nome do tipo: <c>bloco_fixacao</c> vira <c>BlocoFixacao</c>.</summary>
    private static string SuggestedTypeName(string featureName) =>
        string.Concat(featureName.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    // --- Cor da peça: o seletor muda muito rápido ao arrastar, então o código só é reescrito
    // depois de uma pausa (o timer), numa única reexecução.
    private static readonly Color DefaultPartColor = Color.Parse("#EBEBF2");
    private readonly DispatcherTimer _colorWriteTimer;
    private string? _pendingColor;
    private Color _partColor = DefaultPartColor;

    public Color PartColor
    {
        get => _partColor;
        set
        {
            if (_partColor == value) return;
            _partColor = value;
            OnPropertyChanged();
            _pendingColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            _colorWriteTimer.Stop();
            _colorWriteTimer.Start();
        }
    }

    private bool _hasPartColor;
    /// <summary>A instância tem <c>color:</c> próprio (mostra o botão de voltar ao padrão).</summary>
    public bool HasPartColor
    {
        get => _hasPartColor;
        private set { _hasPartColor = value; OnPropertyChanged(); }
    }

    public ICommand ResetPartColorCommand { get; }

    private async System.Threading.Tasks.Task WritePartColorAsync()
    {
        _colorWriteTimer.Stop();
        if (_selectionService.SelectedObject is not PartInstance instance || _partCodeEditor() is not { } editor) return;
        if (instance.Color == _pendingColor) return;

        string? error = await editor.SetPartInstanceColorAsync(instance, _pendingColor);
        if (error != null)
        {
            PartNote = error;
        }
    }

    private void LoadPartColor(PartInstance instance)
    {
        // Enquanto uma gravação está pendente, a reexecução não deve devolver o seletor à cor antiga.
        if (_colorWriteTimer.IsEnabled) return;
        HasPartColor = instance.Color != null;
        _partColor = instance.Color != null && Color.TryParse(instance.Color, out var color) ? color : DefaultPartColor;
        OnPropertyChanged(nameof(PartColor));
    }

    private bool _hasSelection;
    public bool HasSelection
    {
        get => _hasSelection;
        private set { _hasSelection = value; OnPropertyChanged(); }
    }

    private string _title = "Nenhum item selecionado";
    public string Title
    {
        get => _title;
        private set { _title = value; OnPropertyChanged(); }
    }

    private string _typeBadge = "";
    public string TypeBadge
    {
        get => _typeBadge;
        private set { _typeBadge = value; OnPropertyChanged(); }
    }

    // --- Parametric Fields ---
    private bool _isCylinder;
    public bool IsCylinder
    {
        get => _isCylinder;
        private set { _isCylinder = value; OnPropertyChanged(); }
    }

    private bool _isPrism;
    public bool IsPrism
    {
        get => _isPrism;
        private set { _isPrism = value; OnPropertyChanged(); }
    }

    private bool _is2DProfile;
    public bool Is2DProfile
    {
        get => _is2DProfile;
        private set { _is2DProfile = value; OnPropertyChanged(); }
    }

    private bool _is2DEntity;
    public bool Is2DEntity
    {
        get => _is2DEntity;
        private set { _is2DEntity = value; OnPropertyChanged(); }
    }

    // Mathematical Values
    private double _centerX;
    public double CenterX
    {
        get => _centerX;
        set
        {
            if (_centerX != value)
            {
                _centerX = value;
                OnPropertyChanged();
                ApplyCenterModification();
            }
        }
    }

    private double _centerY;
    public double CenterY
    {
        get => _centerY;
        set
        {
            if (_centerY != value)
            {
                _centerY = value;
                OnPropertyChanged();
                ApplyCenterModification();
            }
        }
    }

    private double _centerZ;
    public double CenterZ
    {
        get => _centerZ;
        set
        {
            if (_centerZ != value)
            {
                _centerZ = value;
                OnPropertyChanged();
                ApplyCenterModification();
            }
        }
    }

    private double _radius;
    public double Radius
    {
        get => _radius;
        set
        {
            if (_radius != value && value > 0)
            {
                _radius = value;
                _diameter = value * 2.0;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Diameter));
                ApplyRadiusModification();
            }
        }
    }

    private double _diameter;
    public double Diameter
    {
        get => _diameter;
        set
        {
            if (_diameter != value && value > 0)
            {
                _diameter = value;
                _radius = value / 2.0;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Radius));
                ApplyRadiusModification();
            }
        }
    }

    private double _height;
    public double Height
    {
        get => _height;
        set
        {
            if (_height != value && value != 0)
            {
                _height = value;
                OnPropertyChanged();
                ApplyHeightModification();
            }
        }
    }

    private double _baseArea;
    public double BaseArea
    {
        get => _baseArea;
        private set { _baseArea = value; OnPropertyChanged(); }
    }

    private double _volume;
    public double Volume
    {
        get => _volume;
        private set { _volume = value; OnPropertyChanged(); }
    }

    private string _directionText = "";
    public string DirectionText
    {
        get => _directionText;
        private set { _directionText = value; OnPropertyChanged(); }
    }

    private string _additionalInfo = "";
    public string AdditionalInfo
    {
        get => _additionalInfo;
        private set { _additionalInfo = value; OnPropertyChanged(); }
    }

    private void RefreshProperties()
    {
        var obj = _selectionService.SelectedObject;
        if (obj is not PartInstance)
        {
            IsPartInstance = false;
            PartParameters.Clear();
            PartNote = "";
        }
        if (obj == null)
        {
            HasSelection = false;
            IsFeature = false;
            CanConvertToPart = false;
            RefreshMergeTargets(null);
            Title = "Nenhum elemento selecionado";
            TypeBadge = "";
            IsCylinder = false;
            IsPrism = false;
            Is2DProfile = false;
            Is2DEntity = false;
            return;
        }

        HasSelection = true;
        IsFeature = obj is IFeature;
        FeatureName = (obj as IFeature)?.Name ?? "";
        NameError = null;
        CanConvertToPart = obj is IFeature and not PartInstance and not WorkPlaneFeature;
        NewPartTypeName = CanConvertToPart ? SuggestedTypeName(FeatureName) : "";
        ConvertError = null;
        RefreshMergeTargets(obj as IFeature);

        if (obj is ExtrudeFeature feature)
        {
            if (feature.Profile is CircleProfile circleProfile)
            {
                IsCylinder = true;
                IsPrism = false;
                Is2DProfile = false;
                Is2DEntity = false;

                Title = "Cilindro (Sólido 3D)";
                TypeBadge = "3D SÓLIDO PARAMÉTRICO";

                _centerX = circleProfile.Center.X;
                _centerY = circleProfile.Center.Y;
                _centerZ = circleProfile.Center.Z;
                _radius = circleProfile.Radius;
                _diameter = circleProfile.Radius * 2.0;
                _height = feature.Height;
                BaseArea = feature.BaseArea;
                Volume = feature.Volume;
                DirectionText = $"({feature.Direction.X:F2}, {feature.Direction.Y:F2}, {feature.Direction.Z:F2})";
                AdditionalInfo = $"Tampas Planas: 2 | Superfície Curva: 1";

                NotifyAllFields();
            }
            else
            {
                IsCylinder = false;
                IsPrism = true;
                Is2DProfile = false;
                Is2DEntity = false;

                Title = $"{feature.Name}";
                TypeBadge = "3D SÓLIDO POLIGONAL";

                _centerX = feature.BaseCenter.X;
                _centerY = feature.BaseCenter.Y;
                _centerZ = feature.BaseCenter.Z;
                _height = feature.Height;
                BaseArea = feature.BaseArea;
                Volume = feature.Volume;
                DirectionText = $"({feature.Direction.X:F2}, {feature.Direction.Y:F2}, {feature.Direction.Z:F2})";
                AdditionalInfo = $"Perfil: {feature.Profile.Classification}";

                NotifyAllFields();
            }
        }
        else if (obj is PartInstance instance)
        {
            IsCylinder = false;
            IsPrism = false;
            Is2DProfile = false;
            Is2DEntity = false;

            Title = instance.Name;
            TypeBadge = $"PEÇA · {instance.PartTypeName}";
            var origin = instance.ToWorld(Point3D.Origin);
            _centerX = origin.X;
            _centerY = origin.Y;
            _centerZ = origin.Z;
            DirectionText = "";
            AdditionalInfo = "";

            LoadPartParameters(instance);
            LoadPartColor(instance);
            IsPartInstance = true;
            NotifyAllFields();
        }
        else if (obj is IProfile profile)
        {
            IsCylinder = false;
            IsPrism = false;
            Is2DProfile = true;
            Is2DEntity = false;

            Title = $"Perfil 2D ({profile.Classification})";
            TypeBadge = "PERFIL 2D";

            _centerX = profile.Center.X;
            _centerY = profile.Center.Y;
            _centerZ = profile.Center.Z;
            BaseArea = profile.Area;
            Volume = 0;

            if (profile is CircleProfile cp)
            {
                _radius = cp.Radius;
                _diameter = cp.Radius * 2.0;
                AdditionalInfo = $"Raio: {cp.Radius:F2} mm";
            }
            else
            {
                _radius = 0;
                _diameter = 0;
                AdditionalInfo = $"Vértices: {profile.GetVertices().Count}";
            }

            NotifyAllFields();
        }
        else if (obj is Entity entity)
        {
            IsCylinder = false;
            IsPrism = false;
            Is2DProfile = false;
            Is2DEntity = true;

            if (entity is LineEntity l)
            {
                Title = "Segmento de Linha 2D";
                TypeBadge = "ENTIDADE";
                _centerX = (l.StartPoint.X + l.EndPoint.X) / 2.0;
                _centerY = (l.StartPoint.Y + l.EndPoint.Y) / 2.0;
                _centerZ = (l.StartPoint.Z + l.EndPoint.Z) / 2.0;
                AdditionalInfo = $"Início: ({l.StartPoint.X:F1}, {l.StartPoint.Y:F1})\nFim: ({l.EndPoint.X:F1}, {l.EndPoint.Y:F1})\nComprimento: {l.Length:F2} mm";
            }
            else if (entity is CircleEntity c)
            {
                Title = "Círculo 2D";
                TypeBadge = "ENTIDADE";
                _centerX = c.Center.X;
                _centerY = c.Center.Y;
                _centerZ = c.Center.Z;
                _radius = c.Radius;
                _diameter = c.Radius * 2.0;
                AdditionalInfo = $"Raio: {c.Radius:F2} mm\nDiâmetro: {c.Radius * 2.0:F2} mm";
            }

            NotifyAllFields();
        }
    }

    private void LoadPartParameters(PartInstance instance)
    {
        PartParameters.Clear();
        var editor = _partCodeEditor();
        var arguments = editor?.ReadPartArguments(instance);
        var sourceOrigin = AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.Of(instance);

        foreach (var parameter in instance.Part.GetParameters())
        {
            var argument = arguments?.FirstOrDefault(a => a.Name == parameter.Name);
            bool typeOk = PartSourceEditor.IsEditableType(parameter.Type);
            bool editable = editor != null && arguments != null && typeOk && argument?.IsEditable != false;

            string? hint;
            if (argument is { IsEditable: false })
                hint = $"calculado no código: {argument.Expression}";
            else if (!typeOk)
                hint = "edite no código";
            else if (parameter.Range != null)
                hint = $"entre {Format(parameter.Range.Minimum)} e {Format(parameter.Range.Maximum)}";
            else
                hint = null;

            PartParameters.Add(new PartParameterItem(
                parameter.Name,
                parameter.Type.Name,
                Format(parameter.Value),
                editable,
                hint,
                (name, text) => editor != null
                    ? editor.SetPartParameterAsync(instance, name, text)
                    : System.Threading.Tasks.Task.FromResult<string?>("Editor de código indisponível.")));
        }

        var notes = new System.Collections.Generic.List<string>();
        if (sourceOrigin != null && sourceOrigin.Line > 0)
        {
            notes.Add($"Criada em {sourceOrigin.FilePath}, linha {sourceOrigin.Line}.");
        }
        int siblings = editor?.CountInstancesFromSameCall(instance) ?? 1;
        if (siblings > 1)
        {
            notes.Add($"Esta linha cria {siblings} instâncias: a mudança vale para todas.");
        }
        if (editor != null && arguments == null)
        {
            notes.Add("Não achei a chamada Add(...) no código; edite os parâmetros no editor.");
        }
        PartNote = string.Join(" ", notes);
    }

    /// <summary>
    /// Mostra os parâmetros da peça ranqueados pelo que afeta um sólido do <c>Build()</c> (um furo,
    /// um reforço): primeiro os que mexem só nele, depois os que mexem nele e em outros, e por fim
    /// os que não o afetam. Usa a mesma lista e as mesmas linhas de
    /// <see cref="LoadPartParameters"/> — o que muda é a ordem, a dica e o destino da edição, que
    /// aqui é o valor padrão do record (o sólido é da peça, não de uma instância).
    /// </summary>
    public void ShowPartSolid(PartInstance owner, AvaloniaCadEditor.Application.Interfaces.PartSolidInputs solid)
    {
        var editor = _partCodeEditor();
        PartParameters.Clear();
        IsPartInstance = true;
        Title = solid.SolidName;
        TypeBadge = $"SÓLIDO · {owner.PartTypeName}";

        // Só os parâmetros do record são editáveis aqui; os locais e funções locais da cadeia
        // aparecem destacados no editor, que é onde se mexe neles.
        var relevant = solid.Inputs
            .Where(i => i.Kind == AvaloniaCadEditor.Application.Interfaces.SolidInputKind.PartParameter)
            .ToDictionary(i => i.Name, StringComparer.Ordinal);

        int Rank(string name) =>
            !relevant.TryGetValue(name, out var input) ? 2 : input.IsExclusive ? 0 : 1;

        var parameters = owner.Part.GetParameters()
            .OrderBy(p => Rank(p.Name))
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var parameter in parameters)
        {
            relevant.TryGetValue(parameter.Name, out var input);
            bool typeOk = PartSourceEditor.IsEditableType(parameter.Type);
            bool editable = editor != null && typeOk;

            string? hint;
            if (!typeOk)
                hint = "edite no código";
            else if (input == null)
                hint = null;
            else if (input.IsExclusive)
                hint = $"move só {solid.SolidName}";
            else
                hint = $"move também {string.Join(", ", input.SharedWith)}";

            PartParameters.Add(new PartParameterItem(
                parameter.Name,
                parameter.Type.Name,
                Format(parameter.Value),
                editable,
                hint,
                (name, text) => editor != null
                    ? editor.SetPartTypeParameterDefaultAsync(owner, name, text)
                    : System.Threading.Tasks.Task.FromResult<string?>("Editor de código indisponível.")));
        }

        var notes = new System.Collections.Generic.List<string>
        {
            $"{relevant.Count} dos {parameters.Count} parâmetros de {owner.PartTypeName} afetam {solid.SolidName}.",
            "Editar aqui muda o padrão da peça: vale para todas as instâncias."
        };

        // Uma instância que passa o parâmetro no Add(...) ignora o padrão do record. Sem avisar, a
        // edição simplesmente não surtiria efeito nela e o app não explicaria por quê.
        if (editor != null)
        {
            var ignoring = relevant.Keys
                .Select(name => (Name: name, Counts: editor.CountInstancesOverriding(owner.PartTypeName, name)))
                .Where(x => x.Counts.Overriding > 0)
                .ToList();
            foreach (var (name, counts) in ignoring)
            {
                notes.Add($"{counts.Overriding} de {counts.Total} instâncias passam {name} na chamada Add(...); essas não mudam.");
            }
        }

        PartNote = string.Join(" ", notes);
        NotifyAllFields();
    }

    /// <summary>
    /// Desfaz <see cref="ShowPartSolid"/>, voltando a mostrar o que estiver selecionado (a
    /// instância inteira, ou nada). Chamado ao sair do modo de edição de grupo booleano.
    /// </summary>
    public void ClearPartSolid() => RefreshProperties();

    private static string Format(object? value) =>
        value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";

    private void ApplyRadiusModification()
    {
        var obj = _selectionService.SelectedObject;
        if (obj is ExtrudeFeature ef && ef.Profile is CircleProfile cp)
        {
            cp.UpdateRadius(_radius);
            BaseArea = ef.BaseArea;
            Volume = ef.Volume;
            _onModelParametricModified?.Invoke();
        }
        else if (obj is CircleProfile cp2)
        {
            cp2.UpdateRadius(_radius);
            BaseArea = cp2.Area;
            _onModelParametricModified?.Invoke();
        }
        else if (obj is CircleEntity c)
        {
            c.Radius = _radius;
            _onModelParametricModified?.Invoke();
        }
    }

    private void ApplyHeightModification()
    {
        var obj = _selectionService.SelectedObject;
        if (obj is ExtrudeFeature ef)
        {
            ef.UpdateHeight(_height);
            Volume = ef.Volume;
            _onModelParametricModified?.Invoke();
        }
    }

    private void ApplyCenterModification()
    {
        var obj = _selectionService.SelectedObject;
        var newCenter = new Point3D(_centerX, _centerY, _centerZ);

        if (obj is ExtrudeFeature ef && ef.Profile is CircleProfile cp)
        {
            cp.UpdateCenter(newCenter);
            _onModelParametricModified?.Invoke();
        }
        else if (obj is CircleProfile cp2)
        {
            cp2.UpdateCenter(newCenter);
            _onModelParametricModified?.Invoke();
        }
        else if (obj is PartInstance instance && _partCodeEditor() is { } editor)
        {
            // Peça: a origem mostrada é onde o Placement leva (0,0,0); mover = somar a diferença no "at:".
            var current = instance.ToWorld(Point3D.Origin);
            var offset = new Vector3D(_centerX - current.X, _centerY - current.Y, _centerZ - current.Z);
            if (offset.Length < 1e-9) return;
            _ = MovePartAsync(editor, instance, offset);
        }
    }

    private async System.Threading.Tasks.Task MovePartAsync(IPartCodeEditor editor, PartInstance instance, Vector3D offset)
    {
        string? error = await editor.MovePartInstanceAsync(instance, offset);
        if (error != null)
        {
            PartNote = error;
        }
    }

    private void NotifyAllFields()
    {
        OnPropertyChanged(nameof(CenterX));
        OnPropertyChanged(nameof(CenterY));
        OnPropertyChanged(nameof(CenterZ));
        OnPropertyChanged(nameof(Radius));
        OnPropertyChanged(nameof(Diameter));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(BaseArea));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(DirectionText));
        OnPropertyChanged(nameof(AdditionalInfo));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
