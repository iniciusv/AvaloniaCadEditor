using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class DrawLineCommand : ICommandTool
{
    public string Name => "Line";

    private enum State { WaitingForStartPoint, WaitingForEndPoint }
    private State _currentState = State.WaitingForStartPoint;

    private enum AxisConstraint { None, X, Y, Z }
    private AxisConstraint _constraint = AxisConstraint.None;

    private double? _lengthConstraint = null;

    private Point3D _startPoint;
    private Point3D _currentMousePosition;

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
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
            var line = new LineEntity(_startPoint, finalPoint);
            sketch.AddEntity(line);
            
            // Loop back to start to draw contiguous lines
            _startPoint = finalPoint;
            _constraint = AxisConstraint.None;
            _lengthConstraint = null;
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
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
            AxisConstraint newConstraint = _constraint;

            switch (key)
            {
                case CadKey.ArrowRight:
                    newConstraint = AxisConstraint.X;
                    break;
                case CadKey.ArrowLeft:
                    newConstraint = AxisConstraint.Y;
                    break;
                case CadKey.ArrowUp:
                    newConstraint = AxisConstraint.Z;
                    break;
            }

            // Toggle off if same key pressed
            if (_constraint == newConstraint)
                _constraint = AxisConstraint.None;
            else
                _constraint = newConstraint;
                
            // Reapply constraint to mouse position
            _currentMousePosition = ApplyConstraint(_currentMousePosition);
        }
    }

    public void OnValueEntered(Sketch sketch, double value)
    {
        if (_currentState == State.WaitingForEndPoint)
        {
            _lengthConstraint = value;
            
            var finalPoint = ApplyConstraint(_currentMousePosition);
            var line = new LineEntity(_startPoint, finalPoint);
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
            axisConstrainedPoint = _constraint switch
            {
                AxisConstraint.X => new Point3D(point.X, _startPoint.Y, _startPoint.Z),
                AxisConstraint.Y => new Point3D(_startPoint.X, point.Y, _startPoint.Z),
                AxisConstraint.Z => new Point3D(_startPoint.X, _startPoint.Y, point.Z),
                _ => point
            };
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
            yield return new LineEntity(_startPoint, _currentMousePosition);
        }
    }
}
