using System;
using System.Collections.Generic;
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

namespace AvaloniaCadEditor.UI.ViewModels;

public class CadViewModel : INotifyPropertyChanged
{
    private readonly IGeometryEngine _geometryEngine;
    private readonly IOSnapService _osnapService;
    private readonly IProfileRecognitionService _profileRecognitionService;
    private readonly ISelectionService _selectionService;
    
    // Services
    public ISceneMeshManager MeshManager { get; }
    public IVcbInputService VcbService { get; }
    public ISceneDeletionService DeletionService { get; }

    public Sketch Sketch { get; } = new Sketch();
    
    public IReadOnlyList<IProfile> Profiles => Sketch.Profiles;

    public ModelTreeViewModel ModelTree { get; }
    public PropertyInspectorViewModel PropertyInspector { get; }
    public SceneScriptViewModel ScriptEditor { get; set; }

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

    private Guid? _hoveredFeatureId;
    public Guid? HoveredFeatureId
    {
        get => _hoveredFeatureId;
        set
        {
            if (_hoveredFeatureId != value)
            {
                _hoveredFeatureId = value;
                OnPropertyChanged();
            }
        }
    }

    private Ray? _lastHoverRay;
    private Avalonia.Threading.DispatcherTimer? _hoverTimer;

    public void ProcessHover(Ray ray)
    {
        _lastHoverRay = ray;
        
        // Remove the highlight immediately when mouse starts moving
        if (HoveredFeatureId != null) 
        {
            HoveredFeatureId = null;
        }
        
        _hoverTimer?.Stop();
        
        if (_hoverTimer == null)
        {
            _hoverTimer = new Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(200) };
            _hoverTimer.Tick += (s, e) =>
            {
                _hoverTimer.Stop();
                if (_lastHoverRay.HasValue)
                {
                    var hit = MeshManager.Raycast3D(Sketch, _lastHoverRay.Value);
                    HoveredFeatureId = hit?.FeatureId;
                }
            };
        }
        
