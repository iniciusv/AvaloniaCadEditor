using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class DrawLineCommand : ICommandTool
{
    public string Name => "Line";

    private enum State { WaitingForStartPoint, WaitingForEndPoint }
    private State _currentState = State.WaitingForStartPoint;

    private Point3D _startPoint;
    private Point3D _currentMousePosition;

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        if (_currentState == State.WaitingForStartPoint)
        {
            _startPoint = point;
            _currentMousePosition = point;
            _currentState = State.WaitingForEndPoint;
        }
        else if (_currentState == State.WaitingForEndPoint)
        {
            var line = new LineEntity(_startPoint, point);
            sketch.AddEntity(line);
            
            // Loop back to start to draw contiguous lines or standalone lines
            _startPoint = point;
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        _currentMousePosition = point;
    }

    public void Cancel()
    {
        _currentState = State.WaitingForStartPoint;
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.WaitingForEndPoint)
        {
            yield return new LineEntity(_startPoint, _currentMousePosition);
        }
    }
}
