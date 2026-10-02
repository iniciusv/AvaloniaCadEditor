using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
