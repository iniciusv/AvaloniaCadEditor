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

public enum EditorMode { Part, SketchEdit }

public class CadViewModel : INotifyPropertyChanged
{
    private readonly IGeometryEngine _geometryEngine;
    private readonly IOSnapService _osnapService;
    private readonly IProfileRecognitionService _profileRecognitionService;
    private readonly ISelectionService _selectionService;
    private readonly IPlanarTopologyService _planarTopologyService;
    
    // Services
    public ISceneMeshManager MeshManager { get; }
    public IVcbInputService VcbService { get; }
    public ISceneDeletionService DeletionService { get; }

    private EditorMode _currentMode = EditorMode.Part;
    public EditorMode CurrentMode
    {
        get => _currentMode;
        set { _currentMode = value; OnPropertyChanged(); }
    }

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

    private double _currentZoom = 1.0;
    public double CurrentZoom
    {
        get => _currentZoom;
        set { _currentZoom = value; OnPropertyChanged(); }
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
    public ICommand ExitWorkPlaneCommand { get; }
    public ICommand ReconstructMeshCommand { get; }

    public ICommand ImportStlCommand { get; }
    public ICommand ExportStlCommand { get; }

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

    public int ProfileSegments
    {
        get => _settingsProvider.GetSettings().ProfileSegments;
        set
        {
            var settings = _settingsProvider.GetSettings();
            if (settings.ProfileSegments != value)
            {
                settings.ProfileSegments = value;
                _settingsProvider.SaveSettings(settings);
                OnPropertyChanged();
                MeshManager.ClearCache();
                RefreshSolidMesh();
            }
        }
    }

    private readonly ITessellationSettingsProvider _settingsProvider;
    private readonly IStlService _stlService;
    private readonly IFilePickerService _filePickerService;
    private readonly IShapeReconstructionService _reconstructionService;

    public CadViewModel(
        IGeometryEngine geometryEngine,
        IOSnapService osnapService,
        ISceneMeshManager meshManager,
        IVcbInputService vcbService,
        ISceneDeletionService deletionService,
        SceneScriptViewModel scriptEditor,
        IProfileRecognitionService? profileRecognitionService = null,
        ISelectionService? selectionService = null,
        ITessellationSettingsProvider? settingsProvider = null,
        IStlService? stlService = null,
        IFilePickerService? filePickerService = null,
        IShapeReconstructionService? reconstructionService = null,
        IPlanarTopologyService? planarTopologyService = null)
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
        _stlService = stlService ?? new AvaloniaCadEditor.Infrastructure.IO.StlService();
        _filePickerService = filePickerService!;
        _reconstructionService = reconstructionService!;
        _planarTopologyService = planarTopologyService ?? new AvaloniaCadEditor.Domain.Services.PlanarTopologyService(_geometryEngine);
        
        ModelTree = new ModelTreeViewModel(_selectionService);
        ModelTree.WorkPlaneDoubleClicked += (wpf) => 
        {
            EnterSketchMode(wpf);
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
            CurrentMode = EditorMode.SketchEdit;
            CurrentTool = new DrawLineCommand();
        });

        SelectCircleToolCommand = new RelayCommand(() => 
        {
            if (_selectionService.SelectedObject is IProfile faceProfile && faceProfile.Plane != null)
            {
                ActiveWorkPlane = faceProfile.Plane;
            }
            CurrentMode = EditorMode.SketchEdit;
            CurrentTool = new DrawCircleCommand();
        });

        SelectTrimToolCommand = new RelayCommand(() => { CurrentMode = EditorMode.SketchEdit; CurrentTool = new TrimCommand(_geometryEngine); });
        SelectExtrudeToolCommand = new RelayCommand(() => 
        {
            var profile = _selectionService.SelectedObject as IProfile;
            var extrudeCmd = new ExtrudeCommand();
            CurrentTool = extrudeCmd;
            
            if (profile != null)
            {
                extrudeCmd.StartDirectExtrusion(Sketch, profile, new AvaloniaCadEditor.Domain.ValueObjects.Point3D(0, 0, 0));
                RefreshSolidMesh(); // Force preview mesh update
            }
        });
        SelectBooleanCutCommand = new RelayCommand(() => CurrentTool = new BooleanTool(BooleanOperationType.Subtract, _selectionService));
        SelectBooleanFuseCommand = new RelayCommand(() => CurrentTool = new BooleanTool(BooleanOperationType.Union, _selectionService));

