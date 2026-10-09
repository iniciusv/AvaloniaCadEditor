using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Ferramenta Extend: prolonga a ponta clicada de uma linha até a primeira entidade encontrada
/// naquela direção. Se nenhuma fronteira for encontrada, a linha fica "pendente" e um segundo
/// clique em outra linha forma um canto no ponto onde as retas suportadas por ambas se cruzam.
/// </summary>
public class ExtendCommand : ICommandTool, IHitTolerantTool
{
    public event EventHandler<string>? CodeGenerated;
    private readonly IGeometryEngine _geometryEngine;
    public double HitTolerance { get; set; } = 5.0;

    public string Name => "Extend";

    private LineEntity? _pendingLine;
    private Entity? _preview;

    public ExtendCommand(IGeometryEngine geometryEngine)
    {
        _geometryEngine = geometryEngine;
    }

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        var hitEntity = _geometryEngine.HitTest(sketch.Entities, point, HitTolerance);

        if (hitEntity is not LineEntity hitLine)
        {
            ClearPending();
            return;
        }

        if (_pendingLine == null)
        {
            var boundaries = sketch.Entities.Where(e => e != hitLine);
            var extended = _geometryEngine.ExtendEntity(hitLine, boundaries, point);

            if (extended != null)
            {
                sketch.RemoveEntity(hitLine);
                sketch.AddEntity(extended);
                return;
            }

            // Nada encontrado na direção: marca como pendente à espera de uma segunda linha (canto).
            hitLine.IsSelected = true;
            _pendingLine = hitLine;
            return;
        }

        if (hitLine == _pendingLine)
        {
            ClearPending();
            return;
        }

        var corner = _geometryEngine.GetApparentIntersection(_pendingLine, hitLine);
        if (corner != null)
        {
            sketch.RemoveEntity(_pendingLine);
            sketch.AddEntity(SnapClosestEndpoint(_pendingLine, corner.Value));

            sketch.RemoveEntity(hitLine);
            sketch.AddEntity(SnapClosestEndpoint(hitLine, corner.Value));
        }

        ClearPending();
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        _preview = null;

        var hitEntity = _geometryEngine.HitTest(sketch.Entities, point, HitTolerance);
        if (hitEntity is not LineEntity hitLine || hitLine == _pendingLine) return;

        if (_pendingLine == null)
        {
            var boundaries = sketch.Entities.Where(e => e != hitLine);
            _preview = _geometryEngine.ExtendEntity(hitLine, boundaries, point);
        }
        else
        {
            var corner = _geometryEngine.GetApparentIntersection(_pendingLine, hitLine);
            if (corner != null)
            {
                _preview = SnapClosestEndpoint(_pendingLine, corner.Value);
            }
        }
    }

    public void Cancel()
    {
        ClearPending();
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_preview != null) yield return _preview;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape)
        {
            ClearPending();
        }
    }

    public void OnValueEntered(Sketch sketch, double value) { }

    public string GetMeasurementText() => _pendingLine != null ? "Selecione a 2ª linha para formar o canto" : "";

    private void ClearPending()
    {
        if (_pendingLine != null)
        {
            _pendingLine.IsSelected = false;
        }
        _pendingLine = null;
        _preview = null;
    }

    /// <summary>
    /// Retorna uma nova LineEntity com a extremidade mais próxima de 'target' movida para 'target'.
    /// </summary>
    private static LineEntity SnapClosestEndpoint(LineEntity line, Point3D target)
    {
        bool moveStart = target.DistanceTo(line.StartPoint) < target.DistanceTo(line.EndPoint);
        var newLine = moveStart
            ? new LineEntity(target, line.EndPoint)
            : new LineEntity(line.StartPoint, target);
        newLine.Plane = line.Plane;
        return newLine;
    }
}
