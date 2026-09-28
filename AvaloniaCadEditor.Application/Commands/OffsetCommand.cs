using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Ferramenta Offset: cria um contorno paralelo de uma região (perfil fechado) selecionada.
/// Mover o mouse para fora da região produz um offset positivo (expande); para dentro,
/// negativo (contrai). A região original é preservada.
/// </summary>
public class OffsetCommand : ICommandTool
{
    public event EventHandler<string>? CodeGenerated;
    private readonly IGeometryEngine _geometryEngine;

    public string Name => "Offset";

    private enum State { SelectingRegion, DefiningDistance }
    private State _currentState = State.SelectingRegion;

    private IProfile? _selectedRegion;
    private double _currentDistance;

    public OffsetCommand(IGeometryEngine geometryEngine)
    {
        _geometryEngine = geometryEngine;
    }

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        if (_currentState == State.SelectingRegion)
        {
            _selectedRegion = FindRegionAtPoint(sketch, point);
            if (_selectedRegion != null)
            {
                _currentDistance = 0;
                _currentState = State.DefiningDistance;
            }
        }
        else if (_currentState == State.DefiningDistance)
        {
            CommitOffset(sketch);
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        if (_currentState == State.DefiningDistance && _selectedRegion != null)
        {
            _currentDistance = SignedDistanceToBoundary(_selectedRegion, point);
        }
    }

    public void Cancel()
    {
        _currentState = State.SelectingRegion;
        _selectedRegion = null;
        _currentDistance = 0;
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
        if (_currentState == State.DefiningDistance && _selectedRegion != null)
        {
            // O sinal do valor digitado segue o lado em que o cursor está atualmente.
            double sign = _currentDistance >= 0 ? 1.0 : -1.0;
            _currentDistance = Math.Abs(value) * sign;
            CommitOffset(sketch);
        }
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.DefiningDistance && _selectedRegion != null && Math.Abs(_currentDistance) > 1e-6)
        {
            foreach (var entity in _geometryEngine.OffsetProfile(_selectedRegion, _currentDistance))
            {
                yield return entity;
            }
        }
    }

    public string GetMeasurementText()
    {
        if (_currentState == State.DefiningDistance)
        {
            string side = _currentDistance >= 0 ? "fora" : "dentro";
            return $"Offset: {Math.Abs(_currentDistance):F2} ({side})";
        }
        return "Clique numa região";
    }

    private void CommitOffset(Sketch sketch)
    {
        if (_selectedRegion != null && Math.Abs(_currentDistance) > 1e-6)
        {
            var offsetEntities = _geometryEngine.OffsetProfile(_selectedRegion, _currentDistance).ToList();
            foreach (var entity in offsetEntities)
            {
                sketch.AddEntity(entity);
            }
        }
        Cancel();
    }

    private static IProfile? FindRegionAtPoint(Sketch sketch, Point3D point)
    {
        return sketch.Profiles
            .Where(p => p.IsVisible && p is not FaceProfile && p.ContainsPoint(point))
            .OrderBy(p => p.Area)
            .FirstOrDefault();
    }

    /// <summary>
    /// Distância do ponto até a borda da região, positiva se o ponto estiver fora, negativa se dentro.
    /// </summary>
    private static double SignedDistanceToBoundary(IProfile profile, Point3D point)
    {
        double distance;
        if (profile is CircleProfile circle)
        {
            distance = Math.Abs(point.DistanceTo(circle.Center) - circle.Radius);
        }
        else
        {
            var localVerts = profile.GetVertices().Select(profile.Plane.ToLocalSpace).ToList();
            distance = double.MaxValue;
            for (int i = 0; i < localVerts.Count; i++)
            {
                var a = localVerts[i];
                var b = localVerts[(i + 1) % localVerts.Count];
                distance = Math.Min(distance, DistancePointToSegment(point, a, b));
            }
        }

        return profile.ContainsPoint(point) ? -distance : distance;
    }

    private static double DistancePointToSegment(Point3D p, Point3D a, Point3D b)
    {
        var v = b - a;
        var w = p - a;

        double l2 = v.X * v.X + v.Y * v.Y;
        if (l2 < 1e-10) return p.DistanceTo(a);

        double t = (w.X * v.X + w.Y * v.Y) / l2;
        t = Math.Max(0, Math.Min(1, t));

        var proj = new Point3D(a.X + t * v.X, a.Y + t * v.Y, 0);
        return p.DistanceTo(proj);
    }
}
