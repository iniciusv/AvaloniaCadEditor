using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class BooleanTool : ICommandTool
{
    private readonly BooleanOperationType _operationType;
    private readonly ISelectionService _selectionService;

    public string Name => $"{_operationType}";

    private enum State { SelectingBase, SelectingTool }
    private State _currentState = State.SelectingBase;

    private IFeature? _baseFeature;
    private IFeature? _toolFeature;

    public BooleanTool(BooleanOperationType operationType, ISelectionService selectionService)
    {
        _operationType = operationType;
        _selectionService = selectionService;
        _selectionService.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        // Allow the user to select via the ModelTree if the 3D hit test misses
        if (_selectionService.CurrentSelectionType == SelectionType.Feature && _selectionService.SelectedObject is IFeature feature)
        {
            if (feature is WorkPlaneFeature) return;

            if (_currentState == State.SelectingBase)
            {
                _baseFeature = feature;
                _currentState = State.SelectingTool;
            }
            else if (_currentState == State.SelectingTool && feature != _baseFeature)
            {
                _toolFeature = feature;
            }
        }
    }

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        var hitFeature = FindFeatureNear(sketch, point);
        if (hitFeature is WorkPlaneFeature) return;

        if (_currentState == State.SelectingBase)
        {
            if (hitFeature != null)
            {
                _baseFeature = hitFeature;
                _selectionService.SelectFeature(_baseFeature);
                _currentState = State.SelectingTool;
            }
        }
        else if (_currentState == State.SelectingTool)
        {
            if (hitFeature != null && hitFeature != _baseFeature)
            {
                _toolFeature = hitFeature;
                _selectionService.SelectFeature(_toolFeature);
                Execute(sketch);
            }
        }
    }

    private void Execute(Sketch sketch)
    {
        if (_baseFeature != null && _toolFeature != null)
        {
            string name = $"{_operationType} ({_baseFeature.Name}, {_toolFeature.Name})";
            var booleanFeature = new BooleanFeature(name, _operationType, _baseFeature, new[] { _toolFeature });
            sketch.AddFeature(booleanFeature);
            
            // Re-select the new boolean feature
            _selectionService.SelectFeature(booleanFeature);
            Cancel();
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point) { }

    public void Cancel()
    {
        _currentState = State.SelectingBase;
        _baseFeature = null;
        _toolFeature = null;
    }

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape) Cancel();
    }

    public void OnValueEntered(Sketch sketch, double value) { }

    public string GetMeasurementText() => "";

    public string GetStatusText()
    {
        string opName = _operationType == BooleanOperationType.Subtract ? "Subtrair (Corte)" : "Unir";
        return _currentState switch
        {
            State.SelectingBase => $"Operação {opName}: 1/2 Selecione o Volume BASE",
            State.SelectingTool => $"Operação {opName}: 2/2 Selecione o Volume FERRAMENTA para {_operationType} de {_baseFeature?.Name}",
            _ => ""
        };
    }

    private IFeature? FindFeatureNear(Sketch sketch, Point3D point)
    {
        // Simple heuristic: find visible feature whose center/origin is nearest
        return sketch.Features
            .Where(f => f.IsVisible)
            .OrderBy(f => GetFeatureCenter(f).DistanceTo(point))
            .FirstOrDefault();
    }

    private Point3D GetFeatureCenter(IFeature feature)
    {
        if (feature is ExtrudeFeature extrude) return extrude.Centroid;
        return Point3D.Origin;
    }
}
