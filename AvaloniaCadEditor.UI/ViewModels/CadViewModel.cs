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
	public AiChatViewModel? AiChat { get; set; }
	public AvaloniaCadEditor.UI.ReferenceGuides.ReferenceGuidesViewModel? ReferenceGuides { get; set; }

	public ISelectionService SelectionService => _selectionService;
	public IFeature? SelectedFeature => _selectionService.SelectedObject as IFeature;

	/// <summary>Índice da aba "Código" no painel da direita.</summary>
	public const int CodeTabIndex = 1;

	private int _sidebarTabIndex;
	/// <summary>Aba visível no painel da direita (0 = Modelo, 1 = Código, 2 = Ferramentas).</summary>
	public int SidebarTabIndex
	{
		get => _sidebarTabIndex;
		set { if (_sidebarTabIndex != value) { _sidebarTabIndex = value; OnPropertyChanged(); } }
	}

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

	private IProfile? _hoveredFaceProfile;
	/// <summary>
	/// Face realçada pelo hover 3D quando a ferramenta ativa escolhe uma face (modo Face, ou
	/// ExtrudeCommand) - nesse caso o sólido inteiro não deve realçar, só a face sob o cursor.
	/// Distinto de <see cref="HoveredProfile"/>, que é o hover de região 2D em modo Sketch.
	/// </summary>
	public IProfile? HoveredFaceProfile
	{
		get => _hoveredFaceProfile;
		set
		{
			if (_hoveredFaceProfile != value)
			{
				_hoveredFaceProfile = value;
				OnPropertyChanged();
			}
		}
	}

	private bool IsFacePickMode() =>
		CurrentTool is ExtrudeCommand || CurrentSelectionMode == TargetSelectionMode.Face;

	private Ray? _lastHoverRay;
	private Avalonia.Threading.DispatcherTimer? _hoverTimer;
	private List<RaycastHit>? _pickCandidates;
	private int _pickCandidateIndex = -1;
	private RaycastHit? _pickOverride;

	public void ProcessHover(Ray ray)
	{
		_lastHoverRay = ray;

		// O cursor se moveu de verdade: qualquer ciclo TAB em andamento perde o sentido.
		_pickCandidates = null;
		_pickCandidateIndex = -1;
		_pickOverride = null;

		// Remove the highlight immediately when mouse starts moving
		if (HoveredFeatureId != null)
		{
			HoveredFeatureId = null;
		}
		if (HoveredFaceProfile != null)
		{
			HoveredFaceProfile = null;
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
					var hit = RaycastScene(_lastHoverRay.Value);
					if (hit?.FeatureId != null && IsFacePickMode())
					{
						HoveredFeatureId = null;
						HoveredFaceProfile = AvaloniaCadEditor.UI.Controls.Input.ViewportInputHandler.BuildFaceProfile(this, hit.Value);
					}
					else
					{
						HoveredFaceProfile = null;
						HoveredFeatureId = hit?.FeatureId;
					}
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
				if (_currentTool is MoveTool oldMove)
				{
					oldMove.FeaturesMoved -= MoveTool_FeaturesMoved;
					oldMove.FeaturesCopied -= MoveTool_FeaturesCopied;
				}
				if (_currentTool is RotateTool oldRotate)
				{
					oldRotate.FeaturesRotated -= RotateTool_FeaturesRotated;
					oldRotate.FeaturesCopied -= RotateTool_FeaturesCopied;
				}
				if (_currentTool is BooleanTool oldBoolean)
				{
					oldBoolean.OperandAppendRequested -= BooleanTool_OperandAppendRequested;
				}
				_currentTool.Cancel();
			}

			_currentTool = value;

			if (_currentTool != null)
			{
				_currentTool.CodeGenerated += CurrentTool_CodeGenerated;
				if (_currentTool is MoveTool newMove)
				{
					newMove.FeaturesMoved += MoveTool_FeaturesMoved;
					newMove.FeaturesCopied += MoveTool_FeaturesCopied;
				}
				if (_currentTool is RotateTool newRotate)
				{
					newRotate.FeaturesRotated += RotateTool_FeaturesRotated;
					newRotate.FeaturesCopied += RotateTool_FeaturesCopied;
				}
				if (_currentTool is BooleanTool newBoolean)
				{
					newBoolean.OperandAppendRequested += BooleanTool_OperandAppendRequested;
				}
			}

			VcbService.Clear();

			HoveredFeatureId = null;
			_hoverTimer?.Stop();

			// BooleanTool preserva a seleção que já estava marcada: quem chama (SelectBooleanCutCommand/
			// SelectBooleanFuseCommand) já passou os sólidos pré-selecionados como bases; limpar aqui
			// desmarcaria justamente o que a ferramenta acabou de receber.
			if (value != null && !(value is SelectTool) && !(value is MoveTool) && !(value is RotateTool) && !(value is FilletTool) && !(value is BooleanTool))
			{
				ClearSelection();
			}

			OnPropertyChanged();
			OnPropertyChanged(nameof(IsDrawingMode));

			UpdateStatusText(CurrentSnappedPoint);
		}
	}

	/// <summary>
	/// Instâncias de peça movem o "at:" do Add(...); demais features (soltas, via AddFeature) ganham
	/// (ou somam a) um x.Transformed(Placement.Translate(...)) — sem reescrever suas coordenadas
	/// originais, então funciona para qualquer feature (prisma com plano próprio, booleana, etc.).
	/// </summary>
	private async void MoveTool_FeaturesMoved(AvaloniaCadEditor.Domain.ValueObjects.Vector3D vector, System.Collections.Generic.IEnumerable<AvaloniaCadEditor.Domain.Features.IFeature> features)
	{
		var moved = features.ToList();
		var selectedFeatureNames = moved.Select(f => f.Name).ToList();
		ReturnToSelectTool();

		var instances = moved.OfType<PartInstance>().ToList();
		var loose = moved.Where(f => f is not PartInstance).ToList();

		bool changed = false;
		if (instances.Count > 0) changed |= ScriptEditor.MovePartInstances(instances, vector);
		if (loose.Count > 0) changed |= ScriptEditor.MoveLooseFeatures(loose, vector);

		if (!changed) return;

		await ScriptEditor.RunCodeAsync();

		// Restaura a seleção
		foreach (var featureName in selectedFeatureNames)
		{
			var newFeature = Sketch.Features.FirstOrDefault(f => f.Name == featureName);
			if (newFeature != null)
			{
				_selectionService.SelectById(Sketch, newFeature.Id);
			}
		}
	}

	/// <summary>Mesma ideia de <see cref="MoveTool_FeaturesMoved"/>, para girar em torno de um pivô/eixo.</summary>
	private async void RotateTool_FeaturesRotated(AvaloniaCadEditor.Domain.ValueObjects.Point3D pivot, AvaloniaCadEditor.Domain.ValueObjects.Vector3D axis, double degrees, System.Collections.Generic.IEnumerable<AvaloniaCadEditor.Domain.Features.IFeature> features)
	{
		if (Math.Abs(degrees) < 1e-9) return;

		var rotated = features.ToList();
		if (rotated.Count == 0)
		{
			StatusText = "✗ Rotacionar: nada selecionado.";
			return;
		}

		var selectedFeatureNames = rotated.Select(f => f.Name).ToList();
		ReturnToSelectTool();
		var instances = rotated.OfType<PartInstance>().ToList();
		var loose = rotated.Where(f => f is not PartInstance).ToList();

		bool changed = false;
		if (instances.Count > 0) changed |= ScriptEditor.RotatePartInstances(instances, pivot, axis, degrees);
		if (loose.Count > 0) changed |= ScriptEditor.RotateLooseFeatures(loose, pivot, axis, degrees);

		if (!changed) return;

		await ScriptEditor.RunCodeAsync();

		foreach (var featureName in selectedFeatureNames)
		{
			var newFeature = Sketch.Features.FirstOrDefault(f => f.Name == featureName);
			if (newFeature != null)
			{
				_selectionService.SelectById(Sketch, newFeature.Id);
			}
		}
	}

	/// <summary>
	/// Mover/Rotacionar são de uso único: depois de aplicar, volta ao Select. Sem isso a ferramenta
	/// continua ativa (a UI não indica qual está ativa) e o próximo clique no modelo inicia outro movimento.
	/// </summary>
	private void ReturnToSelectTool()
	{
		if (CurrentTool is MoveTool or RotateTool)
			CurrentTool = new SelectTool(_geometryEngine, _selectionService);
	}

	private void MoveTool_FeaturesCopied(AvaloniaCadEditor.Domain.ValueObjects.Vector3D vector, System.Collections.Generic.IEnumerable<AvaloniaCadEditor.Domain.Features.IFeature> features)
	{
		ReturnToSelectTool();
		CopyFeatures(features,
			instances => ScriptEditor.CopyPartInstances(instances, (source, line) =>
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.TranslateInstance(source, line, vector.X, vector.Y, vector.Z)),
			loose => ScriptEditor.CopyLooseFeatures(loose,
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.TranslationCall(vector.X, vector.Y, vector.Z)));
	}

	private void RotateTool_FeaturesCopied(AvaloniaCadEditor.Domain.ValueObjects.Point3D pivot, AvaloniaCadEditor.Domain.ValueObjects.Vector3D axis, double degrees, System.Collections.Generic.IEnumerable<AvaloniaCadEditor.Domain.Features.IFeature> features)
	{
		ReturnToSelectTool();
		CopyFeatures(features,
			instances => ScriptEditor.CopyPartInstances(instances, (source, line) =>
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.RotateInstance(source, line, pivot, axis, degrees)),
			loose => ScriptEditor.CopyLooseFeatures(loose,
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.RotationCall(pivot, axis, degrees)));
	}

	/// <summary>
	/// Modo cópia (Ctrl) do Mover/Rotacionar: instâncias de peça duplicam a chamada Add(...);
	/// features soltas ganham uma AddFeature(x.Transformed(...)) nova. Depois de reexecutar, as
	/// cópias ficam selecionadas.
	/// </summary>
	private async void CopyFeatures(
		System.Collections.Generic.IEnumerable<AvaloniaCadEditor.Domain.Features.IFeature> features,
		Func<System.Collections.Generic.List<PartInstance>, bool> copyInstances,
		Func<System.Collections.Generic.List<AvaloniaCadEditor.Domain.Features.IFeature>, bool> copyLoose)
	{
		var all = features.ToList();
		if (all.Count == 0)
		{
			StatusText = "✗ Copiar: nada selecionado.";
			return;
		}

		var instances = all.OfType<PartInstance>().ToList();
		var loose = all.Where(f => f is not PartInstance).ToList();

		var existingNames = Sketch.Features.Select(f => f.Name).ToHashSet();

		bool changed = false;
		if (instances.Count > 0) changed |= copyInstances(instances);
		if (loose.Count > 0) changed |= copyLoose(loose);

		if (!changed) return;

		await ScriptEditor.RunCodeAsync();

		_selectionService.ClearSelection(Sketch);
		foreach (var copy in Sketch.Features.Where(f => !existingNames.Contains(f.Name)))
		{
			_selectionService.SelectById(Sketch, copy.Id);
		}
	}

	private void CurrentTool_CodeGenerated(object? sender, string codeSnippet)
	{
		ScriptEditor.AppendCode(codeSnippet + "\n");
		ScriptEditor.RunCodeCommand.Execute(null);

		// Return to SelectTool after an operation is committed
		CurrentTool = new SelectTool(_geometryEngine, _selectionService);
	}

	/// <summary>Sólidos já marcados no viewport/árvore (Ctrl+clique) antes de abrir Cut/Fuse - entram
	/// direto como bases da operação (ver <see cref="BooleanTool"/>).</summary>
	private System.Collections.Generic.List<AvaloniaCadEditor.Domain.Features.IFeature> PreSelectedSolids() =>
		Sketch.Features.Where(f => f.IsSelected && f is not AvaloniaCadEditor.Domain.Features.WorkPlaneFeature).ToList();

	/// <summary>Tecla H: com sólido(s) selecionado(s), oculta todo o restante do modelo (isolar seleção).</summary>
	private void HideUnselectedSolids()
	{
		var selected = PreSelectedSolids();
		if (selected.Count == 0)
		{
			return;
		}

		foreach (var feature in Sketch.Features)
		{
			if (feature is AvaloniaCadEditor.Domain.Features.WorkPlaneFeature)
			{
				continue;
			}

			feature.IsVisible = selected.Contains(feature);
		}

		RefreshProfiles();
	}

	private void BooleanTool_OperandAppendRequested(object? sender, (string BooleanVar, System.Collections.Generic.IReadOnlyList<string> ToolVars, string FallbackCode) args)
	{
		if (!ScriptEditor.TryAppendBooleanTool(args.BooleanVar, args.ToolVars))
		{
			// Formato de código não reconhecido: cai para o caminho antigo (feature nova encadeada).
			ScriptEditor.AppendCode(args.FallbackCode + "\n");
		}
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

	/// <summary>O que a seleção pega (inclusive a seleção por arraste): sólidos, faces ou arestas
	/// (linhas e círculos do esboço).</summary>
	public enum TargetSelectionMode { Feature, Face, Edge }

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
			OnPropertyChanged(nameof(IsEdgeSelectionMode));
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
		set { if (value) CurrentSelectionMode = TargetSelectionMode.Feature; }
	}

	public bool IsFaceSelectionMode
	{
		get => CurrentSelectionMode == TargetSelectionMode.Face;
		set { if (value) CurrentSelectionMode = TargetSelectionMode.Face; }
	}

	public bool IsEdgeSelectionMode
	{
		get => CurrentSelectionMode == TargetSelectionMode.Edge;
		set { if (value) CurrentSelectionMode = TargetSelectionMode.Edge; }
	}

	public bool IsInCustomWorkPlane => !ActiveWorkPlane.Equals(WorkPlane.XY);

	/// <summary>Pick radius around the cursor, in screen pixels, for sketch entity hit-testing.</summary>
	private const double HitTolerancePixels = 6.0;

	/// <summary>Size of one screen pixel in world units at the cursor, measured on the active work plane by the viewport.</summary>
	public double WorldUnitsPerPixel { get; set; } = 1.0;

	private double HitTolerance => HitTolerancePixels * WorldUnitsPerPixel;

	private void ApplyHitTolerance()
	{
		switch (CurrentTool)
		{
			case SelectTool t: t.HitTolerance = HitTolerance; break;
			case TrimCommand t: t.HitTolerance = HitTolerance; break;
			case ExtendCommand t: t.HitTolerance = HitTolerance; break;
		}
	}

	private IProfile? FindRegionAtPoint(Point3D localPoint)
	{
		// Smallest containing region wins, so a hole nested in an outer profile stays pickable.
		return Sketch.Profiles
			.Where(p => p.Plane.Equals(ActiveWorkPlane) && p.ContainsPoint(localPoint))
			.OrderBy(p => p.Area)
			.FirstOrDefault();
	}

	public ICommand SelectLineToolCommand { get; }
	public ICommand SelectCircleToolCommand { get; }
	public ICommand SelectPolygonToolCommand { get; }
	public ICommand SelectTrimToolCommand { get; }
	public ICommand SelectExtendToolCommand { get; }
	public ICommand SelectOffsetToolCommand { get; }
	public ICommand SelectExtrudeToolCommand { get; }
	public ICommand SelectBooleanCutCommand { get; }
	public ICommand SelectBooleanFuseCommand { get; }
	public ICommand SelectFilletToolCommand { get; }
	public ICommand SelectSelectToolCommand { get; }
	public ICommand SelectMoveToolCommand { get; }
	public ICommand SelectRotateToolCommand { get; }
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
		}, () => ScriptEditor);

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

		SelectPolygonToolCommand = new RelayCommand(() =>
		{
			if (_selectionService.SelectedObject is IProfile faceProfile && faceProfile.Plane != null)
			{
				ActiveWorkPlane = faceProfile.Plane;
			}
			CurrentMode = EditorMode.SketchEdit;
			CurrentTool = new DrawPolygonCommand();
		});

		SelectTrimToolCommand = new RelayCommand(() => { CurrentMode = EditorMode.SketchEdit; CurrentTool = new TrimCommand(_geometryEngine); });
		SelectExtendToolCommand = new RelayCommand(() => { CurrentMode = EditorMode.SketchEdit; CurrentTool = new ExtendCommand(_geometryEngine); });
		SelectOffsetToolCommand = new RelayCommand(() => { CurrentMode = EditorMode.SketchEdit; CurrentTool = new OffsetCommand(_geometryEngine); });
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
		SelectBooleanCutCommand = new RelayCommand(() => CurrentTool = new BooleanTool(BooleanOperationType.Subtract, _selectionService, PreSelectedSolids()));
		SelectBooleanFuseCommand = new RelayCommand(() => CurrentTool = new BooleanTool(BooleanOperationType.Union, _selectionService, PreSelectedSolids()));
		ExitBooleanEditModeCommand = new RelayCommand(() => ActiveBooleanGroupName = null);
		SelectFilletToolCommand = new RelayCommand<string>((modeStr) =>
		{
			var sel = _selectionService.SelectedObject;
			var mode = modeStr == "Bevel" ? AvaloniaCadEditor.Domain.ValueObjects.FilletMode.Bevel : AvaloniaCadEditor.Domain.ValueObjects.FilletMode.Round;
			var filletCmd = new FilletTool(_selectionService, mode);
			
			CurrentTool = filletCmd;

			// Automatically switch to Face selection mode for convenience
			CurrentSelectionMode = TargetSelectionMode.Face;

			// Just store the sketch so the tool is ready.
			// Do NOT automatically execute! The user must press Enter.
			filletCmd.OnMouseDown(Sketch, new AvaloniaCadEditor.Domain.ValueObjects.Point3D(0, 0, 0));
		});

		SelectSelectToolCommand = new RelayCommand(() => CurrentTool = new SelectTool(_geometryEngine, _selectionService));
		SelectMoveToolCommand = new RelayCommand(() => CurrentTool = new MoveTool { Plane = ActiveWorkPlane });
		SelectRotateToolCommand = new RelayCommand(() => CurrentTool = new RotateTool { WorldUnitsPerPixel = WorldUnitsPerPixel, Plane = ActiveWorkPlane });
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
			RefreshProfiles(); // já reconstrói a malha e a árvore
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
			var meshToExport = GetExportMesh();
			if (meshToExport == null || meshToExport.TriangleCount == 0) return;

			var path = await _filePickerService.SaveFileAsync("Save STL File", "stl", "model");
			if (string.IsNullOrEmpty(path)) return;

			await _stlService.ExportAsync(meshToExport, path, asBinary: true);
		});

		ReconstructMeshCommand = new RelayCommand(async () =>
		{
			if (_selectionService.SelectedObject is ImportedMeshFeature importedFeature && _reconstructionService != null)
			{
				StatusText = "Deduzindo sólido a partir da malha...";
				IFeature? reconstructedFeature;
				try
				{
					reconstructedFeature = await _reconstructionService.ReconstructMeshAsync(importedFeature.Mesh);
				}
				catch (Exception ex)
				{
					StatusText = $"✗ Deduce Solid: falhou ({ex.Message}).";
					return;
				}

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
				else
				{
					StatusText = "✗ Deduce Solid: nenhum algoritmo encontrou um sólido com acurácia volumétrica suficiente (mínimo 50%).";
				}
			}
		}, () => _selectionService.SelectedObject is ImportedMeshFeature);

		_selectionService.SelectionChanged += () =>
		{
			if (ReconstructMeshCommand is RelayCommand rc)
			{
				rc.RaiseCanExecuteChanged();
			}
			// Em uma cena com vários arquivos, abre no editor o arquivo onde a feature foi registrada.
			ScriptEditor?.RevealFeature(SelectedFeature);
			OnPropertyChanged(nameof(SelectedFeature));
		};

		CurrentTool = new SelectTool(_geometryEngine, _selectionService);
	}

	public TriangleMesh? GetFeatureMesh(Guid featureId, bool forceRebuild = false)
	{
		IFeature? previewFeature = (CurrentTool as ExtrudeCommand)?.GetPreviewFeature();
		bool isPreview = previewFeature != null && previewFeature.Id == featureId;
		return MeshManager.GetFeatureMesh(Sketch, featureId, previewFeature, forceRebuild || isPreview);
	}

	private string? _activeBooleanGroupName;
	public string? ActiveBooleanGroupName
	{
		get => _activeBooleanGroupName;
		set
		{
			if (_activeBooleanGroupName != value)
			{
				_activeBooleanGroupName = value;
				HighlightedToolId = null;
				OnPropertyChanged();
				OnPropertyChanged(nameof(IsEditingBooleanGroup));
				OnPropertyChanged(nameof(ActiveBooleanGroup));
			}
		}
	}

	public AvaloniaCadEditor.Domain.Features.BooleanFeature? ActiveBooleanGroup =>
        Sketch.Features.OfType<AvaloniaCadEditor.Domain.Features.BooleanFeature>().FirstOrDefault(f => f.Name == _activeBooleanGroupName);

	public bool IsEditingBooleanGroup => !string.IsNullOrEmpty(_activeBooleanGroupName);

	/// <summary>
	/// Sólidos-ferramenta (vazios) do grupo aberto com duplo-clique, desenhados em vermelho. Numa booleana
	/// solta são as próprias <c>ToolFeatures</c>; numa instância de peça são as ferramentas das booleanas da
	/// definição, envolvidas num <see cref="TransformedFeature"/> com o <c>Placement</c> da instância para
	/// aparecerem no lugar certo do mundo.
	/// </summary>
	public IReadOnlyList<IFeature> ActiveToolFeatures
	{
		get
		{
			if (string.IsNullOrEmpty(_activeBooleanGroupName)) return Array.Empty<IFeature>();

			var group = Sketch.Features.FirstOrDefault(f => f.Name == _activeBooleanGroupName);
			switch (group)
			{
				case AvaloniaCadEditor.Domain.Features.BooleanFeature boolean:
					return boolean.ToolFeatures;
				case PartInstance instance:
					if (!ReferenceEquals(instance, _partToolsOwner))
					{
						_partToolsOwner = instance;
						_partTools = CollectToolFeatures(instance.Definition)
							.Select(tool => (IFeature)new TransformedFeature(tool, instance.Placement, tool.Name))
							.ToList();
					}
					return _partTools;
				default:
					return Array.Empty<IFeature>();
			}
		}
	}

	private PartInstance? _partToolsOwner;
	private IReadOnlyList<IFeature> _partTools = Array.Empty<IFeature>();

	/// <summary>Ferramentas de todas as booleanas de uma árvore de features (bases, fillets e transformações incluídos).</summary>
	public static IReadOnlyList<IFeature> CollectToolFeatures(IFeature root)
	{
		var tools = new List<IFeature>();
		void Visit(IFeature feature)
		{
			if (feature is AvaloniaCadEditor.Domain.Features.BooleanFeature boolean)
			{
				foreach (var b in boolean.BaseFeatures) Visit(b);
				tools.AddRange(boolean.ToolFeatures);
				return;
			}
			foreach (var dep in feature.Dependencies) Visit(dep);
		}
		Visit(root);
		return tools;
	}

	/// <summary>Ferramenta do grupo aberto que foi clicada; desenhada com realce.</summary>
	private Guid? _highlightedToolId;
	public Guid? HighlightedToolId
	{
		get => _highlightedToolId;
		set { if (_highlightedToolId != value) { _highlightedToolId = value; OnPropertyChanged(); } }
	}

	/// <summary>
	/// Clique num sólido vermelho: realça, mostra o nome no status e seleciona a declaração no código
	/// (em Pecas/ para ferramentas de peça, no arquivo da cena para booleanas soltas).
	/// </summary>
	public void RevealToolFeature(IFeature tool)
	{
		HighlightedToolId = tool.Id;

		if (tool is TransformedFeature wrapper && _partToolsOwner != null && _partTools.Contains(tool))
		{
			var local = wrapper.Source;
			StatusText = $"{local.Name} ({local.GetType().Name}) — {_partToolsOwner.PartTypeName}";
			ScriptEditor?.RevealPartTool(_partToolsOwner.PartTypeName, local.Name);
		}
		else
		{
			StatusText = $"{tool.Name} ({tool.GetType().Name})";
			ScriptEditor?.RevealLooseFeatureLine(tool);
		}
	}

	public System.Windows.Input.ICommand ExitBooleanEditModeCommand { get; }

	public RaycastHit? Raycast3D(Ray ray)
	{
		if (_pickOverride.HasValue)
		{
			var over = _pickOverride.Value;
			_pickOverride = null;
			_pickCandidates = null;
			return over;
		}

		return RaycastScene(ray);
	}

	/// <summary>
	/// Raycast na cena incluindo as ferramentas do grupo aberto (soltas ou de peça). Com um grupo aberto, o
	/// próprio sólido do grupo é atravessado, para o clique alcançar os vazios que ficam dentro dele.
	/// </summary>
	private RaycastHit? RaycastScene(Ray ray)
	{
		if (!IsEditingBooleanGroup)
		{
			return MeshManager.Raycast3D(Sketch, ray);
		}

		var tools = ActiveToolFeatures;
		var groupId = Sketch.Features.FirstOrDefault(f => f.Name == _activeBooleanGroupName)?.Id;
		return MeshManager.RaycastAll3D(Sketch, ray, f => tools.Contains(f), ExtraToolFeatures(tools))
			.Cast<RaycastHit?>()
			.FirstOrDefault(h => h!.Value.FeatureId != groupId);
	}

	/// <summary>Ferramentas que não estão no sketch (as de peça) e por isso precisam ir como extras no raycast.</summary>
	private IEnumerable<IFeature>? ExtraToolFeatures(IReadOnlyList<IFeature> tools) =>
		ReferenceEquals(tools, _partTools) && tools.Count > 0 ? tools : null;

	/// <summary>
	/// Alterna o realce (e o próximo alvo de clique) entre sólidos sobrepostos no ponto onde o mouse
	/// está parado, usando o último raio de hover. Disparado pela tecla TAB.
	/// </summary>
	public void CycleHoveredFeature()
	{
		if (_lastHoverRay is not { } ray) return;

		bool isPickTool = CurrentTool is Application.Commands.SelectTool
			or Application.Commands.FilletTool
			or Application.Commands.BooleanTool
			or Application.Commands.ExtrudeCommand
			or Application.Commands.SetWorkPlaneCommand;
		if (!isPickTool) return;

		if (_pickCandidates == null)
		{
			var tools = ActiveToolFeatures;
			_pickCandidates = MeshManager
				.RaycastAll3D(Sketch, ray, f => tools.Contains(f), ExtraToolFeatures(tools))
				.ToList();
			_pickCandidateIndex = -1;
		}

		if (_pickCandidates.Count == 0) return;

		_pickCandidateIndex = (_pickCandidateIndex + 1) % _pickCandidates.Count;
		var candidate = _pickCandidates[_pickCandidateIndex];
		_pickOverride = candidate;

		if (IsFacePickMode())
		{
			HoveredFeatureId = null;
			HoveredFaceProfile = AvaloniaCadEditor.UI.Controls.Input.ViewportInputHandler.BuildFaceProfile(this, candidate);
		}
		else
		{
			HoveredFaceProfile = null;
			HoveredFeatureId = candidate.FeatureId;
		}
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

		// The scene changed: the last guide analysis (if any) is now out of date. It is not
		// recomputed here - that only happens when the user explicitly asks for it - so this
		// never adds cost to the hot edit/rebuild loop.
		ReferenceGuides?.MarkStale();

		if (!skipCodeGeneration && ScriptEditor.GenerateCodeCommand != null && ScriptEditor.GenerateCodeCommand.CanExecute(null))
		{
			ScriptEditor.GenerateCodeCommand.Execute(null);
		}
	}

	/// <summary>
	/// Malha a exportar para STL: se houver features selecionadas na cena, exporta só essas;
	/// caso contrário, exporta a cena inteira (SolidMesh).
	/// </summary>
	private TriangleMesh GetExportMesh()
	{
		var selectedFeatures = Sketch.Features.Where(f => f.IsSelected).ToList();
		if (selectedFeatures.Count == 0) return SolidMesh;

		var meshes = selectedFeatures
			.Select(f => MeshManager.GetFeatureMesh(Sketch, f.Id, null))
			.Where(m => m != null && m.TriangleCount > 0)
			.ToList();

		return TriangleMesh.Combine(meshes, includeFeatureNormals: false);
	}

	public void EnterSketchMode(AvaloniaCadEditor.Domain.Features.WorkPlaneFeature wpf)
	{
		CurrentMode = EditorMode.SketchEdit;
		SetActiveWorkPlane(wpf);
		// By default, when entering sketch mode, grab the Select tool or Line tool
		if (!(CurrentTool is DrawLineCommand) && !(CurrentTool is DrawCircleCommand) && !(CurrentTool is DrawPolygonCommand) && !(CurrentTool is TrimCommand) && !(CurrentTool is ExtendCommand) && !(CurrentTool is OffsetCommand))
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

			var existingWpf = Sketch.WorkPlanes.FirstOrDefault(f => f.Plane.Equals(value));

			if (existingWpf == null)
			{
				var wpf = new AvaloniaCadEditor.Domain.Features.WorkPlaneFeature(value, $"Plano {Sketch.WorkPlanes.Count}");
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
		SyncToolPlane();
		var refPt = CurrentTool?.GetReferencePoint();
		var snappedWorld = _osnapService.GetSnappedPoint(Sketch, worldPoint, refPt != null ? ActiveWorkPlane.ToWorldSpace(refPt.Value) : null, SolidMesh);
		var localSnapped = ActiveWorkPlane.ToLocalSpace(snappedWorld);
		localSnapped = new Point3D(localSnapped.X, localSnapped.Y, 0.0);

		var localRaw = ActiveWorkPlane.ToLocalSpace(worldPoint);
		localRaw = new Point3D(localRaw.X, localRaw.Y, 0.0);

		ApplyHitTolerance();

		if (CurrentTool is SelectTool && CurrentMode == EditorMode.SketchEdit)
		{
			// Use localRaw for HitTest so magnetic snapping doesn't force line selection
			var hitEntity = _geometryEngine.HitTest(Sketch.Entities.Where(e => e.IsVisible), localRaw, HitTolerance);
			if (hitEntity != null)
			{
				_selectionService.SelectById(Sketch, hitEntity.Id);
				ModelTree.RebuildTree();
				return true;
			}

			var hitProfile = FindRegionAtPoint(localRaw);
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
						foreach (var sub in newCompound.SubProfiles) sub.IsSelected = true;
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

		if (CurrentTool is DrawLineCommand || CurrentTool is DrawCircleCommand || CurrentTool is DrawPolygonCommand || CurrentTool is TrimCommand || CurrentTool is ExtendCommand || CurrentTool is OffsetCommand)
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
		SyncToolPlane();
		var refPt = CurrentTool?.GetReferencePoint();
		var snappedWorld = _osnapService.GetSnappedPoint(Sketch, worldPoint, refPt != null ? ActiveWorkPlane.ToWorldSpace(refPt.Value) : null, SolidMesh);
		var localSnapped = ActiveWorkPlane.ToLocalSpace(snappedWorld);
		CurrentSnappedPoint = localSnapped;
		ApplyHitTolerance();
		if (CurrentTool is RotateTool rotateTool) rotateTool.WorldUnitsPerPixel = WorldUnitsPerPixel;
		CurrentTool?.OnMouseMove(Sketch, localSnapped);

		if (CurrentTool is ExtrudeCommand)
		{
			RefreshSolidMesh(skipCodeGeneration: true);
		}

		if (CurrentMode == EditorMode.SketchEdit)
		{
			HoveredProfile = FindRegionAtPoint(localSnapped);
		}
		else
		{
			HoveredProfile = null;
		}

		UpdateStatusText(localSnapped);
	}

	/// <summary>
	/// Move e Rotate recebem pontos locais do plano ativo e precisam dele para levar o resultado
	/// (vetor, pivô, eixo) às coordenadas do mundo. Atualizado a cada evento, então trocar de plano
	/// com a ferramenta ativa também funciona.
	/// </summary>
	private void SyncToolPlane()
	{
		if (CurrentTool is RotateTool rotate) rotate.Plane = ActiveWorkPlane;
		else if (CurrentTool is MoveTool move) move.Plane = ActiveWorkPlane;
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
			case "TAB":
				CycleHoveredFeature();
				break;
			case "L":
				SelectLineToolCommand.Execute(null);
				break;
			case "C":
				SelectCircleToolCommand.Execute(null);
				break;
			case "G":
				SelectPolygonToolCommand.Execute(null);
				break;
			case "P":
			case "E":
				SelectExtrudeToolCommand.Execute(null);
				break;
			case "T":
				SelectTrimToolCommand.Execute(null);
				break;
			case "X":
				SelectExtendToolCommand.Execute(null);
				break;
			case "O":
				SelectOffsetToolCommand.Execute(null);
				break;
			case "M":
				SelectMoveToolCommand.Execute(null);
				break;
			case "R":
				SelectRotateToolCommand.Execute(null);
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
				else if (IsEditingBooleanGroup)
				{
					ActiveBooleanGroupName = null;
				}
				else if (!ActiveWorkPlane.Equals(AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY))
				{
					ActiveWorkPlane = AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY;
				}
				break;
			case "H":
				HideUnselectedSolids();
				break;
			case "F":
				CurrentSelectionMode = CurrentSelectionMode switch
				{
					TargetSelectionMode.Feature => TargetSelectionMode.Face,
					TargetSelectionMode.Face => TargetSelectionMode.Edge,
					_ => TargetSelectionMode.Feature
				};
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
			case "ENTER":
			case "RETURN":
				CurrentTool?.OnKeyDown(Sketch, CadKey.Enter);
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
