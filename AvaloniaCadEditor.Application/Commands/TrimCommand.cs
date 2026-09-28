using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class TrimCommand : ICommandTool
{
    public event EventHandler<string>? CodeGenerated;
    private readonly IGeometryEngine _geometryEngine;
    public string Name => "Trim";

    public double HitTolerance { get; set; } = 5.0;

    public TrimCommand(IGeometryEngine geometryEngine)
    {
        _geometryEngine = geometryEngine;
    }

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        var hitEntity = _geometryEngine.HitTest(sketch.Entities, point, HitTolerance);
        if (hitEntity == null) return;

        // Find all intersections of this entity with others in the sketch
        var intersections = new List<Point3D>();
        foreach (var other in sketch.Entities)
        {
            if (other == hitEntity) continue;
            intersections.AddRange(_geometryEngine.GetIntersections(hitEntity, other));
        }

        if (intersections.Count == 0)
        {
            // If no intersections, trim removes the whole line
            sketch.RemoveEntity(hitEntity);
            return;
        }

        // We split the entity at all intersections.
        // In a real robust CAD, splitting by multiple points requires ordering them 
        // by parameter along the curve. For this implementation, we assume the geometry engine 
        // handles the multiple splits or we do it sequentially.
        // Let's assume a simplified version where the engine splits an entity into multiple given a list of points.
        // I will update the interface if needed, but for now let's assume we split it at the closest intersection
        // or the engine can split at multiple points.
        
        // As a domain abstraction, let's ask the engine to split the entity given the click point and intersections.
        // The engine knows the topology.
        
        var resultingPieces = _geometryEngine.TrimEntity(hitEntity, intersections, point);
        
        // Wait, SplitEntity currently takes a splitPoint, but typically a trim deletes the segment containing the click point 
        // bounded by intersections. Let's adjust our engine contract to be smarter: `TrimEntity(entity, intersections, clickPoint)`.
        
        sketch.RemoveEntity(hitEntity);
        foreach (var piece in resultingPieces)
        {
            sketch.AddEntity(piece);
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        // Highlight entity to be trimmed could be handled here
    }

    public void Cancel()
    {
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        return Enumerable.Empty<Entity>();
    }

    public void OnKeyDown(Sketch sketch, CadKey key) { }

    public void OnValueEntered(Sketch sketch, double value) { }

    public string GetMeasurementText() => "";
}


