using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class FilletCommand : ICommandTool
{
    private readonly IGeometryEngine _geometryEngine;
    
    public string Name => "Fillet";
    public double Radius { get; set; } = 10.0;

    private enum State { WaitingForFirst, WaitingForSecond }
    private State _currentState = State.WaitingForFirst;

    private Entity? _firstEntity;
    private Point3D _firstClick;

    public FilletCommand(IGeometryEngine geometryEngine)
    {
        _geometryEngine = geometryEngine;
    }

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        double hitTolerance = 5.0;
        var hitEntity = _geometryEngine.HitTest(sketch.Entities, point, hitTolerance);
        
        if (hitEntity == null) return;

        if (_currentState == State.WaitingForFirst)
        {
            _firstEntity = hitEntity;
            _firstClick = point;
            _currentState = State.WaitingForSecond;
        }
        else if (_currentState == State.WaitingForSecond)
        {
            if (hitEntity == _firstEntity)
            {
                // Can't fillet a line with itself
                Cancel();
                return;
            }

            var resultingEntities = _geometryEngine.FilletEntities(_firstEntity!, _firstClick, hitEntity, point, Radius);
            
            if (resultingEntities.Any())
            {
                sketch.RemoveEntity(_firstEntity!);
                sketch.RemoveEntity(hitEntity);
                
                foreach (var newEntity in resultingEntities)
                {
                    sketch.AddEntity(newEntity);
                }
            }

            Cancel(); // Reset state
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        // Highlight logic
    }

    public void Cancel()
    {
        _currentState = State.WaitingForFirst;
        _firstEntity = null;
    }

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();
}