        SelectSelectToolCommand = new RelayCommand(() => CurrentTool = new SelectTool(_geometryEngine, _selectionService));
        SelectMoveToolCommand = new RelayCommand(() => CurrentTool = new MoveTool());
        SelectSetWorkPlaneCommand = new RelayCommand(() => CurrentTool = new SetWorkPlaneCommand());
        
        ExitWorkPlaneCommand = new RelayCommand(() => 
        {
            ExitSketchMode();
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
                            if (tool.IsTransientTool)
                            {
                                ScriptEditor.RemoveCodeForFeature(tool.Name);
                            }
                        }
                    }
                    ScriptEditor.RemoveCodeForFeature(feature.Name);
                    ScriptEditor.RunCodeCommand.Execute(null);
                    _selectionService.ClearSelection(Sketch);
                    return;
                }
            }

            DeletionService.DeleteSelected(Sketch, ModelTree.SelectedNode?.Id);
            RefreshProfiles();
            ModelTree.RebuildTree();
        });

        ImportStlCommand = new RelayCommand(async () =>
        {
            var path = await _filePickerService.OpenFileAsync("Open STL File", new[] { "*.stl" });
            if (string.IsNullOrEmpty(path)) return;

            var mesh = await _stlService.ImportAsync(path);
            if (mesh != null && mesh.TriangleCount > 0)
            {
                var featureName = System.IO.Path.GetFileNameWithoutExtension(path);
                var importedFeature = new AvaloniaCadEditor.Domain.Features.ImportedMeshFeature(mesh, featureName);
                Sketch.AddFeature(importedFeature);
                
                RefreshSolidMesh();
                ModelTree.RebuildTree();
            }
        });

        ExportStlCommand = new RelayCommand(async () =>
        {
            if (SolidMesh == null || SolidMesh.TriangleCount == 0) return;
            
            var path = await _filePickerService.SaveFileAsync("Save STL File", "stl", "model");
            if (string.IsNullOrEmpty(path)) return;

            await _stlService.ExportAsync(SolidMesh, path, asBinary: true);
        });

        ReconstructMeshCommand = new RelayCommand(async () =>
        {
            if (_selectionService.SelectedObject is ImportedMeshFeature importedFeature && _reconstructionService != null)
            {
                var reconstructedFeature = await _reconstructionService.ReconstructMeshAsync(importedFeature.Mesh);
                if (reconstructedFeature != null)
                {
                    importedFeature.IsVisible = false;
                    
                    // Converte a geometria deduzida em código C# e joga no ScriptEditor
                    string code = FeatureToCSharpConverter.GenerateCode(reconstructedFeature);
                    ScriptEditor.AppendCode(code + "\n");
                    ScriptEditor.RunCodeCommand.Execute(null);
                    
                    RefreshSolidMesh();
                    ModelTree.RebuildTree();
                }
            }
        }, () => _selectionService.SelectedObject is ImportedMeshFeature);

        _selectionService.SelectionChanged += () => 
        {
            if (ReconstructMeshCommand is RelayCommand rc)
            {
                rc.RaiseCanExecuteChanged();
            }
        };

        CurrentTool = new SelectTool(_geometryEngine, _selectionService);
    }

    public TriangleMesh? GetFeatureMesh(Guid featureId, bool forceRebuild = false)
    {
        IFeature? previewFeature = (CurrentTool as ExtrudeCommand)?.GetPreviewFeature();
        bool isPreview = previewFeature != null && previewFeature.Id == featureId;
        return MeshManager.GetFeatureMesh(Sketch, featureId, previewFeature, forceRebuild || isPreview);
    }

    public RaycastHit? Raycast3D(Ray ray)
    {
        return MeshManager.Raycast3D(Sketch, ray);
    }

    public void RefreshProfiles()
    {
        var arrangedEntities = _planarTopologyService.ComputePlanarArrangement(Sketch.Entities);
        Sketch.UpdateEntities(arrangedEntities);
        
        var recognized = _profileRecognitionService.RecognizeProfiles(Sketch);
        Sketch.SetProfiles(recognized);
        OnPropertyChanged(nameof(Profiles));
        RefreshSolidMesh();
        ModelTree.RebuildTree();
    }

    public void RefreshSolidMesh(bool skipCodeGeneration = false)
    {
        IFeature? previewFeature = null;
        if (CurrentTool is ExtrudeCommand extCmd)
        {
            previewFeature = extCmd.GetPreviewFeature();
            if (previewFeature != null && extCmd.IsPreviewDirty)
            {
                MeshManager.InvalidateFeatureAndDependents(Sketch, previewFeature.Id);
                extCmd.IsPreviewDirty = false;
            }
        }
        
        SolidMesh = MeshManager.BuildCompositeMesh(Sketch, previewFeature);
        
        if (!skipCodeGeneration && ScriptEditor.GenerateCodeCommand != null && ScriptEditor.GenerateCodeCommand.CanExecute(null))
        {
            ScriptEditor.GenerateCodeCommand.Execute(null);
        }
    }

    public void EnterSketchMode(AvaloniaCadEditor.Domain.Features.WorkPlaneFeature wpf)
    {
        CurrentMode = EditorMode.SketchEdit;
        SetActiveWorkPlane(wpf);
        // By default, when entering sketch mode, grab the Select tool or Line tool
        if (!(CurrentTool is DrawLineCommand) && !(CurrentTool is DrawCircleCommand) && !(CurrentTool is TrimCommand))
        {
            SelectSelectToolCommand.Execute(null);
        }
    }

    public void ExitSketchMode()
    {
        CurrentMode = EditorMode.Part;
        SelectSelectToolCommand.Execute(null);
    }

    public void SetActiveWorkPlane(AvaloniaCadEditor.Domain.Features.WorkPlaneFeature wpf)
    {
        ActiveWorkPlane = wpf.Plane;
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

    public bool OnPointerPressed(Point3D worldPoint, bool isCtrlPressed = false)
    {
        var refPt = CurrentTool?.GetReferencePoint();
        var snappedWorld = _osnapService.GetSnappedPoint(Sketch, worldPoint, refPt != null ? ActiveWorkPlane.ToWorldSpace(refPt.Value) : null, SolidMesh);
        var localSnapped = ActiveWorkPlane.ToLocalSpace(snappedWorld);
        localSnapped = new Point3D(localSnapped.X, localSnapped.Y, 0.0);
        
        var localRaw = ActiveWorkPlane.ToLocalSpace(worldPoint);
        localRaw = new Point3D(localRaw.X, localRaw.Y, 0.0);
        
        // Reduced 10x as requested (0.5 visual pixels)
        double dynamicTolerance = 0.5 / CurrentZoom;

        if (CurrentTool is SelectTool selTool)
        {
            selTool.HitTolerance = dynamicTolerance;
        }

        if (CurrentTool is SelectTool && CurrentMode == EditorMode.SketchEdit)
        {
            // Use localRaw for HitTest so magnetic snapping doesn't force line selection
            var hitEntity = _geometryEngine.HitTest(Sketch.Entities.Where(e => e.IsVisible), localRaw, dynamicTolerance);
            if (hitEntity != null)
            {
                _selectionService.SelectById(Sketch, hitEntity.Id);
                ModelTree.RebuildTree();
                return true; 
            }

            var hitProfile = Sketch.Profiles.FirstOrDefault(p => p.Plane.Equals(ActiveWorkPlane) && p.ContainsPoint(localRaw));
            if (hitProfile != null)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] HitProfile: {hitProfile.Id}, Area: {hitProfile.Area:F2}");
                if (isCtrlPressed || IsCtrlPressed)
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Ctrl Pressed. Current Selected: {_selectionService.SelectedObject?.GetType().Name}");
                    var currentProfile = _selectionService.SelectedObject as IProfile;
                    var subs = new List<IProfile>();
                    
                    if (currentProfile is AvaloniaCadEditor.Domain.Profiles.CompoundProfile compound)
                        subs.AddRange(compound.SubProfiles);
                    else if (currentProfile != null)
                        subs.Add(currentProfile);

                    // Check if hitProfile is already in subs (geometrically or by ID)
                    var existing = subs.FirstOrDefault(p => p.Id == hitProfile.Id || 
                        (p.Center.DistanceTo(hitProfile.Center) < 1e-4 && Math.Abs(p.Area - hitProfile.Area) < 1e-4));

                    if (existing != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Deselecting existing profile {existing.Id}");
                        subs.Remove(existing); // Deselect
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Adding new profile {hitProfile.Id} to selection");
                        subs.Add(hitProfile); // Add to selection
                    }

                    _selectionService.ClearSelection(Sketch);
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Subs count after evaluation: {subs.Count}");

                    if (subs.Count == 1)
                    {
                        var liveProfile = Sketch.Profiles.FirstOrDefault(p => p.Id == subs[0].Id || 
                            (p.Center.DistanceTo(subs[0].Center) < 1e-4 && Math.Abs(p.Area - subs[0].Area) < 1e-4)) ?? subs[0];
                        
                        _selectionService.SelectById(Sketch, liveProfile.Id);
                        if (_selectionService.SelectedObject == null) // Fallback if ID changed
                        {
                            System.Diagnostics.Debug.WriteLine($"[DEBUG] Fallback selection for single profile");
                            liveProfile.IsSelected = true;
                            _selectionService.SelectProfile(liveProfile);
                        }
                    }
                    else if (subs.Count > 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Creating CompoundProfile with {subs.Count} profiles");
                        var newCompound = new AvaloniaCadEditor.Domain.Profiles.CompoundProfile(subs) { IsSelected = true };
                        foreach(var sub in newCompound.SubProfiles) sub.IsSelected = true;
                        _selectionService.SelectProfile(newCompound);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Single select (no Ctrl): {hitProfile.Id}");
                    _selectionService.SelectById(Sketch, hitProfile.Id);
                }
                
                ModelTree.RebuildTree();
                // Removed RefreshProfiles() here because it destroys and recreates all profiles, 
                // breaking selection references and IDs.
                return true; 
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] No profile hit at {localRaw}");
            }
        }

        if (CurrentTool is SelectTool)
            CurrentTool.OnMouseDown(Sketch, localRaw);
        else
            CurrentTool?.OnMouseDown(Sketch, localSnapped);
            
        UpdateStatusText(localSnapped);
        
        if (CurrentTool is DrawLineCommand || CurrentTool is DrawCircleCommand || CurrentTool is TrimCommand)
        {
            RefreshProfiles();
        }
        else if (CurrentTool is ExtrudeCommand)
        {
            RefreshSolidMesh(skipCodeGeneration: true);
        }
        
        if (CurrentTool is ExtrudeCommand extCmd && extCmd.IsFinished)
        {
            ExitSketchMode();
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
        
        if (CurrentMode == EditorMode.SketchEdit)
        {
            var hitProfile = Sketch.Profiles.FirstOrDefault(p => p.ContainsPoint(localSnapped));
            HoveredProfile = hitProfile;
        }
        else
        {
            HoveredProfile = null;
        }

        UpdateStatusText(localSnapped);
    }

    private void UpdateStatusText(Point3D snapped)
    {
        string toolInstruction = CurrentTool?.GetStatusText() ?? "";
        string prefix = string.IsNullOrEmpty(toolInstruction) ? "" : $"{toolInstruction} | ";
        StatusText = $"{prefix}X: {snapped.X:F2}, Y: {snapped.Y:F2}";
        
        OnPropertyChanged(nameof(VcbText)); // Force update
    }

    public bool IsCtrlPressed { get; set; }
    
    public void OnKeyDown(string keyName, bool isCtrlPressed = false)
    {
        string upperKey = keyName.ToUpperInvariant();
        
        if (upperKey == "LEFTCTRL" || upperKey == "RIGHTCTRL")
        {
            IsCtrlPressed = true;
            CurrentTool?.OnKeyDown(Sketch, CadKey.Control);
            return;
        }
        
        if (isCtrlPressed || IsCtrlPressed)
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
                ExitSketchMode();
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

                if (CurrentTool is not Application.Commands.SelectTool)
                {
                    CurrentTool?.OnKeyDown(Sketch, CadKey.Escape);
                    CurrentTool?.Cancel();
                    SelectSelectToolCommand.Execute(null);
                }
                else if (_selectionService.SelectedObject != null || _selectionService.SelectedId != null)
                {
                    _selectionService.ClearSelection(Sketch);
                }
                else if (!ActiveWorkPlane.Equals(AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY))
                {
                    ActiveWorkPlane = AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY;
                }
                break;
            case "F":
                CurrentSelectionMode = CurrentSelectionMode == TargetSelectionMode.Feature 
                    ? TargetSelectionMode.Face 
                    : TargetSelectionMode.Feature;
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
        }
    }
    
    public void OnKeyUp(string keyName)
    {
        string upperKey = keyName.ToUpperInvariant();
        if (upperKey == "LEFTCTRL" || upperKey == "RIGHTCTRL")
        {
            IsCtrlPressed = false;
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
