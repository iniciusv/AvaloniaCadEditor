using System.Linq;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class MoveTool : ICommandTool
{
    public string Name => "Move";

    private enum State { WaitingForBasePoint, WaitingForTargetPoint }
    private State _currentState = State.WaitingForBasePoint;

    private Point3D _basePoint;
    private Point3D _currentMouse;
    public bool IsCopyMode { get; private set; } = false;
    
    // Store clones for preview
    private List<Entity> _previewClones = new List<Entity>();
    private List<Entity> _selectedOriginals = new List<Entity>();

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        if (_currentState == State.WaitingForBasePoint)
        {
            _basePoint = point;
            _currentMouse = point;
            
            _selectedOriginals = sketch.Entities.Where(e => e.IsSelected).ToList();
            _previewClones = _selectedOriginals.Select(e => e.Clone()).ToList();
            
            _currentState = State.WaitingForTargetPoint;
        }
        else if (_currentState == State.WaitingForTargetPoint)
        {
            var vector = point - _basePoint;

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
            }

            _currentState = State.WaitingForBasePoint;
            _previewClones.Clear();
            _selectedOriginals.Clear();
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        _currentMouse = point;
    }

    public void Cancel()
    {
        _currentState = State.WaitingForBasePoint;
        _previewClones.Clear();
        _selectedOriginals.Clear();
    }

    public void OnKeyDown(CadKey key)
    {
        if (key == CadKey.Control)
        {
            IsCopyMode = !IsCopyMode;
        }
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.WaitingForTargetPoint)
        {
            var vector = _currentMouse - _basePoint;
            
            // Retorna os clones transladados para dar o preview real-time
            foreach (var clone in _previewClones)
            {
                // Reset to original position (we stored them as exact copies)
                // We shouldn't mutate _previewClones directly in a Get getter to avoid cumulative translation, 
                // so we clone the clone!
                var previewEntity = clone.Clone();
                previewEntity.Translate(vector);
                yield return previewEntity;
            }
            
            // Uma cordinha visual do basepoint ao mouse
            yield return new LineEntity(_basePoint, _currentMouse);
        }
    }
}
