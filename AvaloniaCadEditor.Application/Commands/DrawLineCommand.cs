using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class DrawLineCommand : ICommandTool
{
    public event EventHandler<string>? CodeGenerated;
    public string Name => "Line";

    private enum State { WaitingForStartPoint, WaitingForEndPoint }
    private AvaloniaCadEditor.Domain.ValueObjects.WorkPlane? _currentPlane;
    private State _currentState = State.WaitingForStartPoint;

    private AxisConstraint _constraint = AxisConstraint.None;

    private double? _lengthConstraint = null;

    private Point3D _startPoint;
    private Point3D _currentMousePosition;

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        _currentPlane = sketch.Plane;
        if (_currentState == State.WaitingForStartPoint)
        {
            _startPoint = point;
            _currentMousePosition = point;
            _constraint = AxisConstraint.None;
            _lengthConstraint = null;
            _currentState = State.WaitingForEndPoint;
        }
        else if (_currentState == State.WaitingForEndPoint)
        {
            var finalPoint = ApplyConstraint(point);
            var line = new LineEntity(_startPoint, finalPoint) { Plane = sketch.Plane };
            sketch.AddEntity(line);
            
            // Loop back to start to draw contiguous lines
            _startPoint = finalPoint;
            _constraint = AxisConstraint.None;
            _lengthConstraint = null;
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        _currentPlane = sketch.Plane;
        if (_currentState == State.WaitingForEndPoint)
        {
            _currentMousePosition = ApplyConstraint(point);
        }
        else
        {
            _currentMousePosition = point;
        }
    }

    public void Cancel()
    {
        _currentState = State.WaitingForStartPoint;
        _constraint = AxisConstraint.None;
        _lengthConstraint = null;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (_currentState == State.WaitingForEndPoint)
        {
            _constraint = AxisConstraintHelper.ToggleConstraint(key, _constraint);
            _currentMousePosition = ApplyConstraint(_currentMousePosition);
        }
    }

    public void OnValueEntered(Sketch sketch, double value)
    {
        if (_currentState == State.WaitingForEndPoint)
        {
            _lengthConstraint = value;
            
            var finalPoint = ApplyConstraint(_currentMousePosition);
            var line = new LineEntity(_startPoint, finalPoint) { Plane = sketch.Plane };
            sketch.AddEntity(line);
            
            _startPoint = finalPoint;
            _constraint = AxisConstraint.None;
            _lengthConstraint = null;
        }
    }

    private Point3D ApplyConstraint(Point3D point)
    {
        Point3D axisConstrainedPoint = point;

        if (_constraint != AxisConstraint.None)
        {
            axisConstrainedPoint = AxisConstraintHelper.Apply(point, _startPoint, _constraint);
        }

        if (_lengthConstraint.HasValue)
        {
            var vector = axisConstrainedPoint - _startPoint;
            if (vector.Length > 0)
            {
                var direction = vector.Normalize();
                return _startPoint + (direction * _lengthConstraint.Value);
            }
        }

        return axisConstrainedPoint;
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.WaitingForEndPoint)
        {
            yield return new LineEntity(_startPoint, _currentMousePosition) { Plane = _currentPlane ?? AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY };
        }
    }

    public string GetMeasurementText()
    {
        if (_currentState == State.WaitingForEndPoint)
        {
            var distance = (_currentMousePosition - _startPoint).Length;
            return $"Length: {distance:F2}";
        }
        return "";
    }

    public Point3D? GetReferencePoint()
    {
        if (_currentState == State.WaitingForEndPoint)
        {
            return _startPoint;
        }
        return null;
    }
}


