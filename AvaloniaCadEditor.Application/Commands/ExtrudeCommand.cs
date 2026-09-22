using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class ExtrudeCommand : ICommandTool
{
    public string Name => "Extrude";

    private enum State { SelectingProfile, DefiningDepth }
    private State _currentState = State.SelectingProfile;

    private IProfile? _selectedProfile;
    private double _currentDepth = 20.0;
    private Point3D _startPoint;

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        if (_currentState == State.SelectingProfile)
        {
            // Pick profile closest to click or default to last profile
            _selectedProfile = FindProfileAtPoint(sketch, point) ?? sketch.Profiles.LastOrDefault();

            if (_selectedProfile != null)
            {
                _startPoint = point;
                _currentState = State.DefiningDepth;
            }
        }
        else if (_currentState == State.DefiningDepth)
        {
            if (_selectedProfile != null)
            {
                double depth = Math.Abs(_currentDepth) < 1e-3 ? 20.0 : _currentDepth;
                var feature = new ExtrudeFeature(_selectedProfile, depth);
                sketch.AddFeature(feature);
                HideProfileAndEntities(sketch, _selectedProfile);
            }
            Cancel();
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        if (_currentState == State.DefiningDepth && _selectedProfile != null)
        {
            // Calculate depth based on distance from click point
            double distance = point.DistanceTo(_startPoint);
            _currentDepth = distance > 0.1 ? distance : 20.0;
        }
    }

    public void Cancel()
    {
        _currentState = State.SelectingProfile;
        _selectedProfile = null;
        _currentDepth = 20.0;
    }

    public IFeature? GetPreviewFeature()
    {
        if (_currentState == State.DefiningDepth && _selectedProfile != null)
        {
            return new ExtrudeFeature(_selectedProfile, _currentDepth);
        }
        return null;
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.DefiningDepth && _selectedProfile != null)
        {
            var previewLines = new List<Entity>();
            var baseVerts = _selectedProfile.GetVertices();
            var dir = _selectedProfile.Normal * _currentDepth;
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
            var feature = new ExtrudeFeature(_selectedProfile, value);
            sketch.AddFeature(feature);
            HideProfileAndEntities(sketch, _selectedProfile);
        }
        Cancel();
    }

    private void HideProfileAndEntities(Sketch sketch, IProfile profile)
    {
        profile.IsVisible = false;
        foreach (var entityId in profile.SourceEntityIds)
        {
            var entity = sketch.Entities.FirstOrDefault(e => e.Id == entityId);
            if (entity != null)
            {
                entity.IsVisible = false;
            }
        }
    }

    public string GetMeasurementText()
    {
        if (_currentState == State.DefiningDepth)
        {
            return $"Height: {_currentDepth:F2}";
        }
        return "Click a Profile or enter Height";
    }

    private static IProfile? FindProfileAtPoint(Sketch sketch, Point3D point)
    {
        // Simple bounding check or distance to center
        return sketch.Profiles
            .Where(p => p.IsVisible)
            .OrderBy(p => p.Center.DistanceTo(point))
            .FirstOrDefault();
    }
}
