using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Gira as features selecionadas em torno de um eixo (Z por padrão, trocável com as setas), como
/// o transferidor do SketchUp: 3 cliques — centro, referência (define o 0°) e o ângulo final —
/// com o ângulo também digitável no VCB. Só instâncias de peça têm o ângulo gravado no código
/// (ver <c>CadViewModel.RotateTool_FeaturesRotated</c>); outras features selecionadas são ignoradas.
/// </summary>
public class RotateTool : ICommandTool, ITransformPreviewTool, IGuideProvider
{
    // ICommandTool exige o evento, mas o RotateTool não gera snippet de código diretamente
    // (a edição vai pelo Roslyn, via FeaturesRotated) — mesmo padrão do MoveTool.
    public event EventHandler<string>? CodeGenerated;
    public event Action<Point3D, Vector3D, double, IEnumerable<IFeature>>? FeaturesRotated;
    /// <summary>Como <see cref="FeaturesRotated"/>, mas no modo cópia (Ctrl): o original fica onde está.</summary>
    public event Action<Point3D, Vector3D, double, IEnumerable<IFeature>>? FeaturesCopied;
    public string Name => "Rotate";

    private enum State { WaitingForPivot, WaitingForReference, WaitingForAngle }
    private State _currentState = State.WaitingForPivot;

    private Point3D _pivot;
    private Point3D _referenceRaw;
    private Point3D _currentMouseRaw;
    private bool _hasMouse;
    private Vector3D? _axisOverride; // null = normal do plano de trabalho

    private List<IFeature> _selectedFeatures = new();

    /// <summary>Ctrl alterna: gira uma cópia e deixa o original (mesmo comportamento do MoveTool).</summary>
    public bool IsCopyMode { get; private set; }
    public bool IsCopyPreview => IsCopyMode;

    private const double SnapIncrementDegrees = 15.0;
    private const double SnapToleranceDegrees = 2.0;
    private const double GuideRadiusPixels = 60.0;

    /// <summary>Tamanho de um pixel de tela em unidades do mundo no cursor, para o transferidor
    /// manter um tamanho constante na tela (a mesma medida que o CadViewModel já calcula para o
    /// raio de detecção de clique). Atualizado a cada movimento do mouse pelo CadViewModel.</summary>
    public double WorldUnitsPerPixel { get; set; } = 1.0;

    /// <summary>
    /// Plano de trabalho ativo. O CadViewModel entrega os pontos em coordenadas locais desse plano;
    /// a ferramenta converte tudo para o mundo (pivô, eixo e preview são globais, como o Placement).
    /// </summary>
    public WorkPlane Plane { get; set; } = WorkPlane.XY;

    /// <summary>Eixo de rotação: a normal do plano ativo, ou um eixo global escolhido pelas setas.</summary>
    private Vector3D Axis => _axisOverride ?? Plane.Normal;

    /// <summary>Centro do transferidor: o cursor antes do 1º clique, o pivô depois.</summary>
    private Point3D GuideCenter => _currentState == State.WaitingForPivot ? _currentMouseRaw : _pivot;

    public IEnumerable<IFeature> DraggedFeatures =>
        _currentState != State.WaitingForPivot ? _selectedFeatures : Enumerable.Empty<IFeature>();

    public Matrix4x4 PreviewTransform =>
        _currentState == State.WaitingForAngle ? BuildMatrix(_pivot, Axis, CurrentAngleDegrees) : Matrix4x4.Identity;

    /// <summary>Ângulo atual (com imã a cada 15°), calculado a partir da posição do mouse.</summary>
    private double CurrentAngleDegrees
    {
        get
        {
            if (_currentState != State.WaitingForAngle) return 0;
            double raw = SignedAngleBetween(PerpComponent(_referenceRaw), PerpComponent(_currentMouseRaw), Axis);
            return SnapAngle(raw);
        }
    }

