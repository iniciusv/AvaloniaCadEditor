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

            // 1. Top perimeter lines
            for (int i = 0; i < baseVerts.Count; i++)
            {
                var p1 = baseVerts[i];
                var p2 = baseVerts[(i + 1) % baseVerts.Count];

                var top1 = new Point3D(p1.X + dir.X, p1.Y + dir.Y, p1.Z + dir.Z);
                var top2 = new Point3D(p2.X + dir.X, p2.Y + dir.Y, p2.Z + dir.Z);

                previewLines.Add(new LineEntity(top1, top2));
            }

            // 2. Vertical corner lines
            int step = Math.Max(1, baseVerts.Count / 8);
            for (int i = 0; i < baseVerts.Count; i += step)
            {
                var p1 = baseVerts[i];
                var top1 = new Point3D(p1.X + dir.X, p1.Y + dir.Y, p1.Z + dir.Z);
                previewLines.Add(new LineEntity(p1, top1));
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
            _selectedProfile = sketch.Profiles.LastOrDefault();
        }

        if (_selectedProfile != null)
        {
            var feature = new ExtrudeFeature(_selectedProfile, value);
            sketch.AddFeature(feature);
        }
        Cancel();
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
            .OrderBy(p => p.Center.DistanceTo(point))
            .FirstOrDefault();
    }
}
