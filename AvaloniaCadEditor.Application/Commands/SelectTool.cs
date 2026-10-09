using System.Linq;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Domain.Interfaces;

namespace AvaloniaCadEditor.Application.Commands;

public class SelectTool : ICommandTool, IHitTolerantTool
{
    public event EventHandler<string>? CodeGenerated;
    private readonly IGeometryEngine _geometryEngine;
    private readonly Services.ISelectionService? _selectionService;

    public double HitTolerance { get; set; } = 0.5;

    public SelectTool(IGeometryEngine geometryEngine, Services.ISelectionService? selectionService = null)
    {
        _geometryEngine = geometryEngine;
        _selectionService = selectionService;
    }

    public string Name => "Select";

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        // 1. Try 2D HitTest on entities
        var hitEntity = _geometryEngine.HitTest(sketch.Entities.Where(e => e.IsVisible), point, HitTolerance);

        if (hitEntity != null)
        {
            if (_selectionService != null)
            {
                _selectionService.SelectById(sketch, hitEntity.Id);
            }
            else
            {
                foreach (var e in sketch.Entities) e.IsSelected = false;
                hitEntity.IsSelected = true;
            }
            return;
        }

        // 2. Try 3D Features (e.g. Cylinders / Extrusions) HitTest
        foreach (var feature in sketch.Features)
        {
            if (!feature.IsVisible) continue; // Ignore hidden features
            if (feature is Domain.Features.ExtrudeFeature ef)
            {
                // Check if point is inside profile boundary or near base
                if (ef.Profile is Domain.Profiles.CircleProfile cp)
                {
                    double dist = Math.Sqrt(Math.Pow(point.X - cp.Center.X, 2) + Math.Pow(point.Y - cp.Center.Y, 2));
                    if (dist <= cp.Radius + 3.0)
                    {
                        if (_selectionService != null)
                        {
                            _selectionService.SelectById(sketch, ef.Id);
                        }
                        else
                        {
                            ef.IsSelected = true;
                        }
                        return;
                    }
                }
            }
        }

        // 3. Clear selection if clicked in empty space
        if (_selectionService != null)
        {
            _selectionService.ClearSelection(sketch);
        }
        else
        {
            foreach (var e in sketch.Entities) e.IsSelected = false;
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point) { }

    public void Cancel() { }

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();

    public void OnKeyDown(Sketch sketch, CadKey key) { }

    public void OnValueEntered(Sketch sketch, double value) { }

    public string GetMeasurementText() => "";
}