    public void OnMouseDown(Sketch sketch, Point3D localPoint)
    {
        var point = Plane.ToWorldSpace(localPoint);
        switch (_currentState)
        {
            case State.WaitingForPivot:
                _pivot = point;
                _currentMouseRaw = point;
                _hasMouse = true;
                _selectedFeatures = sketch.Features.Where(f => f.IsSelected).ToList();
                _currentState = State.WaitingForReference;
                break;

            case State.WaitingForReference:
                if (PerpComponent(point).Length < 1e-6)
                {
                    // Ponto em cima do eixo: não dá para definir o 0° a partir dele, ignora o clique.
                    return;
                }
                _referenceRaw = point;
                _currentMouseRaw = point;
                _currentState = State.WaitingForAngle;
                break;

            case State.WaitingForAngle:
                _currentMouseRaw = point;
                Commit(CurrentAngleDegrees);
                break;
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D localPoint)
    {
        _currentMouseRaw = Plane.ToWorldSpace(localPoint);
        _hasMouse = true;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Control)
        {
            IsCopyMode = !IsCopyMode;
            return;
        }

        var newAxis = key switch
        {
            CadKey.ArrowRight => Vector3D.UnitX,
            CadKey.ArrowLeft => Vector3D.UnitY,
            CadKey.ArrowUp => Vector3D.UnitZ,
            _ => (Vector3D?)null
        };
        if (newAxis.HasValue)
        {
            _axisOverride = newAxis.Value;
        }
    }

    public void OnValueEntered(Sketch sketch, double value)
    {
        if (_currentState == State.WaitingForAngle)
        {
            Commit(value);
        }
    }

    public void Cancel()
    {
        _currentState = State.WaitingForPivot;
        _selectedFeatures = new List<IFeature>();
        _axisOverride = null;
    }

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();

    public string GetMeasurementText() =>
        _currentState == State.WaitingForAngle ? $"Ângulo: {CurrentAngleDegrees:F1}°" : "";

    public string GetStatusText() => (IsCopyMode ? "[Cópia] " : "") + (_currentState switch
    {
        State.WaitingForPivot => "Rotacionar: clique no centro da rotação",
        State.WaitingForReference => "Rotacionar: clique para definir o ângulo 0° (use as setas para trocar o eixo)",
        State.WaitingForAngle => "Rotacionar: clique para confirmar o ângulo, ou digite o valor",
        _ => ""
    });

    // O CadViewModel espera o ponto de referência em coordenadas locais do plano ativo.
    public Point3D? GetReferencePoint() => _currentState != State.WaitingForPivot ? Plane.ToLocalSpace(_pivot) : null;

    /// <summary>
    /// Segmentos do transferidor: círculo + marcas de 15° + indicador do eixo, seguindo o cursor
    /// antes do 1º clique (centro da rotação); depois disso, fixo no pivô, com os raios de
    /// referência/mouse e o arco entre eles.
    /// </summary>
    public IEnumerable<GuideSegment> GetGuideSegments()
    {
        if (!_hasMouse) yield break;

        var center = GuideCenter;
        var (axisR, axisG, axisB) = AxisColor(Axis);
        double radius = GuideRadiusPixels * WorldUnitsPerPixel;

        const int circleSegments = 96;
        for (int i = 0; i < circleSegments; i++)
        {
            var a = CirclePoint(radius, 360.0 * i / circleSegments);
            var b = CirclePoint(radius, 360.0 * (i + 1) / circleSegments);
            yield return new GuideSegment(a, b, axisR, axisG, axisB);
        }

        for (int deg = 0; deg < 360; deg += (int)SnapIncrementDegrees)
        {
            double length = deg % 90 == 0 ? radius * 0.18 : deg % 45 == 0 ? radius * 0.12 : radius * 0.06;
            var inner = CirclePoint(radius - length, deg);
            var outer = CirclePoint(radius + length * 0.15, deg);
            yield return new GuideSegment(inner, outer, axisR, axisG, axisB);
        }

        // Indicador do eixo: uma linha através do centro, na direção do eixo de rotação — mostra
        // em torno de qual eixo vai girar mesmo quando o círculo aparece "de perfil".
        var axisN = Axis.Normalize();
        yield return new GuideSegment(center - axisN * (radius * 0.4), center + axisN * (radius * 0.6), axisR, axisG, axisB);

        if (_currentState == State.WaitingForPivot)
        {
            yield break;
        }

        if (_currentState == State.WaitingForReference)
        {
            yield return new GuideSegment(_pivot, ClampToRadius(_currentMouseRaw, radius), 1f, 1f, 0.2f);
            yield break;
        }

        // Referência (0°) e mouse atual, mais o arco varrido entre eles.
        var refPoint = ClampToRadius(_referenceRaw, radius);
        yield return new GuideSegment(_pivot, refPoint, 1f, 1f, 0.2f);

        double angle = CurrentAngleDegrees;
        var curPoint = CirclePoint(radius, angle);
        yield return new GuideSegment(_pivot, curPoint, 1f, 1f, 0.2f);

        int arcSegments = Math.Max(1, (int)(Math.Abs(angle) / 3.0));
        for (int i = 0; i < arcSegments; i++)
        {
            double a1 = angle * i / arcSegments;
            double a2 = angle * (i + 1) / arcSegments;
            yield return new GuideSegment(CirclePoint(radius, a1), CirclePoint(radius, a2), 0.2f, 1f, 0.2f);
        }
    }

