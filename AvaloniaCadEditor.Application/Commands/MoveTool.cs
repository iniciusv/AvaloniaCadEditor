using System.Linq;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class MoveTool : ICommandTool, ITransformPreviewTool
{
    public event System.EventHandler<string>? CodeGenerated;
    public event System.Action<Vector3D, IEnumerable<Domain.Features.IFeature>>? FeaturesMoved;
    /// <summary>Como <see cref="FeaturesMoved"/>, mas no modo cópia (Ctrl): o original fica onde está.</summary>
    public event System.Action<Vector3D, IEnumerable<Domain.Features.IFeature>>? FeaturesCopied;
    public string Name => "Move";

    private enum State { WaitingForBasePoint, WaitingForTargetPoint }
    private State _currentState = State.WaitingForBasePoint;

    private Point3D _basePoint;
    private Point3D _currentMouse;
    public bool IsCopyMode { get; private set; } = false;
    public bool IsCopyPreview => IsCopyMode;
    
    private List<Entity> _previewClones = new List<Entity>();
    private List<Entity> _selectedOriginals = new List<Entity>();
    private List<Domain.Features.IFeature> _selectedFeatures = new();
    
    private AxisConstraint _constraint = AxisConstraint.None;

    public IEnumerable<Domain.Features.IFeature> DraggedFeatures => _currentState == State.WaitingForTargetPoint ? _selectedFeatures : Enumerable.Empty<Domain.Features.IFeature>();
    public Vector3D CurrentTranslation => _currentState == State.WaitingForTargetPoint ? _currentMouse - _basePoint : new Vector3D(0, 0, 0);

    /// <summary>
    /// Plano de trabalho ativo. Os pontos chegam em coordenadas locais desse plano: as entidades do
    /// sketch (que vivem no plano) são transladadas no local, mas o deslocamento das features 3D
    /// (FeaturesMoved e o preview) é convertido para o mundo.
    /// </summary>
    public WorkPlane Plane { get; set; } = WorkPlane.XY;

    /// <summary>
    /// O que arrastar quando não é a seleção do sketch: com o grupo de uma peça aberto, o sólido do
    /// <c>Build()</c> realçado, que não está no sketch (ver <c>CadViewModel</c>). Consultado no 1º clique;
    /// null, ou devolvendo null, usa as entidades e features selecionadas.
    /// </summary>
    public System.Func<IReadOnlyList<Domain.Features.IFeature>?>? SelectionOverride { get; set; }

    private Vector3D ToWorld(Vector3D v) => Plane.XAxis * v.X + Plane.YAxis * v.Y + Plane.Normal * v.Z;

    public System.Numerics.Matrix4x4 PreviewTransform
    {
        get
        {
            var t = ToWorld(CurrentTranslation);
            return System.Numerics.Matrix4x4.CreateTranslation((float)t.X, (float)t.Y, (float)t.Z);
        }
    }

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        if (_currentState == State.WaitingForBasePoint)
        {
            _basePoint = point;
            _currentMouse = point;
            
            var overridden = SelectionOverride?.Invoke();
            _selectedOriginals = overridden != null ? new List<Entity>() : sketch.Entities.Where(e => e.IsSelected).ToList();
            _previewClones = _selectedOriginals.Select(e => e.Clone()).ToList();

            _selectedFeatures = overridden?.ToList() ?? sketch.Features.Where(f => f.IsSelected).ToList();
            
            _currentState = State.WaitingForTargetPoint;
        }
        else if (_currentState == State.WaitingForTargetPoint)
        {
            var constrainedPoint = AxisConstraintHelper.Apply(point, _basePoint, _constraint);
            var vector = constrainedPoint - _basePoint;
            ApplyMove(sketch, vector);
        }
    }

    private void ApplyMove(Sketch sketch, Vector3D vector)
    {
        if (IsCopyMode)
        {
            foreach (var entity in _selectedOriginals)
            {
                var clone = entity.Clone();
                clone.Translate(vector);
                sketch.AddEntity(clone);
            }

            if (_selectedFeatures.Any())
            {
                FeaturesCopied?.Invoke(ToWorld(vector), _selectedFeatures);
            }
        }
        else
        {
            foreach (var entity in _selectedOriginals)
            {
                entity.Translate(vector);
            }
            
            if (_selectedFeatures.Any())
            {
                FeaturesMoved?.Invoke(ToWorld(vector), _selectedFeatures);
            }
        }

        _currentState = State.WaitingForBasePoint;
        _previewClones.Clear();
        _selectedOriginals.Clear();
        _selectedFeatures.Clear();
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        if (_currentState == State.WaitingForTargetPoint)
        {
            _currentMouse = AxisConstraintHelper.Apply(point, _basePoint, _constraint);
        }
        else
        {
            _currentMouse = point;
        }
    }

    public void Cancel()
    {
        _currentState = State.WaitingForBasePoint;
        _previewClones.Clear();
        _selectedOriginals.Clear();
        _selectedFeatures.Clear();
        _constraint = AxisConstraint.None;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Control)
        {
            IsCopyMode = !IsCopyMode;
        }
        else if (_currentState == State.WaitingForTargetPoint)
        {
            _constraint = AxisConstraintHelper.ToggleConstraint(key, _constraint);
            _currentMouse = AxisConstraintHelper.Apply(_currentMouse, _basePoint, _constraint);
        }
    }

    public void OnValueEntered(Sketch sketch, double value)
    {
        if (_currentState == State.WaitingForTargetPoint)
        {
            var constrainedPoint = AxisConstraintHelper.Apply(_currentMouse, _basePoint, _constraint);
            var currentVector = constrainedPoint - _basePoint;
            if (currentVector.Length > 0)
            {
                var direction = currentVector.Normalize();
                var vector = direction * value;
                ApplyMove(sketch, vector);
            }
        }
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        if (_currentState == State.WaitingForTargetPoint)
        {
            var vector = _currentMouse - _basePoint;
            
            foreach (var clone in _previewClones)
            {
                var previewEntity = clone.Clone();
                previewEntity.Translate(vector);
                yield return previewEntity;
            }
            
            yield return new LineEntity(_basePoint, _currentMouse);
        }
    }

    public string GetMeasurementText()
    {
        if (_currentState == State.WaitingForTargetPoint)
        {
            var distance = (_currentMouse - _basePoint).Length;
            return $"Distance: {distance:F2}";
        }
        return "";
    }
}