        _hoverTimer.Start();
    }

    private ICommandTool? _currentTool;
    public ICommandTool? CurrentTool
    {
        get => _currentTool;
        set
        {
            if (_currentTool is IDisposable disposable) disposable.Dispose();
            if (_currentTool != null)
            {
                _currentTool.CodeGenerated -= CurrentTool_CodeGenerated;
                _currentTool.Cancel();
            }
            
            _currentTool = value;
            
            if (_currentTool != null)
            {
                _currentTool.CodeGenerated += CurrentTool_CodeGenerated;
            }

            VcbService.Clear();
            
            HoveredFeatureId = null;
            _hoverTimer?.Stop();
            
            if (value != null && !(value is SelectTool) && !(value is MoveTool))
            {
                ClearSelection();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDrawingMode));
            
            UpdateStatusText(CurrentSnappedPoint);
        }
    }

    private void CurrentTool_CodeGenerated(object? sender, string codeSnippet)
    {
        ScriptEditor.AppendCode(codeSnippet + "\n");
        ScriptEditor.RunCodeCommand.Execute(null);
        
        // Return to SelectTool after an operation is committed
        CurrentTool = new SelectTool(_geometryEngine, _selectionService);
    }

    public bool IsDrawingMode => _currentTool is DrawLineCommand || _currentTool is DrawCircleCommand;

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

    public string VcbText => VcbService.InputBuffer.Length > 0 ? VcbService.InputBuffer : (CurrentTool?.GetMeasurementText() ?? "");
    
    private Point3D _currentSnappedPoint;
    public Point3D CurrentSnappedPoint
    {
        get => _currentSnappedPoint;
        set { _currentSnappedPoint = value; OnPropertyChanged(); }
    }

    public enum TargetSelectionMode { Feature, Face }
    
    private TargetSelectionMode _currentSelectionMode = TargetSelectionMode.Feature;
    public TargetSelectionMode CurrentSelectionMode
    {
        get => _currentSelectionMode;
        set 
        { 
            _currentSelectionMode = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(SelectionModeIndex));
            OnPropertyChanged(nameof(IsFeatureSelectionMode));
            OnPropertyChanged(nameof(IsFaceSelectionMode));
        }
    }

    public int SelectionModeIndex
    {
        get => (int)CurrentSelectionMode;
        set { CurrentSelectionMode = (TargetSelectionMode)value; }
    }

    public bool IsFeatureSelectionMode 
    {
        get => CurrentSelectionMode == TargetSelectionMode.Feature;
        set { if(value) CurrentSelectionMode = TargetSelectionMode.Feature; }
    }

    public bool IsFaceSelectionMode 
    {
        get => CurrentSelectionMode == TargetSelectionMode.Face;
        set { if(value) CurrentSelectionMode = TargetSelectionMode.Face; }
    }

    public bool IsInCustomWorkPlane => !ActiveWorkPlane.Equals(WorkPlane.XY);

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
    public ICommand ExitWorkPlaneCommand { get; }

    public OsnapSettings OsnapSettings => _osnapService.Settings;

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
        ISceneMeshManager meshManager,
        IVcbInputService vcbService,
        ISceneDeletionService deletionService,
        SceneScriptViewModel scriptEditor,
        IProfileRecognitionService? profileRecognitionService = null,
        ISelectionService? selectionService = null,
        ITessellationSettingsProvider? settingsProvider = null)
    {
        _geometryEngine = geometryEngine;
        _osnapService = osnapService;
        MeshManager = meshManager;
        VcbService = vcbService;
        DeletionService = deletionService;
        ScriptEditor = scriptEditor;
        
        _profileRecognitionService = profileRecognitionService ?? new ProfileRecognitionService();
        _selectionService = selectionService ?? new SelectionService();
        _settingsProvider = settingsProvider ?? new AvaloniaCadEditor.Infrastructure.Settings.JsonTessellationSettingsProvider();
        
        ModelTree = new ModelTreeViewModel(_selectionService);
        ModelTree.WorkPlaneDoubleClicked += (wpf) => 
        {
            SetActiveWorkPlane(wpf);
        };

        ModelTree.NodeVisibilityChanged += (s, e) => 
        {
            RefreshProfiles();
            RefreshSolidMesh();
        };

        PropertyInspector = new PropertyInspectorViewModel(_selectionService, () =>
        {
            if (_selectionService.SelectedObject is IFeature selectedFeature)
            {
                MeshManager.InvalidateFeatureAndDependents(Sketch, selectedFeature.Id);
            }
            else
            {
                MeshManager.ClearCache();
            }
            RefreshSolidMesh();
            ModelTree.RebuildTree();
        });

        ModelTree.BindSketch(Sketch);

        SelectLineToolCommand = new RelayCommand(() => 
        {
            if (_selectionService.SelectedObject is IProfile faceProfile && faceProfile.Plane != null)
            {
                ActiveWorkPlane = faceProfile.Plane;
            }
            CurrentTool = new DrawLineCommand();
        });

        SelectCircleToolCommand = new RelayCommand(() => 
        {
            if (_selectionService.SelectedObject is IProfile faceProfile && faceProfile.Plane != null)
            {
                ActiveWorkPlane = faceProfile.Plane;
            }
            CurrentTool = new DrawCircleCommand();
        });

        SelectTrimToolCommand = new RelayCommand(() => CurrentTool = new TrimCommand(_geometryEngine));
        SelectExtrudeToolCommand = new RelayCommand(() => CurrentTool = new ExtrudeCommand());
        SelectBooleanCutCommand = new RelayCommand(() => CurrentTool = new BooleanTool(BooleanOperationType.Subtract, _selectionService));
        SelectBooleanFuseCommand = new RelayCommand(() => CurrentTool = new BooleanTool(BooleanOperationType.Union, _selectionService));

        SelectSelectToolCommand = new RelayCommand(() => CurrentTool = new SelectTool(_geometryEngine, _selectionService));
        SelectMoveToolCommand = new RelayCommand(() => CurrentTool = new MoveTool());
        SelectSetWorkPlaneCommand = new RelayCommand(() => CurrentTool = new SetWorkPlaneCommand());
        
        ExitWorkPlaneCommand = new RelayCommand(() => 
        {
            ActiveWorkPlane = WorkPlane.XY;
            SelectSelectToolCommand.Execute(null);
        });
        
        DeleteSelectedCommand = new RelayCommand(() =>
        {
            var selectedId = _selectionService.SelectedId ?? ModelTree.SelectedNode?.Id;
            if (selectedId.HasValue)
            {
                var feature = System.Linq.Enumerable.FirstOrDefault(Sketch.Features, f => f.Id == selectedId.Value);
                if (feature != null && feature is not AvaloniaCadEditor.Domain.Features.WorkPlaneFeature)
                {
                    if (feature is AvaloniaCadEditor.Domain.Features.BooleanFeature boolF)
                    {
                        foreach (var tool in boolF.ToolFeatures)
                        {
                            ScriptEditor.RemoveCodeForFeature(tool.Name);
                        }
                    }
                    ScriptEditor.RemoveCodeForFeature(feature.Name);
                    ScriptEditor.RunCodeCommand.Execute(null);
                    return;
                }
            }

            DeletionService.DeleteSelected(Sketch, ModelTree.SelectedNode?.Id);
            RefreshProfiles();
            ModelTree.RebuildTree();
        });

        CurrentTool = new SelectTool(_geometryEngine, _selectionService);
    }

    public TriangleMesh? GetFeatureMesh(Guid featureId, bool forceRebuild = false)
    {
        IFeature? previewFeature = (CurrentTool as ExtrudeCommand)?.GetPreviewFeature();
        return MeshManager.GetFeatureMesh(Sketch, featureId, previewFeature, forceRebuild);
    }

    public RaycastHit? Raycast3D(Ray ray)
    {
        return MeshManager.Raycast3D(Sketch, ray);
    }

    public void RefreshProfiles()
    {
        var recognized = _profileRecognitionService.RecognizeProfiles(Sketch);
        Sketch.SetProfiles(recognized);
        OnPropertyChanged(nameof(Profiles));
        RefreshSolidMesh();
        ModelTree.RebuildTree();
    }

    public void RefreshSolidMesh(bool skipCodeGeneration = false)
    {
        IFeature? previewFeature = (CurrentTool as ExtrudeCommand)?.GetPreviewFeature();
        SolidMesh = MeshManager.BuildCompositeMesh(Sketch, previewFeature);
        
        if (!skipCodeGeneration && ScriptEditor.GenerateCodeCommand != null && ScriptEditor.GenerateCodeCommand.CanExecute(null))
        {
            ScriptEditor.GenerateCodeCommand.Execute(null);
        }
    }

    public void SetActiveWorkPlane(AvaloniaCadEditor.Domain.Features.WorkPlaneFeature wpf)
    {
        Sketch.Plane = wpf.Plane;
        OnPropertyChanged(nameof(ActiveWorkPlane));
        RefreshSolidMesh();
    }

    public WorkPlane ActiveWorkPlane
    {
        get => Sketch.Plane;
        set
        {
            if (Sketch.Plane.Equals(value)) return;
            
            Sketch.Plane = value;

            var existingWpf = Sketch.Features.OfType<AvaloniaCadEditor.Domain.Features.WorkPlaneFeature>().FirstOrDefault(f => f.Plane.Equals(value));
            
            if (existingWpf == null)
            {
                var wpf = new AvaloniaCadEditor.Domain.Features.WorkPlaneFeature(value, $"Plano {Sketch.Features.Count(f => f is AvaloniaCadEditor.Domain.Features.WorkPlaneFeature)}");
                Sketch.AddFeature(wpf);
            }
            
            ModelTree.RebuildTree();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInCustomWorkPlane));
            RefreshSolidMesh();
        }
    }

    public bool OnPointerPressed(Point3D worldPoint)
    {
        var refPt = CurrentTool?.GetReferencePoint();
        var snappedWorld = _osnapService.GetSnappedPoint(Sketch, worldPoint, refPt != null ? ActiveWorkPlane.ToWorldSpace(refPt.Value) : null, SolidMesh);
        var localSnapped = ActiveWorkPlane.ToLocalSpace(snappedWorld);
        localSnapped = new Point3D(localSnapped.X, localSnapped.Y, 0.0);

        if (CurrentTool is SelectTool)
        {
            var hitProfile = Sketch.Profiles.FirstOrDefault(p => p.Plane.Equals(ActiveWorkPlane) && p.ContainsPoint(localSnapped));
            if (hitProfile != null)
            {
                _selectionService.SelectById(Sketch, hitProfile.Id);
                ModelTree.RebuildTree();
                RefreshProfiles();
                return true; 
            }
        }

        CurrentTool?.OnMouseDown(Sketch, localSnapped);
        UpdateStatusText(localSnapped);
        if (!(CurrentTool is SelectTool))
        {
            RefreshProfiles();
        }
        
        if (CurrentTool is ExtrudeCommand extCmd && extCmd.IsFinished)
        {
            SelectSelectToolCommand.Execute(null);
        }
        
        return false;
    }

    public void OnPointerMoved(Point3D worldPoint)
    {
        var refPt = CurrentTool?.GetReferencePoint();
        var snappedWorld = _osnapService.GetSnappedPoint(Sketch, worldPoint, refPt != null ? ActiveWorkPlane.ToWorldSpace(refPt.Value) : null, SolidMesh);
        var localSnapped = ActiveWorkPlane.ToLocalSpace(snappedWorld);
        CurrentSnappedPoint = localSnapped; 
        CurrentTool?.OnMouseMove(Sketch, localSnapped);

        if (CurrentTool is ExtrudeCommand)
        {
            RefreshSolidMesh(skipCodeGeneration: true);
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
        
        OnPropertyChanged(nameof(VcbText)); // Force update
    }

    public void OnKeyDown(string keyName, bool isCtrlPressed = false)
    {
        string upperKey = keyName.ToUpperInvariant();
        
        if (isCtrlPressed)
        {
            if (upperKey == "Z")
            {
                ScriptEditor.Undo();
                return;
            }
            if (upperKey == "U" || upperKey == "Y")
            {
                ScriptEditor.Redo();
                return;
            }
        }

        if (VcbService.ProcessKeyDown(keyName, Sketch, CurrentTool, () => RefreshProfiles()))
        {
            UpdateStatusText(CurrentSnappedPoint);
            if (CurrentTool is ExtrudeCommand extCmd && extCmd.IsFinished)
            {
                SelectSelectToolCommand.Execute(null);
            }
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
            case "M":
                SelectMoveToolCommand.Execute(null);
                break;
            case "W":
                SelectSetWorkPlaneCommand.Execute(null);
                break;
            case "SPACE":
                VcbService.Clear();
                UpdateStatusText(CurrentSnappedPoint);
                SelectSelectToolCommand.Execute(null);
                break;
            case "DELETE":
            case "DEL":
                CurrentTool?.OnKeyDown(Sketch, CadKey.Delete);
                DeleteSelectedCommand.Execute(null);
                break;
            case "ESCAPE":
                VcbService.Clear();
                UpdateStatusText(CurrentSnappedPoint);
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
