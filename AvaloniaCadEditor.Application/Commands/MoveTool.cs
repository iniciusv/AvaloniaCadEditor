using System.Linq;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class MoveTool : ICommandTool
{
    public event System.EventHandler<string>? CodeGenerated;
    public event System.Action<Vector3D, IEnumerable<Domain.Features.IFeature>>? FeaturesMoved;
    public string Name => "Move";

    private enum State { WaitingForBasePoint, WaitingForTargetPoint }
    private State _currentState = State.WaitingForBasePoint;

    private Point3D _basePoint;
    private Point3D _currentMouse;
    public bool IsCopyMode { get; private set; } = false;
    
    private List<Entity> _previewClones = new List<Entity>();
    private List<Entity> _selectedOriginals = new List<Entity>();
    private List<Domain.Features.IFeature> _selectedFeatures = new();
    
    private AxisConstraint _constraint = AxisConstraint.None;

    public IEnumerable<Domain.Features.IFeature> DraggedFeatures => _currentState == State.WaitingForTargetPoint ? _selectedFeatures : Enumerable.Empty<Domain.Features.IFeature>();
    public Vector3D CurrentTranslation => _currentState == State.WaitingForTargetPoint ? _currentMouse - _basePoint : new Vector3D(0, 0, 0);

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        if (_currentState == State.WaitingForBasePoint)
        {
            _basePoint = point;
            _currentMouse = point;
            
            _selectedOriginals = sketch.Entities.Where(e => e.IsSelected).ToList();
            _previewClones = _selectedOriginals.Select(e => e.Clone()).ToList();
            
            _selectedFeatures = sketch.Features.Where(f => f.IsSelected).ToList();
            
            _currentState = State.WaitingForTargetPoint;
        }
        else if (_currentState == State.WaitingForTargetPoint)
        {
            var constrainedPoint = AxisConstraintHelper.Apply(point, _basePoint, _constraint);
            var vector = constrainedPoint - _basePoint;
            ApplyMove(sketch, vector);
        }
    }

    private void ApplyMove(Sketch sketch, Vector3D vector)
    {
        if (IsCopyMode)
        {
            foreach (var entity in _selectedOriginals)
            {
                var clone = entity.Clone();
                clone.Translate(vector);
                sketch.AddEntity(clone);
            }
        }
        else
        {
            foreach (var entity in _selectedOriginals)
            {
                entity.Translate(vector);
            }
            
            if (_selectedFeatures.Any())
            {
                FeaturesMoved?.Invoke(vector, _selectedFeatures);
            }
        }

        _currentState = State.WaitingForBasePoint;
        _previewClones.Clear();
        _selectedOriginals.Clear();
        _selectedFeatures.Clear();
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        if (_currentState == State.WaitingForTargetPoint)
        {
            _currentMouse = AxisConstraintHelper.Apply(point, _basePoint, _constraint);
        }
        else
        {
            _currentMouse = point;
        }
    }

    public void Cancel()
    {
        _currentState = State.WaitingForBasePoint;
        _previewClones.Clear();
        _selectedOriginals.Clear();
        _selectedFeatures.Clear();
        _constraint = AxisConstraint.None;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Control)
        {
            IsCopyMode = !IsCopyMode;
        }
        else if (_currentState == State.WaitingForTargetPoint)
        {
            _constraint = AxisConstraintHelper.ToggleConstraint(key, _constraint);
            _currentMouse = AxisConstraintHelper.Apply(_currentMouse, _basePoint, _constraint);
        }
    }

    public void OnValueEntered(Sketch sketch, double value)
    {
        if (_currentState == State.WaitingForTargetPoint)
        {
            var constrainedPoint = AxisConstraintHelper.Apply(_currentMouse, _basePoint, _constraint);
            var currentVector = constrainedPoint - _basePoint;
            if (currentVector.Length > 0)
            {
                var direction = currentVector.Normalize();
                var vector = direction * value;
                ApplyMove(sketch, vector);
            }
        }
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.WaitingForTargetPoint)
        {
            var vector = _currentMouse - _basePoint;
            
            foreach (var clone in _previewClones)
            {
                var previewEntity = clone.Clone();
                previewEntity.Translate(vector);
                yield return previewEntity;
            }
            
            yield return new LineEntity(_basePoint, _currentMouse);
        }
    }

    public string GetMeasurementText()
    {
        if (_currentState == State.WaitingForTargetPoint)
        {
            var distance = (_currentMouse - _basePoint).Length;
            return $"Distance: {distance:F2}";
        }
        return "";
    }
}


