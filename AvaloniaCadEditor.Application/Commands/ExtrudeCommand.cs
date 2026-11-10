using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class ExtrudeCommand : ICommandTool
{
	public string Name => "Extrude";

	private enum State { SelectingProfile, DefiningHeight }
	private State _currentState = State.SelectingProfile;
	public bool IsDefiningHeight => _currentState == State.DefiningHeight;

	private IProfile? _selectedProfile;
	public IProfile? SelectedProfile => _selectedProfile;
	public double CurrentHeight { get; private set; } = 20.0;
	private Point3D _startPoint;
	public Point3D StartPointWorld { get; private set; }
	public bool IsFinished { get; private set; }

	public event EventHandler<string>? CodeGenerated;

	/// <summary>
	/// Com uma peça aberta (duplo clique), o sólido é gerado autônomo e vai para o <c>Build()</c> dela por
	/// <see cref="SolidForPartRequested"/>; na cena (<see cref="AvaloniaCadEditor.Application.Services.ExtrusionTarget.Scene"/>),
	/// pelo <see cref="CodeGenerated"/> de sempre.
	/// </summary>
	public AvaloniaCadEditor.Application.Services.ExtrusionTarget Target { get; set; } = AvaloniaCadEditor.Application.Services.ExtrusionTarget.Scene;

	/// <summary>
	/// Sólido autônomo (só literais, em coordenadas do mundo) para gravar na peça aberta. <c>IsCut</c> é a
	/// altura negativa: o sólido entra no material e vira um corte; positiva, ele é unido.
	/// </summary>
	public event EventHandler<(string Code, bool IsCut)>? SolidForPartRequested;

	public void OnMouseDown(Sketch sketch, Point3D point)
	{
		if (_currentState == State.SelectingProfile)
		{
			// Pick profile closest to click or default to last profile
			_selectedProfile = FindProfileAtPoint(sketch, point) ?? sketch.Profiles.LastOrDefault();

			if (_selectedProfile != null)
			{
				_startPoint = point;
				_currentState = State.DefiningHeight;
			}
		}
		else if (_currentState == State.DefiningHeight)
		{
			if (_selectedProfile != null)
			{
				double height = Math.Abs(CurrentHeight) < 1e-3 ? 20.0 : CurrentHeight;
				Commit(_selectedProfile, height, sketch);
			}
			Cancel();
		}
	}

	public void StartDirectExtrusion(Sketch sketch, IProfile profile, Point3D startPoint)
	{
		_selectedProfile = profile;
		_startPoint = startPoint;
		StartPointWorld = startPoint;
		CurrentHeight = 20.0;
		_currentState = State.DefiningHeight;
	}

	public void OnMouseMove(Sketch sketch, Point3D point)
	{
		if (_currentState == State.DefiningHeight && _selectedProfile != null)
		{
			// Fallback for 2D extrusion drag
			double dx = point.X - _startPoint.X;
			double dy = point.Y - _startPoint.Y;
			double distance = Math.Sqrt(dx * dx + dy * dy);

			// Determine sign based on general drag direction
			double sign = (dx + dy) >= 0 ? 1.0 : -1.0;
			double newHeight = distance > 0.1 ? distance * sign : 20.0;
			
			if (Math.Abs(CurrentHeight - newHeight) > 1e-4)
			{
				CurrentHeight = newHeight;
			}
			IsPreviewDirty = true;
		}
	}

	public void UpdateHeight(double height)
	{
		if (_currentState == State.DefiningHeight)
		{
			if (Math.Abs(CurrentHeight - height) > 1e-4)
			{
				CurrentHeight = height;
				IsPreviewDirty = true;
			}
		}
	}

	public void Cancel()
	{
		_currentState = State.SelectingProfile;
		_selectedProfile = null;
		CurrentHeight = 20.0;
		_previewFeature = null;
		_profileToBuildSource = null;
		_profileToBuild = null;
		IsPreviewDirty = false;
	}

	private ExtrudeFeature? _previewFeature;
	public bool IsPreviewDirty { get; set; }

	public IFeature? GetPreviewFeature()
	{
		if (_currentState == State.DefiningHeight && _selectedProfile != null)
		{
			var profile = ProfileToBuild();
			if (_previewFeature == null || _previewFeature.Profile != profile)
			{
				_previewFeature = new ExtrudeFeature(profile, CurrentHeight);
				IsPreviewDirty = true; // Force initial build
			}
			else
			{
				_previewFeature.Height = CurrentHeight;
				_previewFeature.Direction = profile.Normal * CurrentHeight; // Update direction based on height
			}
			return _previewFeature;
		}
		_previewFeature = null;
		return null;
	}

	private IProfile? _profileToBuildSource;
	private IProfile? _profileToBuild;

	/// <summary>
	/// O perfil que a prévia mostra: o mesmo que o código vai gerar. Regiões vizinhas marcadas com Ctrl
	/// são juntadas pelo <see cref="AvaloniaCadEditor.Application.Services.SketchRegionMerger"/>, como no
	/// <see cref="AvaloniaCadEditor.Application.Services.FeatureCodeGenerator"/>; sem isso a prévia mostraria
	/// um sólido por região enquanto o código grava um só.
	/// </summary>
	private IProfile ProfileToBuild()
	{
		if (!ReferenceEquals(_profileToBuildSource, _selectedProfile))
		{
			_profileToBuildSource = _selectedProfile;
			_profileToBuild = _selectedProfile;
			if (_selectedProfile is CompoundProfile compound)
			{
				var merged = AvaloniaCadEditor.Application.Services.SketchRegionMerger.MergeAdjacent(compound.SubProfiles);
				_profileToBuild = merged.Count == 1 ? merged[0] : new CompoundProfile(merged);
			}
		}
		return _profileToBuild!;
	}

	public IEnumerable<Entity> GetPreviewEntities()
	{
		if (_currentState == State.DefiningHeight && _selectedProfile != null)
		{
			var previewLines = new List<Entity>();
			var profile = ProfileToBuild();
			// Cada região é um contorno próprio: os vértices do composto são os de todas em fila, e um
			// laço só feito deles ligaria o fim de uma região ao começo da outra.
			var outlines = profile is CompoundProfile compound ? compound.SubProfiles : new[] { profile };
			foreach (var outline in outlines)
				AddPreviewOutline(previewLines, outline);
			return previewLines;
		}

		return Enumerable.Empty<Entity>();
	}

	private void AddPreviewOutline(List<Entity> previewLines, IProfile outline)
	{
		var baseVerts = outline.GetVertices();
		if (baseVerts.Count == 0) return; // FaceProfile might not have simple vertices for preview

		var dir = outline.Normal * CurrentHeight;
		var plane = outline.Plane;

		for (int i = 0; i < baseVerts.Count; i++)
		{
			var p1World = baseVerts[i];
			var p2World = baseVerts[(i + 1) % baseVerts.Count];

			var top1World = new Point3D(p1World.X + dir.X, p1World.Y + dir.Y, p1World.Z + dir.Z);
			var top2World = new Point3D(p2World.X + dir.X, p2World.Y + dir.Y, p2World.Z + dir.Z);

			var p1Local = plane.ToLocalSpace(p1World);
			var top1Local = plane.ToLocalSpace(top1World);
			var top2Local = plane.ToLocalSpace(top2World);

			previewLines.Add(new LineEntity(top1Local, top2Local) { Plane = plane });

			// For vertical lines, just draw at corners, but maybe all of them?
			int step = Math.Max(1, baseVerts.Count / 8);
			if (i % step == 0)
			{
				previewLines.Add(new LineEntity(p1Local, top1Local) { Plane = plane });
			}
		}
	}

	public void OnKeyDown(Sketch sketch, CadKey key)
	{
		if (key == CadKey.Escape)
		{
			Cancel();
		}
	}

	public void OnValueEntered(Sketch sketch, double value)
	{
		if (_selectedProfile == null)
		{
			_selectedProfile = sketch.Profiles.LastOrDefault(p => p.IsVisible);
		}

		if (_selectedProfile != null)
		{
			Commit(_selectedProfile, value, sketch);
		}
		Cancel();
	}

	private void Commit(IProfile profile, double height, Sketch sketch)
	{
		if (Target == AvaloniaCadEditor.Application.Services.ExtrusionTarget.Standalone && SolidForPartRequested != null)
		{
			string solid = AvaloniaCadEditor.Application.Services.FeatureCodeGenerator.GenerateExtrusionCode(
				profile, height, sketch, AvaloniaCadEditor.Application.Services.ExtrusionTarget.Standalone);
			SolidForPartRequested(this, (solid, height < 0));
		}
		else
		{
			CodeGenerated?.Invoke(this, AvaloniaCadEditor.Application.Services.FeatureCodeGenerator.GenerateExtrusionCode(profile, height, sketch));
		}
		IsFinished = true;
	}

	public string GetMeasurementText()
	{
		if (_currentState == State.DefiningHeight)
		{
			return $"Height: {CurrentHeight:F2}";
		}
		return "Click a Profile/Face or enter Height";
	}

	private static IProfile? FindProfileAtPoint(Sketch sketch, Point3D point)
	{
		return sketch.Profiles
			.Where(p => p.IsVisible && !(p is FaceProfile))
			.OrderBy(p => p.Center.DistanceTo(point))
			.FirstOrDefault();
	}
}
