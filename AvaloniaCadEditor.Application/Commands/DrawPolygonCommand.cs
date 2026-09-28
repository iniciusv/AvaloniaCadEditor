using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Ferramenta Polygon: desenha um polígono regular (inscrito num círculo) como um conjunto
/// de LineEntity fechado. O primeiro clique define o centro; o segundo define raio e rotação
/// (o polígono gira para acompanhar o cursor). O número de lados pode ser alterado digitando
/// um valor antes do segundo clique (padrão: 6).
/// </summary>
public class DrawPolygonCommand : ICommandTool
{
    public event EventHandler<string>? CodeGenerated;
    public string Name => "Polygon";

    private enum State { WaitingForCenter, WaitingForRadius }
    private WorkPlane? _currentPlane;
    private State _currentState = State.WaitingForCenter;

    private int _sides = 6;
    private Point3D _center;
    private double _currentRadius;
    private double _currentAngle;

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        _currentPlane = sketch.Plane;
        if (_currentState == State.WaitingForCenter)
        {
            _center = point;
            _currentRadius = 0;
            _currentState = State.WaitingForRadius;
        }
        else if (_currentState == State.WaitingForRadius)
        {
            CommitPolygon(sketch, point);
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        _currentPlane = sketch.Plane;
        if (_currentState == State.WaitingForRadius)
        {
            UpdateFromPoint(point);
        }
    }

    public void Cancel()
    {
        _currentState = State.WaitingForCenter;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape)
        {
            Cancel();
        }
    }

    public void OnValueEntered(Sketch sketch, double value)
    {
        if (_currentState == State.WaitingForCenter)
        {
            // Antes de definir o centro, um valor digitado ajusta o número de lados.
            _sides = (int)Math.Clamp(Math.Round(value), RegularPolygonGeometry.MinSides, RegularPolygonGeometry.MaxSides);
        }
        else if (_currentState == State.WaitingForRadius && value > 0)
        {
            CommitPolygon(sketch, radiusOverride: value);
        }
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.WaitingForRadius && _currentRadius > 0)
        {
            var plane = _currentPlane ?? WorkPlane.XY;
            var vertices = RegularPolygonGeometry.GetVertices(_center, _currentRadius, _sides, _currentAngle);
            for (int i = 0; i < vertices.Count; i++)
            {
                yield return new LineEntity(vertices[i], vertices[(i + 1) % vertices.Count]) { Plane = plane };
            }
        }
    }

    public string GetMeasurementText()
    {
        if (_currentState == State.WaitingForRadius)
        {
            return $"Lados: {_sides}  Raio: {_currentRadius:F2}";
        }
        return $"Lados: {_sides} (digite um número para mudar)";
    }

    private void UpdateFromPoint(Point3D point)
    {
        var vector = point - _center;
        _currentRadius = vector.Length;
        if (_currentRadius > 1e-6)
        {
            _currentAngle = Math.Atan2(vector.Y, vector.X);
        }
    }

    private void CommitPolygon(Sketch sketch, Point3D? mousePoint = null, double? radiusOverride = null)
    {
        if (mousePoint.HasValue)
        {
            UpdateFromPoint(mousePoint.Value);
        }

        double radius = radiusOverride ?? _currentRadius;
        if (radius > 0)
        {
            var plane = sketch.Plane;
            var vertices = RegularPolygonGeometry.GetVertices(_center, radius, _sides, _currentAngle);
            for (int i = 0; i < vertices.Count; i++)
            {
                var line = new LineEntity(vertices[i], vertices[(i + 1) % vertices.Count]) { Plane = plane };
                sketch.AddEntity(line);
            }
        }

        _currentState = State.WaitingForCenter;
        _currentRadius = 0;
    }
}
