using System.Linq;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Domain.Interfaces;

namespace AvaloniaCadEditor.Application.Commands;

public class SelectTool : ICommandTool
{
    private readonly IGeometryEngine _geometryEngine;

    public SelectTool(IGeometryEngine geometryEngine)
    {
        _geometryEngine = geometryEngine;
    }

    public string Name => "Select";

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        // First, clear all selections unless we want to implement multi-select with modifiers later
        foreach (var entity in sketch.Entities)
        {
            entity.IsSelected = false;
        }

        // Perform HitTest with a tolerance of 5.0 (can be adjusted)
        var hitEntity = _geometryEngine.HitTest(sketch.Entities, point, 5.0);

        if (hitEntity != null)
        {
            hitEntity.IsSelected = true;
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point) { }

    public void Cancel() { }

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();

    public void OnKeyDown(CadKey key) { }
}
