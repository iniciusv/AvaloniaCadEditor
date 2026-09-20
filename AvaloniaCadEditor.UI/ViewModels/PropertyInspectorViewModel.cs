using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public PropertyInspectorViewModel(ISelectionService selectionService, Action onModelParametricModified)
    {
        _selectionService = selectionService;
        _onModelParametricModified = onModelParametricModified;
        _selectionService.SelectionChanged += RefreshProperties;
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
        if (obj == null)
        {
            HasSelection = false;
            Title = "Nenhum elemento selecionado";
            TypeBadge = "";
            IsCylinder = false;
            IsPrism = false;
            Is2DProfile = false;
            Is2DEntity = false;
            return;
        }

        HasSelection = true;

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
                _height = feature.Depth;
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
                _height = feature.Depth;
                BaseArea = feature.BaseArea;
                Volume = feature.Volume;
                DirectionText = $"({feature.Direction.X:F2}, {feature.Direction.Y:F2}, {feature.Direction.Z:F2})";
                AdditionalInfo = $"Perfil: {feature.Profile.Classification}";

                NotifyAllFields();
            }
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
            ef.UpdateDepth(_height);
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
