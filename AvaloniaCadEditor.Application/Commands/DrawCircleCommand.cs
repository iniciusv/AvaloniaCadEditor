using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class DrawCircleCommand : ICommandTool
{
    public string Name => "Circle";

    private enum State { WaitingForCenter, WaitingForRadius }
    private State _currentState = State.WaitingForCenter;

    private Point3D _center;
    private double _currentRadius;

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        if (_currentState == State.WaitingForCenter)
        {
            _center = point;
            _currentRadius = 0;
            _currentState = State.WaitingForRadius;
        }
        else if (_currentState == State.WaitingForRadius)
        {
            var radius = _center.DistanceTo(point);
            if (radius > 0)
            {
                var circle = new CircleEntity(_center, radius);
                sketch.AddEntity(circle);
            }
            
            _currentState = State.WaitingForCenter;
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        if (_currentState == State.WaitingForRadius)
        {
            _currentRadius = _center.DistanceTo(point);
        }
    }

    public void Cancel()
    {
        _currentState = State.WaitingForCenter;
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.WaitingForRadius && _currentRadius > 0)
        {
            yield return new CircleEntity(_center, _currentRadius);
            // Optionally, return a line from center to mouse cursor
            yield return new LineEntity(_center, new Point3D(_center.X + _currentRadius, _center.Y, _center.Z));
        }
    }
}
