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

    private Point3D _startPoint;
    private Point3D _currentMousePosition;

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        if (_currentState == State.WaitingForStartPoint)
        {
            _startPoint = point;
            _currentMousePosition = point;
            _constraint = AxisConstraint.None; // Reset constraint
            _currentState = State.WaitingForEndPoint;
        }
        else if (_currentState == State.WaitingForEndPoint)
        {
            var finalPoint = ApplyConstraint(point);
            var line = new LineEntity(_startPoint, finalPoint);
            sketch.AddEntity(line);
            
            // Loop back to start to draw contiguous lines
            _startPoint = finalPoint;
            _constraint = AxisConstraint.None; // Reset for next line
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
    }

    public void OnKeyDown(CadKey key)
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
        }
    }

    private Point3D ApplyConstraint(Point3D point)
    {
        if (_constraint == AxisConstraint.None)
            return point;

        return _constraint switch
        {
            AxisConstraint.X => new Point3D(point.X, _startPoint.Y, _startPoint.Z),
            AxisConstraint.Y => new Point3D(_startPoint.X, point.Y, _startPoint.Z),
            AxisConstraint.Z => new Point3D(_startPoint.X, _startPoint.Y, point.Z),
            _ => point
        };
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.WaitingForEndPoint)
        {
            yield return new LineEntity(_startPoint, _currentMousePosition);
        }
    }
}
