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
				string code = GenerateExtrusionCode(_selectedProfile, height, sketch);
				CodeGenerated?.Invoke(this, code);
				IsFinished = true;
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
		IsPreviewDirty = false;
	}

	private ExtrudeFeature? _previewFeature;
	public bool IsPreviewDirty { get; set; }

	public IFeature? GetPreviewFeature()
	{
		if (_currentState == State.DefiningHeight && _selectedProfile != null)
		{
			if (_previewFeature == null || _previewFeature.Profile != _selectedProfile)
			{
				_previewFeature = new ExtrudeFeature(_selectedProfile, CurrentHeight);
				IsPreviewDirty = true; // Force initial build
			}
			else
			{
				_previewFeature.Height = CurrentHeight;
				_previewFeature.Direction = _selectedProfile.Normal * CurrentHeight; // Update direction based on height
			}
			return _previewFeature;
		}
		_previewFeature = null;
		return null;
	}

	public IEnumerable<Entity> GetPreviewEntities()
	{
		if (_currentState == State.DefiningHeight && _selectedProfile != null)
		{
			var previewLines = new List<Entity>();
			var baseVerts = _selectedProfile.GetVertices();
			if (baseVerts.Count == 0) return previewLines; // FaceProfile might not have simple vertices for preview

			var dir = _selectedProfile.Normal * CurrentHeight;
			var plane = _selectedProfile.Plane;

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

			return previewLines;
		}

		return Enumerable.Empty<Entity>();
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
			string code = GenerateExtrusionCode(_selectedProfile, value, sketch);
			CodeGenerated?.Invoke(this, code);
			IsFinished = true;
		}
		Cancel();
	}

	private string GenerateExtrusionCode(IProfile profile, double height, Sketch? sketch)
	{
		return AvaloniaCadEditor.Application.Services.FeatureCodeGenerator.GenerateExtrusionCode(profile, height, sketch);
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
