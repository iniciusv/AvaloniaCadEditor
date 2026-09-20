using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using System.Globalization;
using System.Linq;

namespace AvaloniaCadEditor.UI.ViewModels;

public class CadViewModel : INotifyPropertyChanged
{
    private readonly IGeometryEngine _geometryEngine;
    private readonly IOSnapService _osnapService;
    private readonly IProfileRecognitionService _profileRecognitionService;
    private readonly IGeometryKernel _geometryKernel;
    private readonly ISelectionService _selectionService;
    
    public Sketch Sketch { get; } = new Sketch();
    
    public IReadOnlyList<IProfile> Profiles => Sketch.Profiles;

    public ModelTreeViewModel ModelTree { get; }
    public PropertyInspectorViewModel PropertyInspector { get; }
    public ISelectionService SelectionService => _selectionService;

    private TriangleMesh _solidMesh = TriangleMesh.Empty;
    public TriangleMesh SolidMesh
    {
        get => _solidMesh;
        private set { _solidMesh = value; OnPropertyChanged(); }
    }

    private IProfile? _selectedProfile;
    public IProfile? SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); }
    }

    private IProfile? _hoveredProfile;
    public IProfile? HoveredProfile
    {
        get => _hoveredProfile;
        set
        {
            if (_hoveredProfile != value)
            {
                _hoveredProfile = value;
                OnPropertyChanged();
            }
        }
    }

    private ICommandTool? _currentTool;
    public ICommandTool? CurrentTool
    {
        get => _currentTool;
        set
        {
            _currentTool?.Cancel();
            _currentTool = value;
            ClearVcb();
            
            if (value != null && !(value is SelectTool) && !(value is MoveTool))
            {
                ClearSelection();
            }

            OnPropertyChanged();
        }
    }

    private void ClearSelection()
    {
        _selectionService.ClearSelection(Sketch);
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _vcbText = "";
    public string VcbText
    {
        get => _vcbText;
        set { _vcbText = value; OnPropertyChanged(); }
    }
    
    private string _inputBuffer = "";
    
    private Point3D _currentSnappedPoint;
    public Point3D CurrentSnappedPoint
    {
        get => _currentSnappedPoint;
        set { _currentSnappedPoint = value; OnPropertyChanged(); }
    }

    public ICommand SelectLineToolCommand { get; }
    public ICommand SelectCircleToolCommand { get; }
    public ICommand SelectTrimToolCommand { get; }
    public ICommand SelectExtrudeToolCommand { get; }

    public ICommand SelectSelectToolCommand { get; }
    public ICommand SelectMoveToolCommand { get; }

    public bool OsnapEnabled
    {
        get => _osnapService.IsEnabled;
        set
        {
            _osnapService.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    public CadViewModel(
        IGeometryEngine geometryEngine,
        IOSnapService osnapService,
        IProfileRecognitionService? profileRecognitionService = null,
        IGeometryKernel? geometryKernel = null,
        ISelectionService? selectionService = null)
    {
        _geometryEngine = geometryEngine;
        _osnapService = osnapService;
        _profileRecognitionService = profileRecognitionService ?? new ProfileRecognitionService();
        _geometryKernel = geometryKernel ?? new NullGeometryKernel();
        _selectionService = selectionService ?? new SelectionService();
        
        ModelTree = new ModelTreeViewModel(_selectionService);
        PropertyInspector = new PropertyInspectorViewModel(_selectionService, () =>
        {
            RefreshSolidMesh();
            ModelTree.RebuildTree();
        });

        ModelTree.BindSketch(Sketch);

        SelectLineToolCommand = new RelayCommand(() => CurrentTool = new DrawLineCommand());
        SelectCircleToolCommand = new RelayCommand(() => CurrentTool = new DrawCircleCommand());
        SelectTrimToolCommand = new RelayCommand(() => CurrentTool = new TrimCommand(_geometryEngine));
        SelectExtrudeToolCommand = new RelayCommand(() => CurrentTool = new ExtrudeCommand());

        SelectSelectToolCommand = new RelayCommand(() => CurrentTool = new SelectTool(_geometryEngine, _selectionService));
        SelectMoveToolCommand = new RelayCommand(() => CurrentTool = new MoveTool());

        CurrentTool = new SelectTool(_geometryEngine, _selectionService);
    }

    public bool TrySelect3DFeature(Ray ray)
    {
        if (Sketch.Features.Count == 0) return false;

        float bestDist = float.MaxValue;
        IFeature? hitFeature = null;

        foreach (var feature in Sketch.Features)
        {
            var mesh = _geometryKernel.BuildSolidMesh(new[] { feature });
            if (mesh != null && ray.IntersectMesh(mesh, out float dist))
            {
                if (dist < bestDist)
                {
                    bestDist = dist;
                    hitFeature = feature;
                }
            }
        }

        if (hitFeature != null)
        {
            _selectionService.SelectById(Sketch, hitFeature.Id);
            ModelTree.RebuildTree();
            return true;
        }

        return false;
    }

    public void RefreshProfiles()
    {
        var recognized = _profileRecognitionService.RecognizeProfiles(Sketch);
        Sketch.SetProfiles(recognized);
        OnPropertyChanged(nameof(Profiles));
        RefreshSolidMesh();
        ModelTree.RebuildTree();
    }

    public void RefreshSolidMesh()
    {
        var featuresToRender = new List<IFeature>(Sketch.Features);
        if (CurrentTool is ExtrudeCommand extCmd)
        {
            var preview = extCmd.GetPreviewFeature();
            if (preview != null)
            {
                featuresToRender.Add(preview);
            }
        }

        SolidMesh = _geometryKernel.BuildSolidMesh(featuresToRender);
    }

    public void OnPointerPressed(Point3D worldPoint)
    {
        var snapped = _osnapService.GetSnappedPoint(Sketch, worldPoint, 5.0); // 5.0 world units tolerance

        // If SelectTool is active and user clicked a 2D profile
        if (CurrentTool is SelectTool)
        {
            var hitProfile = Sketch.Profiles.FirstOrDefault(p => p.ContainsPoint(snapped));
            if (hitProfile != null)
            {
                _selectionService.SelectById(Sketch, hitProfile.Id);
                ModelTree.RebuildTree();
                RefreshProfiles();
                return;
            }
        }

        CurrentTool?.OnMouseDown(Sketch, snapped);
        RefreshProfiles();
    }

    public void OnPointerMoved(Point3D worldPoint)
    {
        var snapped = _osnapService.GetSnappedPoint(Sketch, worldPoint, 5.0);
        CurrentSnappedPoint = snapped;
        CurrentTool?.OnMouseMove(Sketch, snapped);

        if (CurrentTool is ExtrudeCommand)
        {
            RefreshSolidMesh();
        }
        
        // Update 2D Profile Hover
        var hitProfile = Sketch.Profiles.FirstOrDefault(p => p.ContainsPoint(snapped));
        HoveredProfile = hitProfile;

        UpdateStatusText(snapped);
    }

    private void UpdateStatusText(Point3D snapped)
    {
        StatusText = $"X: {snapped.X:F2}, Y: {snapped.Y:F2}";
        
        if (!string.IsNullOrEmpty(_inputBuffer))
        {
            VcbText = _inputBuffer;
        }
        else
        {
            VcbText = CurrentTool?.GetMeasurementText() ?? "";
        }
    }

    public void OnKeyDown(string keyName)
    {
        string upperKey = keyName.ToUpperInvariant();

        // Handle numeric input for VCB
        if (IsNumericKey(upperKey, out char numChar))
        {
            _inputBuffer += numChar;
            UpdateStatusText(CurrentSnappedPoint);
            return;
        }
        else if (upperKey == "BACK" && _inputBuffer.Length > 0)
        {
            _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);
            UpdateStatusText(CurrentSnappedPoint);
            return;
        }
        else if (upperKey == "RETURN" || upperKey == "ENTER")
        {
            if (double.TryParse(_inputBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                CurrentTool?.OnValueEntered(Sketch, value);
                RefreshProfiles();
            }
            ClearVcb();
            return;
        }

        switch (upperKey)
        {
            case "L":
                SelectLineToolCommand.Execute(null);
                break;
            case "C":
                SelectCircleToolCommand.Execute(null);
                break;
            case "E":
                SelectExtrudeToolCommand.Execute(null);
                break;
            case "T":
                SelectTrimToolCommand.Execute(null);
                break;
            case "O": // Placeholder for Orbit
                // SelectOrbitToolCommand.Execute(null);
                break;
            case "M":
                SelectMoveToolCommand.Execute(null);
                break;
            case "SPACE":
                ClearVcb();
                SelectSelectToolCommand.Execute(null);
                break;
            case "ESCAPE":
                ClearVcb();
                CurrentTool?.OnKeyDown(Sketch, CadKey.Escape);
                CurrentTool?.Cancel();
                break;
            case "UP":
                CurrentTool?.OnKeyDown(Sketch, CadKey.ArrowUp);
                break;
            case "DOWN":
                CurrentTool?.OnKeyDown(Sketch, CadKey.ArrowDown);
                break;
            case "LEFT":
                CurrentTool?.OnKeyDown(Sketch, CadKey.ArrowLeft);
                break;
            case "RIGHT":
                CurrentTool?.OnKeyDown(Sketch, CadKey.ArrowRight);
                break;
            case "LEFTCTRL":
            case "RIGHTCTRL":
                CurrentTool?.OnKeyDown(Sketch, CadKey.Control);
                break;
        }
    }

    private void ClearVcb()
    {
        _inputBuffer = "";
        UpdateStatusText(CurrentSnappedPoint);
    }

    private bool IsNumericKey(string keyName, out char result)
    {
        result = '\0';
        if (keyName.Length == 2 && keyName.StartsWith("D") && char.IsDigit(keyName[1]))
        {
            result = keyName[1];
            return true;
        }
        if (keyName.StartsWith("NUMPAD") && keyName.Length == 7 && char.IsDigit(keyName[6]))
        {
            result = keyName[6];
            return true;
        }
        if (keyName == "OEMPERIOD" || keyName == "DECIMAL" || keyName == "OEMCOMMA")
        {
            result = '.';
            return true;
        }
        return false;
    }

    public void SetTool(ICommandTool tool)
    {
        CurrentTool = tool;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
