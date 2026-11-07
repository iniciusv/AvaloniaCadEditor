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

public partial class CadViewModel : INotifyPropertyChanged
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
		set { _currentMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSketchEditing)); NotifySelectionLevelChanged(); }
	}

	public bool IsSketchEditing => CurrentMode == EditorMode.SketchEdit;

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

	/// <summary>Faces são selecionáveis só dentro de uma entidade aberta (inclusive para o Extrude).</summary>
	private bool IsFacePickMode() => SolidSelectionMode == TargetSelectionMode.Face;

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
					ApplyHover(RaycastScene(_lastHoverRay.Value));
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
				if (_currentTool is MirrorTool oldMirror)
				{
					oldMirror.FeaturesMirrored -= MirrorTool_FeaturesMirrored;
					oldMirror.FeaturesCopied -= MirrorTool_FeaturesCopied;
				}
				if (_currentTool is BooleanTool oldBoolean)
				{
					oldBoolean.OperandAppendRequested -= BooleanTool_OperandAppendRequested;
				}
				if (_currentTool is FilletTool oldFillet)
				{
					oldFillet.PartFilletRequested -= FilletTool_PartFilletRequested;
					oldFillet.FilletBeforeCutRequested -= FilletTool_FilletBeforeCutRequested;
				}
				if (_currentTool is ExtrudeCommand oldExtrude)
				{
					oldExtrude.SolidForPartRequested -= ExtrudeTool_SolidForPartRequested;
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
				if (_currentTool is MirrorTool newMirror)
				{
					newMirror.FeaturesMirrored += MirrorTool_FeaturesMirrored;
					newMirror.FeaturesCopied += MirrorTool_FeaturesCopied;
				}
				if (_currentTool is BooleanTool newBoolean)
				{
					newBoolean.OperandAppendRequested += BooleanTool_OperandAppendRequested;
				}
				if (_currentTool is FilletTool newFillet)
				{
					newFillet.PartFilletRequested += FilletTool_PartFilletRequested;
					newFillet.FilletBeforeCutRequested += FilletTool_FilletBeforeCutRequested;
				}
				if (_currentTool is ExtrudeCommand newExtrude)
				{
					newExtrude.SolidForPartRequested += ExtrudeTool_SolidForPartRequested;
				}
			}
			ApplyEditContextToTool();

			VcbService.Clear();

			HoveredFeatureId = null;
			_hoverTimer?.Stop();

			// BooleanTool preserva a seleção que já estava marcada: quem chama (SelectBooleanCutCommand/
			// SelectBooleanFuseCommand) já passou os sólidos pré-selecionados como bases; limpar aqui
			// desmarcaria justamente o que a ferramenta acabou de receber.
			// SweepCommand está na lista pelo mesmo motivo: ele recebe a região e as arestas
			// pré-selecionadas no construtor, e limpar aqui apagaria o destaque do que ele acabou
			// de receber (e, numa recusa, o usuário não veria mais o que estava marcado).
			if (value != null && !(value is SelectTool) && !(value is MoveTool) && !(value is RotateTool) && !(value is MirrorTool) && !(value is FilletTool) && !(value is BooleanTool) && !(value is SweepCommand))
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

		if (moved.OfType<TransformedFeature>().FirstOrDefault(f => _partTools.Contains(f)) is { } partSolid && _partToolsOwner is { } owner)
		{
			var localOffset = owner.Placement.Inverse().ApplyVector(vector);
			await EditPartSolidAsync(owner, partSolid.Source.Name, "movido",
				(partTypeName, solidName) => ScriptEditor.MovePartSolidAsync(partTypeName, solidName, localOffset));
			return;
		}

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

		if (rotated.OfType<TransformedFeature>().FirstOrDefault(f => _partTools.Contains(f)) is { } partSolid && _partToolsOwner is { } owner)
		{
			// Girar em torno de (p, a) no mundo é, no referencial da peça, girar em torno de P⁻¹(p) e
			// P⁻¹(a) pelo mesmo ângulo. Arredondado como o código grava, para "mesmo pivô/eixo soma o
			// ângulo" reconhecer o giro anterior apesar do ruído de ponto flutuante da inversa.
			var toLocal = owner.Placement.Inverse();
			var localPivot = toLocal.Apply(pivot);
			var localAxis = toLocal.ApplyVector(axis);
			await EditPartSolidAsync(owner, partSolid.Source.Name, "girado",
				(partTypeName, solidName) => ScriptEditor.RotatePartSolidAsync(partTypeName, solidName,
					new AvaloniaCadEditor.Domain.ValueObjects.Point3D(Round(localPivot.X), Round(localPivot.Y), Round(localPivot.Z)),
					new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(Round(localAxis.X), Round(localAxis.Y), Round(localAxis.Z)),
					degrees));
			return;
		}

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
		if (CurrentTool is MoveTool or RotateTool or MirrorTool)
			CurrentTool = new SelectTool(_geometryEngine, _selectionService);
	}

	/// <summary>
	/// Com o grupo de uma peça aberto e um sólido do <c>Build()</c> realçado, é ele que o Mover arrasta,
	/// e não a instância (que segue selecionada desde o duplo-clique). Null: a seleção normal do sketch.
	/// </summary>
	private IReadOnlyList<IFeature>? HighlightedPartSolidSelection()
	{
		if (HighlightedToolId is not { } id) return null;
		var tools = ActiveToolFeatures;
		if (!ReferenceEquals(tools, _partTools)) return null;
		var solid = tools.FirstOrDefault(t => t.Id == id);
		return solid != null ? new[] { solid } : null;
	}

	/// <summary>
	/// Move/gira um sólido do <c>Build()</c> da peça aberta: a transformação vai para a declaração dele no
	/// record, em coordenadas da peça (quem chama já converteu — a instância pode estar girada). A
	/// definição é compartilhada, então todas as instâncias da peça mudam.
	/// </summary>
	private async System.Threading.Tasks.Task EditPartSolidAsync(PartInstance owner, string solidName, string pastParticiple,
		Func<string, string, System.Threading.Tasks.Task<string?>> edit)
	{
		string? error = await edit(owner.PartTypeName, solidName);
		if (error != null)
		{
			StatusText = $"✗ {error}";
			return;
		}

		// A cena reexecutou: o grupo continua aberto (é guardado por nome), mas as ferramentas são
		// objetos novos. Reencontra o sólido pelo nome para ele seguir realçado e pronto para outro movimento.
		var moved = ActiveToolFeatures.FirstOrDefault(f => f is TransformedFeature w && w.Source.Name == solidName);
		if (moved != null) RevealToolFeature(moved);
		StatusText = $"✓ {solidName} {pastParticiple} em {owner.PartTypeName} (todas as instâncias)";
	}

	private static double Round(double value) => Math.Round(value, 6);

	private void MoveTool_FeaturesCopied(AvaloniaCadEditor.Domain.ValueObjects.Vector3D vector, System.Collections.Generic.IEnumerable<AvaloniaCadEditor.Domain.Features.IFeature> features)
	{
		ReturnToSelectTool();
		if (features.Any(f => _partTools.Contains(f)))
		{
			StatusText = "✗ Copiar um sólido de dentro da peça ainda não é suportado: duplique a declaração no Build().";
			return;
		}
		CopyFeatures(features,
			instances => ScriptEditor.CopyPartInstances(instances, (source, line) =>
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.TranslateInstance(source, line, vector.X, vector.Y, vector.Z)),
			loose => ScriptEditor.CopyLooseFeatures(loose,
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.TranslationCall(vector.X, vector.Y, vector.Z)));
	}

	private void RotateTool_FeaturesCopied(AvaloniaCadEditor.Domain.ValueObjects.Point3D pivot, AvaloniaCadEditor.Domain.ValueObjects.Vector3D axis, double degrees, System.Collections.Generic.IEnumerable<AvaloniaCadEditor.Domain.Features.IFeature> features)
	{
		ReturnToSelectTool();
		if (features.Any(f => _partTools.Contains(f)))
		{
			StatusText = "✗ Copiar um sólido de dentro da peça ainda não é suportado: duplique a declaração no Build().";
			return;
		}
		CopyFeatures(features,
			instances => ScriptEditor.CopyPartInstances(instances, (source, line) =>
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.RotateInstance(source, line, pivot, axis, degrees)),
			loose => ScriptEditor.CopyLooseFeatures(loose,
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.RotationCall(pivot, axis, degrees)));
	}

	// ---------------------------------------------------------------- Espelhar (menu do botão direito)

	/// <summary>O que o Espelhar age sobre: o sólido de peça realçado no grupo aberto, ou a seleção do sketch.</summary>
	public System.Collections.Generic.IReadOnlyList<IFeature> MirrorTargets() =>
		HighlightedPartSolidSelection()
		?? Sketch.Features.Where(f => f.IsSelected && f is not WorkPlaneFeature).ToList();

	/// <summary>O menu de Espelhar só aparece sem outra operação em andamento e com algo para espelhar.</summary>
	public bool CanMirrorSelection => CurrentTool is SelectTool && MirrorTargets().Count > 0;

	/// <summary>Espelha (ou copia espelhado) pelo plano de normal <paramref name="normal"/> que passa pelo centro da seleção.</summary>
	public void MirrorSelectionAtCenter(AvaloniaCadEditor.Domain.ValueObjects.Vector3D normal, bool copy)
	{
		var targets = MirrorTargets();
		if (targets.Count == 0)
		{
			StatusText = "✗ Espelhar: nada selecionado.";
			return;
		}
		var c = MirrorTool.SelectionCenter(targets);
		var center = new AvaloniaCadEditor.Domain.ValueObjects.Point3D(Round(c.X), Round(c.Y), Round(c.Z));
		if (copy) MirrorTool_FeaturesCopied(center, normal, targets);
		else MirrorTool_FeaturesMirrored(center, normal, targets);
	}

	/// <summary>Abre a ferramenta que escolhe o plano do espelho com o mouse (ver <see cref="MirrorTool"/>).</summary>
	public void StartMirrorTool(bool copy)
	{
		var targets = MirrorTargets();
		if (targets.Count == 0)
		{
			StatusText = "✗ Espelhar: nada selecionado.";
			return;
		}
		CurrentTool = new MirrorTool(targets, copy) { Plane = ActiveWorkPlane };
	}

	/// <summary>Mesma ideia de <see cref="RotateTool_FeaturesRotated"/>, para espelhar por um plano.</summary>
	private async void MirrorTool_FeaturesMirrored(AvaloniaCadEditor.Domain.ValueObjects.Point3D pointOnPlane, AvaloniaCadEditor.Domain.ValueObjects.Vector3D normal, System.Collections.Generic.IEnumerable<IFeature> features)
	{
		var mirrored = features.ToList();
		ReturnToSelectTool();
		if (mirrored.Count == 0)
		{
			StatusText = "✗ Espelhar: nada selecionado.";
			return;
		}

		if (mirrored.OfType<TransformedFeature>().FirstOrDefault(f => _partTools.Contains(f)) is { } partSolid && _partToolsOwner is { } owner)
		{
			// O plano vai para o referencial da peça (a instância pode estar girada/espelhada).
			var toLocal = owner.Placement.Inverse();
			var localPoint = toLocal.Apply(pointOnPlane);
			var localNormal = toLocal.ApplyVector(normal);
			await EditPartSolidAsync(owner, partSolid.Source.Name, "espelhado",
				(partTypeName, solidName) => ScriptEditor.MirrorPartSolidAsync(partTypeName, solidName,
					new AvaloniaCadEditor.Domain.ValueObjects.Point3D(Round(localPoint.X), Round(localPoint.Y), Round(localPoint.Z)),
					new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(Round(localNormal.X), Round(localNormal.Y), Round(localNormal.Z))));
			return;
		}

		var selectedFeatureNames = mirrored.Select(f => f.Name).ToList();
		var instances = mirrored.OfType<PartInstance>().ToList();
		var loose = mirrored.Where(f => f is not PartInstance).ToList();

		bool changed = false;
		if (instances.Count > 0) changed |= ScriptEditor.MirrorPartInstances(instances, pointOnPlane, normal);
		if (loose.Count > 0) changed |= ScriptEditor.MirrorLooseFeatures(loose, pointOnPlane, normal);

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

	private void MirrorTool_FeaturesCopied(AvaloniaCadEditor.Domain.ValueObjects.Point3D pointOnPlane, AvaloniaCadEditor.Domain.ValueObjects.Vector3D normal, System.Collections.Generic.IEnumerable<IFeature> features)
	{
		ReturnToSelectTool();
		if (features.Any(f => _partTools.Contains(f)))
		{
			StatusText = "✗ Copiar um sólido de dentro da peça ainda não é suportado: duplique a declaração no Build().";
			return;
		}
		CopyFeatures(features,
			instances => ScriptEditor.CopyPartInstances(instances, (source, line) =>
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.MirrorInstance(source, line, pointOnPlane, normal)),
			loose => ScriptEditor.CopyLooseFeatures(loose,
				AvaloniaCadEditor.Application.Scenes.PartSourceEditor.MirrorCall(pointOnPlane, normal)));
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

	/// <summary>
	/// Fillet/chanfro numa instância de peça: vai para o <c>Build()</c> do record (todas as instâncias),
	/// não para a cena. As normais chegam no mundo e vão para o referencial da peça (a instância pode
	/// estar girada/espelhada), arredondadas como o código grava — mesmo caminho do Espelhar.
	/// </summary>
	private async void FilletTool_PartFilletRequested(object? sender,
		(PartInstance Owner, System.Collections.Generic.IReadOnlyList<(AvaloniaCadEditor.Domain.ValueObjects.Vector3D Normal, AvaloniaCadEditor.Domain.ValueObjects.Point3D Point)> WorldFaces,
		 AvaloniaCadEditor.Domain.ValueObjects.FilletMode Mode, double Radius) request)
	{
		CurrentTool = new SelectTool(_geometryEngine, _selectionService);

		var localFaces = AvaloniaCadEditor.Application.Scenes.PartBuildFilletEditor.ToPartFrame(request.Owner.Placement, request.WorldFaces);
		string? error = await ScriptEditor.AddPartFilletAsync(request.Owner.PartTypeName, request.Mode, request.Radius, localFaces);
		string what = request.Mode == AvaloniaCadEditor.Domain.ValueObjects.FilletMode.Bevel ? "Chanfro" : "Arredondamento";
		StatusText = error != null
			? $"✗ {error}"
			: $"✓ {what} aplicado em {request.Owner.PartTypeName} (todas as instâncias)";
	}

	/// <summary>
	/// Chanfro/arredondamento de todas as arestas num sólido furado: entra antes dos furos (ver
	/// <see cref="FilletTool.FilletBeforeCutRequested"/>); se o código não deixar, vai depois, como antes.
	/// </summary>
	private async void FilletTool_FilletBeforeCutRequested(object? sender, (string CutVar, string BaseVar, string FilletVar, string FilletCode, string FallbackCode) args)
	{
		CurrentTool = new SelectTool(_geometryEngine, _selectionService);

		bool beforeCut = ScriptEditor.TryInsertFilletBeforeCut(args.CutVar, args.BaseVar, args.FilletVar, args.FilletCode);
		if (!beforeCut)
		{
			ScriptEditor.AppendCode(args.FallbackCode + "\n");
		}
		await ScriptEditor.RunCodeAsync();

		// Depois da execução, que escreve o próprio status.
		if (beforeCut && !ScriptEditor.HasErrors)
		{
			StatusText = $"✓ {args.FilletVar} aplicado em {args.BaseVar}, antes dos furos de {args.CutVar} (as bocas dos furos ficam vivas)";
		}
	}

	/// <summary>
	/// Extrusão com uma peça aberta: o sólido vai para o <c>Build()</c> dela, em coordenadas locais (ver
	/// <see cref="SceneScriptViewModel.InsertSolidIntoPartAsync"/>). Altura negativa corta; positiva une
	/// depois dos cortes, para os furos que a peça já tem não apagarem o material novo.
	/// </summary>
	private async void ExtrudeTool_SolidForPartRequested(object? sender, (string Code, bool IsCut) request)
	{
		var context = EditContext;
		CurrentTool = new SelectTool(_geometryEngine, _selectionService);
		if (context == null)
		{
			// A peça foi fechada entre o clique e o evento: grava na cena, como sempre.
			ScriptEditor.AppendCode(request.Code + "\n");
			await ScriptEditor.RunCodeAsync();
			return;
		}

		StatusText = $"Gravando em {context.PartTypeName}...";
		var mode = request.IsCut
			? AvaloniaCadEditor.Application.Interfaces.PartMergeMode.SubtractAsTool
			: AvaloniaCadEditor.Application.Interfaces.PartMergeMode.UnionAfterCuts;
		await ScriptEditor.InsertSolidIntoPartAsync(context, request.Code, mode);
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

	/// <summary>Nomes das features de topo isoladas pelo H. Guardados por nome (e não pelo objeto)
	/// porque cada execução do código recria todas as features com Ids novos; os nomes são estáveis.</summary>
	private System.Collections.Generic.HashSet<string> _isolatedNames = new();

	/// <summary>Sólidos que o isolamento escondeu nesta execução (só os que estavam visíveis); sair do
	/// isolamento reexibe exatamente estes.</summary>
	private System.Collections.Generic.List<AvaloniaCadEditor.Domain.Features.IFeature> _isolationHidden = new();

	public bool IsIsolating => _isolatedNames.Count > 0;

	/// <summary>A feature de topo isolada quando o isolamento é de uma peça só; senão <c>null</c>.
	/// Atualizada junto com <see cref="DisplayFrame"/> (que notifica a mudança).</summary>
	public IFeature? IsolatedFeature { get; private set; }

	/// <summary>Analisador de cotas da peça isolada (ver <see cref="AvaloniaCadEditor.UI.Drafting.DraftingViewModel"/>).</summary>
	public AvaloniaCadEditor.UI.Drafting.DraftingViewModel? Drafting { get; set; }

	private PartFrame? _displayFrame;
	/// <summary>Referencial da peça isolada (origem no canto mínimo, eixos da peça) ou <c>null</c>.
	/// Só visual: a grade e os eixos são desenhados nele; o código continua em coordenadas do mundo.</summary>
	public PartFrame? DisplayFrame
	{
		get => _displayFrame;
		private set
		{
			if (!ReferenceEquals(_displayFrame, value))
			{
				_displayFrame = value;
				OnPropertyChanged();
			}
		}
	}

	/// <summary>Tecla H: com sólido(s) selecionado(s), oculta todo o restante do modelo (isolar seleção);
	/// apertando de novo, reexibe o que foi escondido. O isolamento sobrevive à reexecução do código
	/// (ver <see cref="ReapplyIsolation"/>).</summary>
	private void ToggleIsolateSelection()
	{
		if (IsIsolating)
		{
			var revealed = _isolationHidden;
			foreach (var feature in revealed)
			{
				feature.IsVisible = true;
			}

			_isolationHidden = new();
			_isolatedNames = new();
			IsolatedFeature = null;
			DisplayFrame = null;
			OnPropertyChanged(nameof(IsIsolating));
			// Se o código rodou com a peça isolada, o resto não tem malha desta execução: construí-la aqui
			// travaria a janela. ShowFeaturesAsync a constrói em segundo plano e chama o RefreshProfiles.
			if (ScriptEditor != null) _ = ScriptEditor.ShowFeaturesAsync(revealed);
			else RefreshProfiles();
			return;
		}

		// Dentro de uma entidade aberta, o H esconde o resto do modelo: isola a própria entidade, já que o
		// que se seleciona lá dentro são faces e vazios, não sólidos da cena.
		var selected = OpenEntity is { } open
			? new System.Collections.Generic.List<IFeature> { open }
			: PreSelectedSolids();
		if (selected.Count == 0)
		{
			return;
		}

		_isolatedNames = selected.Select(f => f.Name).ToHashSet();
		ApplyIsolation();
		OnPropertyChanged(nameof(IsIsolating));
		RefreshProfiles();
	}

	/// <summary>Chamado depois que uma execução do código recolocou as features na cena (e antes de
	/// refazer a malha): esconde de novo tudo que não é a peça isolada e recalcula o referencial. Se
	/// a peça isolada não existe mais no código, encerra o isolamento.</summary>
	public void ReapplyIsolation()
	{
		if (!IsIsolating)
		{
			return;
		}

		_isolationHidden = new();
		if (!Sketch.Features.Any(f => _isolatedNames.Contains(f.Name)))
		{
			_isolatedNames = new();
			IsolatedFeature = null;
			DisplayFrame = null;
			OnPropertyChanged(nameof(IsIsolating));
			return;
		}

		ApplyIsolation();
	}

	/// <summary>As features recém-saídas de uma execução que vão aparecer depois de <see cref="ReapplyIsolation"/>
	/// (mesma regra dele): só essas têm a malha construída antes da troca de cena.</summary>
	public System.Collections.Generic.List<IFeature> FeaturesVisibleAfterRun(System.Collections.Generic.IReadOnlyList<IFeature> features)
	{
		bool isolating = IsIsolating && features.Any(f => _isolatedNames.Contains(f.Name));
		return features
			.Where(f => f.IsVisible &&
				(!isolating || _isolatedNames.Contains(f.Name) || f is AvaloniaCadEditor.Domain.Features.WorkPlaneFeature))
			.ToList();
	}

	/// <summary>Sólidos da cena na tela ficaram prontos em segundo plano (ver <c>SceneScriptViewModel.RunCodeAsync</c>):
	/// entram na malha composta e, se um deles é a peça isolada, o referencial dela sai da malha agora completa.</summary>
	public void RefreshBackgroundMeshes()
	{
		RefreshSolidMesh(skipCodeGeneration: true);
		if (IsolatedFeature != null)
		{
			DisplayFrame = AvaloniaCadEditor.Application.Isolation.PartFrameResolver.Resolve(IsolatedFeature, GetFeatureMesh(IsolatedFeature.Id)?.Positions);
		}
	}

	private void ApplyIsolation()
	{
		var isolated = new System.Collections.Generic.List<IFeature>();
		foreach (var feature in Sketch.Features)
		{
			if (_isolatedNames.Contains(feature.Name))
			{
				isolated.Add(feature);
				continue;
			}

			if (feature is AvaloniaCadEditor.Domain.Features.WorkPlaneFeature)
			{
				continue;
			}

			if (feature.IsVisible)
			{
				feature.IsVisible = false;
				_isolationHidden.Add(feature);
			}
		}

		// Referencial só para uma peça única; seleção múltipla isola sem mover a grade.
		IsolatedFeature = isolated.Count == 1 ? isolated[0] : null;
		DisplayFrame = isolated.Count == 1
			? AvaloniaCadEditor.Application.Isolation.PartFrameResolver.Resolve(isolated[0], GetFeatureMesh(isolated[0].Id)?.Positions)
			: null;

		// Sem referencial próprio (feature solta, sem Placement), as cotas não têm folha: avisa em
		// vez de ficar quieto — qualquer peça posicionada (girada ou não) tem referencial.
		if (isolated.Count == 1 && DisplayFrame == null)
		{
			StatusText = $"⚠ {isolated[0].Name}: sem referencial próprio (feature solta) — cotas indisponíveis.";
		}
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

	/// <summary>
	/// Em que nível o usuário está: na cena, dentro de uma entidade aberta (duplo clique) ou num esboço.
	/// Não há mais seletor Sólido/Face/Aresta: o nível decide o que se seleciona.
	/// </summary>
	public AvaloniaCadEditor.Application.EditContext.SelectionLevel SelectionLevel =>
		AvaloniaCadEditor.Application.EditContext.SelectionLevels.Resolve(IsSketchEditing, OpenEntity != null);

	/// <summary>O que a seleção pega, derivado do nível: sólidos na cena, faces dentro de uma entidade, linhas no esboço.</summary>
	public TargetSelectionMode CurrentSelectionMode => SelectionLevel switch
	{
		AvaloniaCadEditor.Application.EditContext.SelectionLevel.Sketch => TargetSelectionMode.Edge,
		AvaloniaCadEditor.Application.EditContext.SelectionLevel.Entity => TargetSelectionMode.Face,
		_ => TargetSelectionMode.Feature
	};

	/// <summary>
	/// O que um clique pega nos sólidos: faces com uma entidade aberta, sólidos inteiros na cena. No esboço, vale
	/// para o clique que não acerta linha nem região (o esboço fica por cima do nível, não o substitui); fora
	/// dele, é igual ao <see cref="CurrentSelectionMode"/>.
	/// </summary>
	public TargetSelectionMode SolidSelectionMode =>
		AvaloniaCadEditor.Application.EditContext.SelectionLevels.Beneath(OpenEntity != null)
			== AvaloniaCadEditor.Application.EditContext.SelectionLevel.Entity
			? TargetSelectionMode.Face
			: TargetSelectionMode.Feature;

	/// <summary>O nível atual por extenso, no lugar do antigo seletor da barra lateral.</summary>
	public string SelectionLevelText =>
		AvaloniaCadEditor.Application.EditContext.SelectionLevels.Describe(SelectionLevel, _activeBooleanGroupName);

	private void NotifySelectionLevelChanged()
	{
		OnPropertyChanged(nameof(SelectionLevel));
		OnPropertyChanged(nameof(CurrentSelectionMode));
		OnPropertyChanged(nameof(SolidSelectionMode));
		OnPropertyChanged(nameof(SelectionLevelText));
	}

	public bool IsInCustomWorkPlane => !ActiveWorkPlane.Equals(WorkPlane.XY);

	/// <summary>Nome do plano ativo, para o painel esquerdo dizer em que plano o próximo clique cai.</summary>
	public string ActiveWorkPlaneLabel =>
		Sketch.WorkPlanes.FirstOrDefault(w => w.Plane.Equals(ActiveWorkPlane))?.Name ?? "Plano ativo";

	/// <summary>Pick radius around the cursor, in screen pixels, for sketch entity hit-testing.</summary>
	private const double HitTolerancePixels = 6.0;

	/// <summary>Size of one screen pixel in world units at the cursor, measured on the active work plane by the viewport.</summary>
	public double WorldUnitsPerPixel { get; set; } = 1.0;

	private double HitTolerance => HitTolerancePixels * WorldUnitsPerPixel;

	private void ApplyHitTolerance()
	{
		// Por capacidade, não por tipo: era um case por ferramenta, e cada ferramenta nova com
		// clique tolerante virava mais uma linha aqui.
		if (CurrentTool is IHitTolerantTool tool) tool.HitTolerance = HitTolerance;
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
	public ICommand SelectSweepToolCommand { get; }
	public ICommand SelectBooleanCutCommand { get; }
	public ICommand SelectBooleanFuseCommand { get; }
	public ICommand SelectFilletToolCommand { get; }
	public ICommand SelectSelectToolCommand { get; }
	public ICommand SelectMoveToolCommand { get; }
	public ICommand SelectRotateToolCommand { get; }
	public ICommand SelectSetWorkPlaneCommand { get; }

	/// <summary>Ativa um dos planos de origem ("XY", "XZ" ou "YZ") e entra em modo de esboço nele.</summary>
	public ICommand SelectOriginWorkPlaneCommand { get; }
	public ICommand DeleteSelectedCommand { get; }
	public ICommand ExitWorkPlaneCommand { get; }
	public ICommand ReconstructMeshCommand { get; }

	public ICommand ImportStlCommand { get; }
	public ICommand ExportStlCommand { get; }
	public ICommand ExportSketchUpCommand { get; }

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

	private bool _isWireframe;

	/// <summary>
	/// Modo de exibição "desenho técnico": só arestas e silhuetas, com as escondidas atrás de
	/// faces em tracejado (<see cref="AvaloniaCadEditor.UI.Controls.Rendering.WireframeRenderPass"/>).
	/// Estado de sessão, de propósito fora de <c>TessellationSettings</c>: não muda a malha, então
	/// não pode entrar na chave do cache de malhas.
	/// </summary>
	public bool IsWireframe
	{
		get => _isWireframe;
		set
		{
			if (_isWireframe == value) return;
			_isWireframe = value;
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

	/// <summary>
	/// Configuração de tesselação em vigor. Existe para o viewport montar o
	/// <see cref="AvaloniaCadEditor.UI.Controls.Rendering.RenderContext"/> de cada frame sem
	/// instanciar um provider de Infrastructure por conta própria.
	/// </summary>
	public AvaloniaCadEditor.Domain.Settings.TessellationSettings TessellationSettings =>
		_settingsProvider.GetSettings();

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
	private readonly ISketchProjectionService _projections;
	private readonly AvaloniaCadEditor.Application.Export.SceneExportService? _sceneExport;

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
		IPlanarTopologyService? planarTopologyService = null,
		ISketchProjectionService? projectionService = null,
		ISceneFileWriter? sceneFileWriter = null)
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
		_projections = projectionService ?? new SketchProjectionService(MeshManager, new AvaloniaCadEditor.Infrastructure.Geometry.ClipperPolygonUnion());
		// Sem default: quem não injeta o escritor (os testes) fica sem o export, em vez de puxar o Infrastructure.
		_sceneExport = sceneFileWriter != null ? new AvaloniaCadEditor.Application.Export.SceneExportService(MeshManager, sceneFileWriter) : null;

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
		SelectSweepToolCommand = new RelayCommand(() =>
		{
			// A região marcada é a seção; as arestas marcadas são o caminho. Irmão do
			// PreSelectedSolids() que alimenta o BooleanTool.
			var secao = _selectionService.SelectedObject as IProfile;
			var arestas = Sketch.Entities.Where(e => e.IsSelected).ToList();

			CurrentMode = EditorMode.SketchEdit;
			var sweepCmd = new SweepCommand(secao, arestas, _geometryEngine);
			CurrentTool = sweepCmd;

			// Depois do setter, que é quem assina CodeGenerated.
			sweepCmd.TryStartFromSelection(Sketch);
			UpdateStatusText(CurrentSnappedPoint);
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

			// O que o chanfro pega vem do nível: na cena, o sólido inteiro; dentro de uma entidade aberta,
			// as faces dela.

			// Just store the sketch so the tool is ready.
			// Do NOT automatically execute! The user must press Enter.
			filletCmd.OnMouseDown(Sketch, new AvaloniaCadEditor.Domain.ValueObjects.Point3D(0, 0, 0));
		});

		SelectSelectToolCommand = new RelayCommand(() => CurrentTool = new SelectTool(_geometryEngine, _selectionService));
		SelectMoveToolCommand = new RelayCommand(() => CurrentTool = new MoveTool { Plane = ActiveWorkPlane, SelectionOverride = HighlightedPartSolidSelection });
		SelectRotateToolCommand = new RelayCommand(() => CurrentTool = new RotateTool { WorldUnitsPerPixel = WorldUnitsPerPixel, Plane = ActiveWorkPlane, SelectionOverride = HighlightedPartSolidSelection });
		SelectSetWorkPlaneCommand = new RelayCommand(() => CurrentTool = new SetWorkPlaneCommand());

		// Entra em esboço no plano, não só troca ActiveWorkPlane: a grade só gira em SketchEdit
		// (ver GridRenderPass), e sem ela nada na viewport diz em que plano o próximo clique cai.
		SelectOriginWorkPlaneCommand = new RelayCommand<string>(which =>
		{
			var plane = which switch
			{
				"XZ" => AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XZ,
				"YZ" => AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.YZ,
				_ => AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY
			};

			var wpf = Sketch.WorkPlanes.FirstOrDefault(w => w.Plane.Equals(plane));
			if (wpf == null) return;

			EnterSketchMode(wpf);
		});

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

		ExportSketchUpCommand = new RelayCommand(async () =>
		{
			if (_sceneExport == null) return;

			var path = await _filePickerService.SaveFileAsync("Export to SketchUp", "dae", "cena");
			if (string.IsNullOrEmpty(path)) return;

			try
			{
				StatusText = (await _sceneExport.ExportAsync(Sketch.Features.ToList(), path)).Message;
			}
			catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
			{
				StatusText = $"✗ Exportar: não foi possível gravar o arquivo ({ex.Message}).";
			}
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
				// Entrar em outra entidade (ou voltar à cena) é trocar de nível: o esboço em que se estava fica para
				// trás, senão ele continuaria por cima e a entidade nova abriria pegando linhas em vez de faces.
				if (IsSketchEditing) ExitSketchMode();
				HighlightedToolId = null;
				SelectedPartSolid = null;
				// Entrar ou sair muda o que é selecionável: o que estava marcado no nível anterior (um sólido
				// da cena, uma face da entidade) não faz sentido no novo.
				_selectionService.ClearSelection(Sketch);
				HoveredFeatureId = null;
				HoveredFaceProfile = null;
				OnPropertyChanged();
				NotifySelectionLevelChanged();
				OnPropertyChanged(nameof(IsEditingBooleanGroup));
				OnPropertyChanged(nameof(ActiveBooleanGroup));
				OnPropertyChanged(nameof(EditContext));
				OnPropertyChanged(nameof(ActiveGroupTitle));
				OnPropertyChanged(nameof(ActiveGroupDetail));
				ApplyEditContextToTool();
			}
		}
	}

	/// <summary>
	/// A peça aberta com duplo clique, se o grupo aberto for uma instância de peça: as ferramentas que criam
	/// geometria gravam no <c>Build()</c> dela. Achada pelo nome a cada leitura, porque cada execução da cena
	/// recria as instâncias.
	/// </summary>
	public AvaloniaCadEditor.Application.EditContext.PartEditContext? EditContext =>
		string.IsNullOrEmpty(_activeBooleanGroupName)
			? null
			: AvaloniaCadEditor.Application.EditContext.PartEditContext.For(
				Sketch.Features.FirstOrDefault(f => f.Name == _activeBooleanGroupName), Sketch.Features);

	/// <summary>Título do letreiro do grupo aberto.</summary>
	public string ActiveGroupTitle => OpenEntity switch
	{
		PartInstance => "Editando peça:",
		AvaloniaCadEditor.Domain.Features.BooleanFeature => "Editando grupo booleano:",
		_ => "Editando sólido:"
	};

	/// <summary>
	/// Nome do grupo aberto; numa peça, também o tipo e, com mais de uma instância, o aviso de que todas
	/// mudam juntas — visível enquanto a peça está aberta, não só depois de gravar.
	/// </summary>
	public string? ActiveGroupDetail
	{
		get
		{
			var context = EditContext;
			if (context == null) return _activeBooleanGroupName;
			string detail = $"{context.InstanceName} ({context.PartTypeName})";
			if (context.InstanceCount <= 1) return detail;
			detail = $"{detail} · {context.InstanceCount} instâncias: todas mudam";
			return context.DifferingParameters is { Count: > 0 } differing
				? $"{detail}, mas com {string.Join(", ", differing)} diferente"
				: detail;
		}
	}

	/// <summary>
	/// Depois de uma execução (que recria todas as features): se a entidade aberta sumiu do código
	/// (apagada, renomeada), volta ao nível da cena; se continua, atualiza o letreiro (o número de
	/// instâncias pode ter mudado).
	/// </summary>
	public void ReapplyOpenEntity()
	{
		if (!IsEditingBooleanGroup) return;
		if (OpenEntity == null)
		{
			ActiveBooleanGroupName = null;
			return;
		}
		OnPropertyChanged(nameof(EditContext));
		OnPropertyChanged(nameof(ActiveGroupTitle));
		OnPropertyChanged(nameof(ActiveGroupDetail));
		NotifySelectionLevelChanged();
		ApplyEditContextToTool();
	}

	/// <summary>Com uma peça aberta, o Extrude gera o sólido autônomo que vai para o <c>Build()</c> dela.</summary>
	private void ApplyEditContextToTool()
	{
		if (_currentTool is ExtrudeCommand extrude)
		{
			extrude.Target = EditContext != null
				? AvaloniaCadEditor.Application.Services.ExtrusionTarget.Standalone
				: AvaloniaCadEditor.Application.Services.ExtrusionTarget.Scene;
		}
	}

	public AvaloniaCadEditor.Domain.Features.BooleanFeature? ActiveBooleanGroup =>
        Sketch.Features.OfType<AvaloniaCadEditor.Domain.Features.BooleanFeature>().FirstOrDefault(f => f.Name == _activeBooleanGroupName);

	public bool IsEditingBooleanGroup => !string.IsNullOrEmpty(_activeBooleanGroupName);

	/// <summary>A entidade de topo aberta com duplo clique (peça, booleana ou sólido solto); null no nível da cena.</summary>
	public IFeature? OpenEntity =>
		string.IsNullOrEmpty(_activeBooleanGroupName)
			? null
			: Sketch.Features.FirstOrDefault(f => f.Name == _activeBooleanGroupName);

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
	/// (em Pecas/ para ferramentas de peça, no arquivo da cena para booleanas soltas). Num sólido de
	/// peça, também guarda em <see cref="SelectedPartSolid"/> a cadeia que o produz, para o inspector
	/// listar só os parâmetros que o afetam.
	/// </summary>
	public void RevealToolFeature(IFeature tool)
	{
		HighlightedToolId = tool.Id;

		if (tool is TransformedFeature wrapper && _partToolsOwner != null && _partTools.Contains(tool))
		{
			var local = wrapper.Source;
			StatusText = $"{local.Name} ({local.GetType().Name}) — {_partToolsOwner.PartTypeName}";
			SelectedPartSolidOwner = _partToolsOwner;
			SelectedPartSolid = ScriptEditor?.RevealPartTool(_partToolsOwner.PartTypeName, local.Name);
			if (SelectedPartSolid != null)
			{
				PropertyInspector.ShowPartSolid(_partToolsOwner, SelectedPartSolid);
			}
		}
		else
		{
			StatusText = $"{tool.Name} ({tool.GetType().Name})";
			SelectedPartSolid = null;
			ScriptEditor?.RevealLooseFeatureLine(tool);
		}
	}

	private AvaloniaCadEditor.Application.Interfaces.PartSolidInputs? _selectedPartSolid;

	/// <summary>
	/// A cadeia que produz o sólido de peça clicado em <see cref="RevealToolFeature"/> — o que o
	/// alimenta e o que é exclusivo dele. Null quando não há sólido de peça selecionado (ou quando
	/// não deu para rastrear, ex.: sólido criado num laço).
	/// </summary>
	public AvaloniaCadEditor.Application.Interfaces.PartSolidInputs? SelectedPartSolid
	{
		get => _selectedPartSolid;
		private set
		{
			if (_selectedPartSolid == value) return;
			bool had = _selectedPartSolid != null;
			_selectedPartSolid = value;
			if (value == null)
			{
				SelectedPartSolidOwner = null;
				// Sair do modo booleano não muda a seleção, então o inspector não se atualizaria
				// sozinho e continuaria mostrando os parâmetros do sólido.
				if (had) PropertyInspector.ClearPartSolid();
			}
			OnPropertyChanged();
		}
	}

	/// <summary>A instância de peça a que <see cref="SelectedPartSolid"/> pertence.</summary>
	public PartInstance? SelectedPartSolidOwner { get; private set; }

	/// <summary>
	/// Reencontra o sólido de peça selecionado depois de a cena ser reexecutada (editar um
	/// parâmetro refaz tudo: as features são objetos novos e o <see cref="HighlightedToolId"/>,
	/// um Guid, fica obsoleto). A busca é por nome, como o resto do app faz depois de um re-run.
	/// </summary>
	public void RestoreSelectedPartSolid()
	{
		string? solidName = _selectedPartSolid?.SolidName;
		if (solidName == null) return;

		var tool = ActiveToolFeatures
			.FirstOrDefault(f => f is TransformedFeature w && w.Source.Name == solidName);
		if (tool == null)
		{
			SelectedPartSolid = null;
			return;
		}
		RevealToolFeature(tool);
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
	/// O alvo de um clique pelo nível (<see cref="AvaloniaCadEditor.Application.EditContext.SelectionLevels.Pick"/>).
	/// Na cena, o sólido mais perto. Com uma entidade aberta, só ela e os vazios dela: um vazio ganha do corpo
	/// mesmo atrás dele, senão nunca seria alcançado; com o Extrude, só o corpo, porque o alvo é uma face.
	/// </summary>
	private RaycastHit? RaycastScene(Ray ray)
	{
		var open = OpenEntity;
		if (open == null)
		{
			return MeshManager.Raycast3D(Sketch, ray);
		}

		var tools = ActiveToolFeatures;
		var hits = MeshManager.RaycastAll3D(Sketch, ray, f => tools.Contains(f), ExtraToolFeatures(tools));
		return AvaloniaCadEditor.Application.EditContext.SelectionLevels.Pick(
			hits, open.Id, tools.Select(t => t.Id).ToList(), bodyOnly: CurrentTool is ExtrudeCommand);
	}

	/// <summary>
	/// Raio para REFERÊNCIA, não para seleção: o sólido visível mais perto, em qualquer nível. Serve ao duplo
	/// clique (entrar em outra entidade) e ao plano de trabalho tirado de uma face, que podem usar qualquer
	/// objeto da cena mesmo com uma entidade aberta.
	/// </summary>
	public RaycastHit? RaycastReference(Ray ray) => MeshManager.Raycast3D(Sketch, ray);

	/// <summary>True se o acerto é num vazio (ferramenta) da entidade aberta, e não no corpo dela.</summary>
	public bool IsOpenEntityToolHit(RaycastHit? hit) =>
		hit?.FeatureId is Guid id && ActiveToolFeatures.Any(t => t.Id == id);

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
			// Só o que é selecionável no nível atual: com uma entidade aberta, ela e os vazios dela.
			var tools = ActiveToolFeatures;
			var hits = MeshManager.RaycastAll3D(Sketch, ray, f => tools.Contains(f), ExtraToolFeatures(tools));
			_pickCandidates = AvaloniaCadEditor.Application.EditContext.SelectionLevels
				.Selectable(hits, OpenEntity?.Id, tools.Select(t => t.Id).ToList())
				.ToList();
			_pickCandidateIndex = -1;
		}

		if (_pickCandidates.Count == 0) return;

		_pickCandidateIndex = (_pickCandidateIndex + 1) % _pickCandidates.Count;
		var candidate = _pickCandidates[_pickCandidateIndex];
		_pickOverride = candidate;

		ApplyHover(candidate);
	}

	/// <summary>
	/// Realce do que está sob o mouse, pelo nível: dentro de uma entidade, a face do corpo (ou o vazio
	/// inteiro); na cena, o sólido. O Extrude na cena não pega faces de sólido (é preciso entrar antes),
	/// então ali nada é realçado.
	/// </summary>
	private void ApplyHover(RaycastHit? hit)
	{
		if (CurrentTool is ExtrudeCommand && OpenEntity == null)
		{
			HoveredFaceProfile = null;
			HoveredFeatureId = null;
			return;
		}

		if (hit?.FeatureId != null && IsFacePickMode() && !IsOpenEntityToolHit(hit))
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
		InvalidatePlaneReferences();
		ReportBuildProblems();

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
	/// Leva à barra de status o motivo de uma feature não ter gerado geometria. Sem isto, uma
	/// primitiva degenerada (caminho de varredura inválido, seção que não cabe no raio de
	/// curvatura...) simplesmente some da viewport: o kernel devolve malha vazia, não erro, e a
	/// explicação morre lá dentro. Só sobrescreve o status quando HÁ problema — quem chamou pode
	/// ter acabado de escrever uma mensagem própria.
	/// </summary>
	private void ReportBuildProblems()
	{
		var problems = MeshManager.BuildProblems;
		if (problems.Count == 0) return;

		// A mensagem já traz o nome da feature. Com mais de uma, mostra a primeira e conta o resto:
		// a barra de status é uma linha só, e a primeira costuma ser a causa das demais.
		string first = problems.Values.First();
		StatusText = problems.Count == 1
			? $"✗ {first}"
			: $"✗ {first} (+{problems.Count - 1} outra(s) feature(s) sem geometria)";
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
		// No esboço o que existe para marcar são linhas e círculos: o nível Sketch faz o retângulo de
		// seleção (BoxSelector) pegá-las em vez de sólidos.
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
		// Chanfros/fillets suprimidos são só uma ajuda para desenhar: fora do esboço o modelo volta inteiro.
		IsFilletSuppressed = false;
		// Sair do esboço volta o nível (e com ele o tipo de seleção) para o da cena ou da entidade aberta.
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
				// Só os planos derivados de face entram na conta: os de origem já têm nome próprio, e
				// contar com eles faria o primeiro plano do usuário nascer como "Plano 3".
				var derivados = Sketch.WorkPlanes.Count(w => !w.IsBasePlane);
				var wpf = new AvaloniaCadEditor.Domain.Features.WorkPlaneFeature(value, $"Plano {derivados + 1}");
				Sketch.AddFeature(wpf);
			}

			ModelTree.RebuildTree();
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsInCustomWorkPlane));
			OnPropertyChanged(nameof(ActiveWorkPlaneLabel));
			RefreshSolidMesh();
		}
	}

	private Point3D? _cursorLocal;


	/// <summary>
	/// Desenhando (modo de esboço), o snap no sólido fica no plano de trabalho: só o contorno da face em
	/// que se desenha, nunca vértices de outra altura. Fora do esboço, Mover/Girar seguem com o snap 3D.
	/// </summary>
	private bool SolidSnapOnPlaneOnly => CurrentMode == EditorMode.SketchEdit;

	public bool OnPointerPressed(Point3D worldPoint, bool isCtrlPressed = false)
	{
		SyncToolPlane();
		var refPt = CurrentTool?.GetReferencePoint();
		var snappedWorld = _osnapService.GetSnappedPoint(Sketch, worldPoint, refPt != null ? ActiveWorkPlane.ToWorldSpace(refPt.Value) : null, SolidMesh, SolidSnapOnPlaneOnly, SolidSnapOnPlaneOnly ? PlaneReferences : null);
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
				// Ctrl acumula arestas - e preserva uma região já marcada - em vez de trocar a
				// seleção: é assim que se junta a seção e o caminho de uma varredura.
				if (isCtrlPressed || IsCtrlPressed)
					_selectionService.ToggleEntity(Sketch, hitEntity);
				else
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

					// Este bloco chama ClearSelection no meio do caminho, o que apagaria arestas já
					// marcadas - e são elas o caminho de uma varredura cuja seção é esta região.
					var manterArestas = Sketch.Entities.Where(e => e.IsSelected).ToList();

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

					foreach (var e in manterArestas) e.IsSelected = true;
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

		// Ferramenta que escolhe geometria desenhada (o Sweep) usa o ponto CRU: com snap, clicar
		// dentro de uma região pularia para a ponta de uma linha vizinha. Só se o clique não for
		// uma escolha ele segue adiante, aí com snap.
		if (CurrentTool is IGeometryPickingTool picker && picker.TryPickAt(Sketch, localRaw))
		{
			UpdateStatusText(localSnapped);
			return true;
		}

		string statusBefore = StatusText;
		if (CurrentTool is SelectTool)
		{
			// Fora do SketchEdit, a seleção de sólidos 3D é feita pelo raycast 3D no
			// ViewportInputHandler (que suporta Ctrl+clique para somar à seleção). Chamar
			// SelectTool.OnMouseDown aqui limparia a seleção antes desse fallback rodar.
			if (CurrentMode == EditorMode.SketchEdit)
				CurrentTool.OnMouseDown(Sketch, localRaw);
		}
		else
			CurrentTool?.OnMouseDown(Sketch, localSnapped);

		UpdateStatusTextUnlessToolReported(localSnapped, statusBefore);

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
		var snappedWorld = _osnapService.GetSnappedPoint(Sketch, worldPoint, refPt != null ? ActiveWorkPlane.ToWorldSpace(refPt.Value) : null, SolidMesh, SolidSnapOnPlaneOnly, SolidSnapOnPlaneOnly ? PlaneReferences : null);
		var localSnapped = ActiveWorkPlane.ToLocalSpace(snappedWorld);
		_cursorLocal = ActiveWorkPlane.ToLocalSpace(worldPoint);
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
		else if (CurrentTool is MirrorTool mirror) mirror.Plane = ActiveWorkPlane;
	}

	private void UpdateStatusText(Point3D snapped)
	{
		string toolInstruction = CurrentTool?.GetStatusText() ?? "";
		string prefix = string.IsNullOrEmpty(toolInstruction) ? "" : $"{toolInstruction} | ";
		if (EditContext is { } context)
		{
			// Peça aberta: as coordenadas são as do Build() dela, as mesmas que vão para o código.
			var local = context.ToLocal(snapped);
			StatusText = $"{prefix}{context.PartTypeName} (local) X: {local.X:F2}, Y: {local.Y:F2}, Z: {local.Z:F2}";
			OnPropertyChanged(nameof(VcbText));
			return;
		}
		StatusText = $"{prefix}X: {snapped.X:F2}, Y: {snapped.Y:F2}";

		OnPropertyChanged(nameof(VcbText)); // Force update
	}

	/// <summary>
	/// Como <see cref="UpdateStatusText"/>, mas preserva o que a ferramenta escreveu no status durante o
	/// evento: Mover/Girar/Espelhar concluem no clique (ou no Enter do VCB) e deixam ali o resultado
	/// ("✗ Não achei a chamada Add(...)", "Avaliando código..."), que a coordenada do cursor apagaria.
	/// </summary>
	private void UpdateStatusTextUnlessToolReported(Point3D snapped, string statusBefore)
	{
		if (StatusText == statusBefore)
			UpdateStatusText(snapped);
		else
			OnPropertyChanged(nameof(VcbText));
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

		string statusBefore = StatusText;
		if (VcbService.ProcessKeyDown(keyName, Sketch, CurrentTool, () => RefreshProfiles()))
		{
			UpdateStatusTextUnlessToolReported(CurrentSnappedPoint, statusBefore);
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
			case "S":
				SelectSweepToolCommand.Execute(null);
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
				else if (IsSketchEditing)
				{
					// Sai um nível: do esboço para a entidade aberta ou para a cena. Sem isto, as ferramentas de
					// desenho (L, C, G, T, X, O, S) deixavam o editor preso no esboço, pegando só linhas.
					ExitSketchMode();
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
				ToggleIsolateSelection();
				break;
			case "V":
				// Ctrl+V não é atalho de nada, mas cairia aqui: só V puro alterna o modo.
				if (!isCtrlPressed && !IsCtrlPressed) IsWireframe = !IsWireframe;
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
