using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.Resources;

namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>
/// Referências do plano de trabalho no esboço: o que o plano corta (<see cref="PlaneSection"/>), o que o usuário
/// projetou nele (<see cref="Application.Services.ISketchProjectionService"/>) e a supressão temporária de
/// chanfros/fillets, que deixa marcar as quinas do sólido base. Aqui só se delega e se avisa a tela.
/// </summary>
public partial class CadViewModel
{
	private readonly Dictionary<Guid, (TriangleMesh Mesh, WorkPlane Plane, List<PlaneReference> References)> _planeReferenceCache = new();
	private IReadOnlyList<PlaneReference>? _planeReferences;
	private WorkPlane? _planeReferencesPlane;

	/// <summary>
	/// Geometria de referência do plano de trabalho no esboço: os contornos das faces que estão no plano, o
	/// corte dos sólidos que o atravessam (<see cref="PlaneSection"/>) e as faces/sólidos projetados. Para o
	/// corte, só sólidos visíveis — o H e o olho da árvore limpam o excesso — e sem o preview da extrusão. É
	/// calculada por feature (sólidos sobrepostos não misturam laços) e guardada junto da malha e do plano: o
	/// arrasto da extrusão chama <see cref="RefreshSolidMesh"/> a cada passo, mas a malha das outras features é a
	/// mesma instância.
	/// </summary>
	public IReadOnlyList<PlaneReference> PlaneReferences
	{
		get
		{
			if (CurrentMode != EditorMode.SketchEdit) return Array.Empty<PlaneReference>();
			if (_planeReferences != null && Equals(_planeReferencesPlane, ActiveWorkPlane)) return _planeReferences;

			_planeReferencesPlane = ActiveWorkPlane;
			_planeReferences = BuildSectionReferences(ActiveWorkPlane)
				.Concat(_projections.ReferencesFor(Sketch, ActiveWorkPlane))
				.ToList();
			return _planeReferences;
		}
	}

	/// <summary>
	/// As referências de corte que valem a pena mostrar agora: só enquanto uma ferramenta de desenho está
	/// ativa, e só as que passam a menos da tolerância de snap do cursor. Todas desenhadas o tempo todo
	/// poluíam a tela; o snap continua usando <see cref="PlaneReferences"/> inteiro.
	/// </summary>
	public IReadOnlyList<PlaneReference> NearbyPlaneReferences
	{
		get
		{
			bool drawing = _currentTool is DrawLineCommand or DrawCircleCommand or DrawPolygonCommand;
			if (!drawing || _cursorLocal is not { } cursor) return Array.Empty<PlaneReference>();

			double tolerance = _osnapService.Settings.SnapTolerance;
			return PlaneReferences
				.Where(r => r.Kind != PlaneReferenceKind.Projected && r.DistanceTo(cursor) <= tolerance)
				.ToList();
		}
	}

	/// <summary>As projeções escolhidas pelo usuário: sempre desenhadas no esboço, ao contrário das de corte.</summary>
	public IReadOnlyList<PlaneReference> ProjectedPlaneReferences =>
		PlaneReferences.Where(r => r.Kind == PlaneReferenceKind.Projected).ToList();

	/// <summary>A malha ou as projeções mudaram: a próxima leitura de <see cref="PlaneReferences"/> recalcula.</summary>
	private void InvalidatePlaneReferences() => _planeReferences = null;

	private void OnProjectionsChanged()
	{
		InvalidatePlaneReferences();
		OnPropertyChanged(nameof(ProjectedPlaneReferences));
	}

	private List<PlaneReference> BuildSectionReferences(WorkPlane plane)
	{
		var result = new List<PlaneReference>();
		var alive = new HashSet<Guid>();
		foreach (var feature in Sketch.Features)
		{
			if (!feature.IsVisible || feature is WorkPlaneFeature) continue;
			var mesh = MeshManager.GetFeatureMesh(Sketch, feature.Id, null);
			if (mesh == null || mesh.TriangleCount == 0) continue;

			alive.Add(feature.Id);
			if (!_planeReferenceCache.TryGetValue(feature.Id, out var cached)
				|| !ReferenceEquals(cached.Mesh, mesh) || !Equals(cached.Plane, plane))
			{
				cached = (mesh, plane, PlaneSection.Compute(mesh, plane));
				_planeReferenceCache[feature.Id] = cached;
			}
			result.AddRange(cached.References);
		}

		foreach (var stale in _planeReferenceCache.Keys.Where(id => !alive.Contains(id)).ToList())
			_planeReferenceCache.Remove(stale);
		return result;
	}

	// ---- Projeções -------------------------------------------------------------------------------------

	/// <summary>O sólido sob o cursor que pode ser projetado no plano do esboço (fora do esboço, nenhum).</summary>
	public RaycastHit? RaycastForProjection(Ray ray) =>
		CurrentMode == EditorMode.SketchEdit ? MeshManager.Raycast3D(Sketch, ray) : null;

	public bool IsProjected(RaycastHit hit, ProjectionTarget target) =>
		TryProjectionSource(hit, target) is { } source && _projections.Contains(Sketch, ActiveWorkPlane, source);

	public bool HasProjections => CurrentMode == EditorMode.SketchEdit && _projections.HasAny(ActiveWorkPlane);

	public void Project(RaycastHit hit, ProjectionTarget target)
	{
		if (TryProjectionSource(hit, target) is not { } source) return;
		_projections.Add(Sketch, ActiveWorkPlane, source);
		OnProjectionsChanged();
	}

	public void RemoveProjection(RaycastHit hit, ProjectionTarget target)
	{
		if (TryProjectionSource(hit, target) is not { } source) return;
		_projections.Remove(Sketch, ActiveWorkPlane, source);
		OnProjectionsChanged();
	}

	public void ClearProjections()
	{
		_projections.Clear(ActiveWorkPlane);
		OnProjectionsChanged();
	}

	/// <summary>Sólidos projetados mostram todas as arestas e silhuetas, em vez de só o contorno.</summary>
	public bool IsVolumeProjectionAllEdges
	{
		get => _projections.VolumeMode == VolumeProjectionMode.AllEdges;
		set
		{
			var mode = value ? VolumeProjectionMode.AllEdges : VolumeProjectionMode.Outline;
			if (_projections.VolumeMode == mode) return;
			_projections.VolumeMode = mode;
			OnProjectionsChanged();
			OnPropertyChanged();
		}
	}

	private ProjectionSource? TryProjectionSource(RaycastHit hit, ProjectionTarget target)
	{
		var feature = Sketch.Features.FirstOrDefault(f => f.Id == hit.FeatureId);
		if (feature == null) return null;
		return target == ProjectionTarget.Face ? ProjectionSource.Face(feature, hit) : ProjectionSource.Body(feature, hit);
	}

	// ---- Chanfros/fillets suprimidos -------------------------------------------------------------------

	/// <summary>
	/// Chanfros e fillets desligados por enquanto: a cena mostra o sólido base, com as quinas vivas para o snap.
	/// Não muda o modelo nem o código, e <see cref="ExitSketchMode"/> religa.
	/// </summary>
	public bool IsFilletSuppressed
	{
		get => MeshManager.SuppressFillets;
		set
		{
			if (MeshManager.SuppressFillets == value) return;
			MeshManager.SuppressFillets = value;
			RefreshSolidMesh(skipCodeGeneration: true);
			StatusText = value ? Strings.FilletsSuppressedStatus : Strings.FilletsRestoredStatus;
			OnPropertyChanged();
		}
	}
}