    // ---------------------------------------------------------------- Internos

    private void Commit(double degrees)
    {
        if (IsCopyMode)
            FeaturesCopied?.Invoke(_pivot, Axis, degrees, _selectedFeatures);
        else
            FeaturesRotated?.Invoke(_pivot, Axis, degrees, _selectedFeatures);
        _currentState = State.WaitingForPivot;
        _selectedFeatures = new List<IFeature>();
    }

    /// <summary>Componente de (<paramref name="point"/> - centro do transferidor) perpendicular ao eixo atual.</summary>
    private Vector3D PerpComponent(Point3D point)
    {
        var d = point - GuideCenter;
        var along = Axis * d.DotProduct(Axis);
        return d - along;
    }

    private static double SignedAngleBetween(Vector3D from, Vector3D to, Vector3D axis)
    {
        if (from.Length < 1e-9 || to.Length < 1e-9) return 0;
        var f = from.Normalize();
        var t = to.Normalize();
        double cos = Math.Clamp(f.DotProduct(t), -1.0, 1.0);
        double sin = axis.Normalize().DotProduct(f.CrossProduct(t));
        return Math.Atan2(sin, cos) * 180.0 / Math.PI;
    }

    private static double SnapAngle(double degrees)
    {
        double nearest = Math.Round(degrees / SnapIncrementDegrees) * SnapIncrementDegrees;
        return Math.Abs(nearest - degrees) <= SnapToleranceDegrees ? nearest : degrees;
    }

    private Point3D ClampToRadius(Point3D raw, double radius)
    {
        var v = PerpComponent(raw);
        if (v.Length < 1e-6) return CirclePoint(radius, 0);
        var n = v.Normalize() * radius;
        return GuideCenter + n;
    }

    /// <summary>Ponto no círculo do transferidor a <paramref name="degreesFromReference"/> da referência (0°).</summary>
    private Point3D CirclePoint(double radius, double degreesFromReference)
    {
        // Antes do clique de referência (WaitingForPivot/WaitingForReference), _referenceRaw ainda
        // não tem sentido: usa uma direção fixa em vez de fazer o "0°" depender da distância do
        // cursor até um ponto obsoleto (evita o círculo girando sozinho enquanto o mouse se move).
        var refDir = _currentState == State.WaitingForAngle
            ? PerpComponent(_referenceRaw)
            : ArbitraryPerpendicular(Axis);
        if (refDir.Length < 1e-6) refDir = ArbitraryPerpendicular(Axis);
        refDir = refDir.Normalize();

        var axisN = Axis.Normalize();
        var perp = axisN.CrossProduct(refDir); // 90° adiante de refDir, regra da mão direita em torno do eixo

        double t = degreesFromReference * Math.PI / 180.0;
        var dir = refDir * Math.Cos(t) + perp * Math.Sin(t);
        return GuideCenter + dir * radius;
    }

    private static Vector3D ArbitraryPerpendicular(Vector3D axis)
    {
        var a = axis.Normalize();
        var guess = Math.Abs(a.X) < 0.9 ? Vector3D.UnitX : Vector3D.UnitY;
        return guess.CrossProduct(a).Normalize();
    }

    private static (float R, float G, float B) AxisColor(Vector3D axis)
    {
        if (axis.Equals(Vector3D.UnitX)) return (1f, 0.3f, 0.3f);
        if (axis.Equals(Vector3D.UnitY)) return (0.3f, 1f, 0.3f);
        if (axis.Equals(Vector3D.UnitZ)) return (0.3f, 0.5f, 1f);
        return (0.8f, 0.4f, 1f); // normal de um plano inclinado
    }

    private static Matrix4x4 BuildMatrix(Point3D pivot, Vector3D axis, double degrees)
    {
        var p = new Vector3((float)pivot.X, (float)pivot.Y, (float)pivot.Z);
        var a = new Vector3((float)axis.X, (float)axis.Y, (float)axis.Z);
        if (a.LengthSquared() < 1e-12) a = Vector3.UnitZ;
        a = Vector3.Normalize(a);

        var rotation = Matrix4x4.CreateFromAxisAngle(a, (float)(degrees * Math.PI / 180.0));
        return Matrix4x4.CreateTranslation(-p) * rotation * Matrix4x4.CreateTranslation(p);
    }
}
