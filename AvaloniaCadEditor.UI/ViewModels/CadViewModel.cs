using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
    public IGeometryKernel GeometryKernel => _geometryKernel;

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
            
            // Force status text update so user sees the new tool instructions immediately
            UpdateStatusText(CurrentSnappedPoint);
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
    public ICommand SelectBooleanCutCommand { get; }
    public ICommand SelectBooleanFuseCommand { get; }
    public ICommand SelectSelectToolCommand { get; }
    public ICommand SelectMoveToolCommand { get; }
    public ICommand SelectSetWorkPlaneCommand { get; }
    public ICommand DeleteSelectedCommand { get; }

    public bool OsnapEnabled
    {
        get => _osnapService.IsEnabled;
        set
        {
            _osnapService.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool ShowInternalEdges
    {
        get => _settingsProvider.GetSettings().ShowInternalEdges;
        set
        {
            var settings = _settingsProvider.GetSettings();
            if (settings.ShowInternalEdges != value)
            {
                settings.ShowInternalEdges = value;
                _settingsProvider.SaveSettings(settings);
                OnPropertyChanged();
                RefreshSolidMesh();
            }
        }
    }

    private readonly ITessellationSettingsProvider _settingsProvider;

    public CadViewModel(
        IGeometryEngine geometryEngine,
        IOSnapService osnapService,
        IProfileRecognitionService? profileRecognitionService = null,
        IGeometryKernel? geometryKernel = null,
        ISelectionService? selectionService = null,
        ITessellationSettingsProvider? settingsProvider = null)
    {
        _geometryEngine = geometryEngine;
        _osnapService = osnapService;
        _profileRecognitionService = profileRecognitionService ?? new ProfileRecognitionService();
        _geometryKernel = geometryKernel ?? new NullGeometryKernel();
        _selectionService = selectionService ?? new SelectionService();
        _settingsProvider = settingsProvider ?? new AvaloniaCadEditor.Infrastructure.Settings.JsonTessellationSettingsProvider();
        
        Sketch.FeaturesChanged += () =>
        {
            RefreshSolidMesh();
            ModelTree.RebuildTree();
        };

        ModelTree = new ModelTreeViewModel(_selectionService);
        ModelTree.NodeVisibilityChanged += (s, e) => 
        {
            RefreshProfiles();
            RefreshSolidMesh();
        };
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
        SelectBooleanCutCommand = new RelayCommand(() => CurrentTool = new BooleanTool(BooleanOperationType.Subtract, _selectionService));
        SelectBooleanFuseCommand = new RelayCommand(() => CurrentTool = new BooleanTool(BooleanOperationType.Union, _selectionService));

        SelectSelectToolCommand = new RelayCommand(() => CurrentTool = new SelectTool(_geometryEngine, _selectionService));
        SelectMoveToolCommand = new RelayCommand(() => CurrentTool = new MoveTool());
        SelectSetWorkPlaneCommand = new RelayCommand(() => CurrentTool = new SetWorkPlaneCommand());
        DeleteSelectedCommand = new RelayCommand(DeleteSelected);

        CurrentTool = new SelectTool(_geometryEngine, _selectionService);
    }

    public RaycastHit? Raycast3D(Ray ray)
    {
        if (Sketch.Features.Count == 0) return null;

        float bestDist = float.MaxValue;
        RaycastHit? bestHit = null;

        foreach (var feature in Sketch.Features)
        {
            if (!feature.IsVisible) continue; // CRITICAL FIX: Ignore hidden features

            var mesh = _geometryKernel.BuildSolidMesh(new[] { feature }, _settingsProvider.GetSettings());
            if (mesh != null)
            {
                var hit = ray.IntersectMesh(mesh);
                if (hit.HasValue && hit.Value.Distance < bestDist)
                {
                    bestDist = hit.Value.Distance;
                    bestHit = new RaycastHit(
                        hit.Value.Distance,
                        hit.Value.Point,
                        hit.Value.Normal,
                        feature.Id);
                }
            }
        }

        return bestHit;
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
        var featuresToRender = new List<IFeature>(Sketch.Features.Where(f => f.IsVisible));
        if (CurrentTool is ExtrudeCommand extCmd)
        {
            var preview = extCmd.GetPreviewFeature();
            if (preview != null)
            {
                featuresToRender.Add(preview);
            }
        }

        SolidMesh = _geometryKernel.BuildSolidMesh(featuresToRender, _settingsProvider.GetSettings());
    }

    public WorkPlane ActiveWorkPlane
    {
        get => Sketch.Plane;
        set
        {
            Sketch.Plane = value;
            var wpf = new Domain.Features.WorkPlaneFeature(value, $"Plano {Sketch.Features.Count(f => f is Domain.Features.WorkPlaneFeature)}");
            Sketch.AddFeature(wpf);
            ModelTree.RebuildTree();
            OnPropertyChanged();
            RefreshSolidMesh();
        }
    }

    public bool OnPointerPressed(Point3D worldPoint)
    {
        var localSnapped = ActiveWorkPlane.ToLocalSpace(_osnapService.GetSnappedPoint(Sketch, worldPoint, 5.0, SolidMesh));

        if (CurrentTool is SelectTool)
        {
            var hitProfile = Sketch.Profiles.FirstOrDefault(p => p.Plane.Equals(ActiveWorkPlane) && p.ContainsPoint(localSnapped));
            if (hitProfile != null)
            {
                _selectionService.SelectById(Sketch, hitProfile.Id);
                ModelTree.RebuildTree();
                RefreshProfiles();
                return true; // Indicates we hit and selected a 2D Profile
            }
        }

        CurrentTool?.OnMouseDown(Sketch, localSnapped);
        UpdateStatusText(localSnapped);
        RefreshProfiles();
        return false;
    }

    public void OnPointerMoved(Point3D worldPoint)
    {
        var localSnapped = ActiveWorkPlane.ToLocalSpace(_osnapService.GetSnappedPoint(Sketch, worldPoint, 5.0, SolidMesh));
        CurrentSnappedPoint = localSnapped; // Note: UI will show local coordinates
        CurrentTool?.OnMouseMove(Sketch, localSnapped);

        if (CurrentTool is ExtrudeCommand)
        {
            RefreshSolidMesh();
        }
        
        var hitProfile = Sketch.Profiles.FirstOrDefault(p => p.ContainsPoint(localSnapped));
        HoveredProfile = hitProfile;

        UpdateStatusText(localSnapped);
    }

    private void UpdateStatusText(Point3D snapped)
    {
        string toolInstruction = CurrentTool?.GetStatusText() ?? "";
        string prefix = string.IsNullOrEmpty(toolInstruction) ? "" : $"{toolInstruction} | ";
        StatusText = $"{prefix}X: {snapped.X:F2}, Y: {snapped.Y:F2}";
        
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
            case "P":
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
            case "W":
                SelectSetWorkPlaneCommand.Execute(null);
                break;
            case "SPACE":
                ClearVcb();
                SelectSelectToolCommand.Execute(null);
                break;
            case "DELETE":
            case "DEL":
                CurrentTool?.OnKeyDown(Sketch, CadKey.Delete);
                DeleteSelected();
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

    public void DeleteSelected()
    {
        var selectedId = _selectionService.SelectedId ?? ModelTree.SelectedNode?.Id;

        if (selectedId.HasValue)
        {
            var id = selectedId.Value;

            // 1. Check in Entities
            var targetEntity = Sketch.Entities.FirstOrDefault(e => e.Id == id);
            if (targetEntity != null)
            {
                Sketch.RemoveEntity(targetEntity);
                ClearSelection();
                RefreshProfiles();
                return;
            }

            // 2. Check in Features
            var targetFeature = Sketch.Features.FirstOrDefault(f => f.Id == id);
            if (targetFeature != null)
            {
                if (targetFeature is Domain.Features.WorkPlaneFeature wpf && wpf.IsBasePlane)
                {
                    return; // Cannot delete base plane
                }
                Sketch.RemoveFeature(targetFeature);
                ClearSelection();
                RefreshProfiles();
                return;
            }

            // 3. Check in Profiles
            var targetProfile = Sketch.Profiles.FirstOrDefault(p => p.Id == id);
            if (targetProfile != null)
            {
                var entitiesToRemove = Sketch.Entities.Where(e => targetProfile.SourceEntityIds.Contains(e.Id)).ToList();
                foreach (var entity in entitiesToRemove)
                {
                    Sketch.RemoveEntity(entity);
                }
                Sketch.RemoveProfile(targetProfile);
                ClearSelection();
                RefreshProfiles();
                return;
            }
        }
        else
        {
            // Check IsSelected flags on Entities, Features, and Profiles
            var selectedEntities = Sketch.Entities.Where(e => e.IsSelected).ToList();
            if (selectedEntities.Count > 0)
            {
                foreach (var entity in selectedEntities)
                {
                    Sketch.RemoveEntity(entity);
                }
                ClearSelection();
                RefreshProfiles();
                return;
            }

            var selectedFeatures = Sketch.Features.Where(f => f.IsSelected).ToList();
            if (selectedFeatures.Count > 0)
            {
                foreach (var feature in selectedFeatures)
                {
                    if (feature is Domain.Features.WorkPlaneFeature wpf && wpf.IsBasePlane) continue;
                    Sketch.RemoveFeature(feature);
                }
                ClearSelection();
                RefreshProfiles();
                return;
            }

            var selectedProfiles = Sketch.Profiles.Where(p => p.IsSelected).ToList();
            if (selectedProfiles.Count > 0)
            {
                foreach (var profile in selectedProfiles)
                {
                    var entitiesToRemove = Sketch.Entities.Where(e => profile.SourceEntityIds.Contains(e.Id)).ToList();
                    foreach (var entity in entitiesToRemove)
                    {
                        Sketch.RemoveEntity(entity);
                    }
                    Sketch.RemoveProfile(profile);
                }
                ClearSelection();
                RefreshProfiles();
                return;
            }
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
