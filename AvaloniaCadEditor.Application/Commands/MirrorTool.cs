using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Espelha as features recebidas por um plano escolhido com o mouse: o plano passa pelo ponto sob o
/// cursor (com osnap, inclusive vértices do sólido) e tem a normal de um eixo global — X por padrão,
/// trocável pelas setas como no <see cref="RotateTool"/>. Enquanto o mouse anda, o plano é desenhado
/// sobre a seleção e o resultado aparece como preview; um clique confirma. A edição do código fica com
/// <c>CadViewModel</c> (via <see cref="FeaturesMirrored"/>/<see cref="FeaturesCopied"/>), como no Mover/Girar.
/// </summary>
public class MirrorTool : ICommandTool, ITransformPreviewTool, IGuideProvider
{
    // ICommandTool exige o evento, mas a edição vai pelo Roslyn (mesmo padrão do RotateTool).
    public event EventHandler<string>? CodeGenerated;
    public event Action<Point3D, Vector3D, IEnumerable<IFeature>>? FeaturesMirrored;
    /// <summary>Como <see cref="FeaturesMirrored"/>, mas no modo cópia: o original fica onde está.</summary>
    public event Action<Point3D, Vector3D, IEnumerable<IFeature>>? FeaturesCopied;
    public string Name => "Mirror";

    private readonly IReadOnlyList<IFeature> _features;
    private readonly Point3D _boundsMin;
    private readonly Point3D _boundsMax;
    private Vector3D _normal = Vector3D.UnitX;
    private Point3D _pointOnPlane;
    private bool _hasMouse;

    /// <param name="features">O que espelhar (a seleção no momento em que a ferramenta foi aberta).</param>
    /// <param name="copy">Começa no modo cópia; Ctrl alterna.</param>
    public MirrorTool(IReadOnlyList<IFeature> features, bool copy)
    {
        _features = features;
        IsCopyMode = copy;
        (_boundsMin, _boundsMax) = Bounds(features);
        _pointOnPlane = Center(_boundsMin, _boundsMax);
    }

    public bool IsCopyMode { get; private set; }
    public bool IsCopyPreview => IsCopyMode;

    /// <summary>Plano de trabalho ativo: o CadViewModel entrega os pontos em coordenadas locais dele.</summary>
    public WorkPlane Plane { get; set; } = WorkPlane.XY;

    public IEnumerable<IFeature> DraggedFeatures => _hasMouse ? _features : Enumerable.Empty<IFeature>();

    public Matrix4x4 PreviewTransform => _hasMouse ? ToMatrix(Placement.Mirror(_pointOnPlane, _normal)) : Matrix4x4.Identity;

    /// <summary>Centro da caixa envolvente das features — o plano do "espelhar pelo centro da seleção".</summary>
    public static Point3D SelectionCenter(IEnumerable<IFeature> features)
    {
        var (min, max) = Bounds(features);
        return Center(min, max);
    }

    public void OnMouseDown(Sketch sketch, Point3D localPoint)
    {
        // O ponto do clique chega achatado no plano de trabalho; o do último movimento mantém a
        // profundidade do osnap (vértice do sólido). Usa este, que é o mesmo que o preview mostrou.
        if (!_hasMouse) OnMouseMove(sketch, localPoint);
        Commit();
    }

    public void OnMouseMove(Sketch sketch, Point3D localPoint)
    {
        _pointOnPlane = Plane.ToWorldSpace(localPoint);
        _hasMouse = true;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        switch (key)
        {
            case CadKey.Control: IsCopyMode = !IsCopyMode; break;
            case CadKey.ArrowRight: _normal = Vector3D.UnitX; break;
            case CadKey.ArrowLeft: _normal = Vector3D.UnitY; break;
            case CadKey.ArrowUp: _normal = Vector3D.UnitZ; break;
            case CadKey.Enter when _hasMouse: Commit(); break;
        }
    }

    /// <summary>Valor digitado: a posição do plano no eixo atual (ex.: 50 com X = plano X = 50).</summary>
    public void OnValueEntered(Sketch sketch, double value)
    {
        _pointOnPlane = new Point3D(
            _normal.X != 0 ? value : _pointOnPlane.X,
            _normal.Y != 0 ? value : _pointOnPlane.Y,
            _normal.Z != 0 ? value : _pointOnPlane.Z);
        _hasMouse = true;
        Commit();
    }

    public void Cancel() => _hasMouse = false;

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();

    public string GetMeasurementText() =>
        _hasMouse ? FormattableString.Invariant($"{AxisName} = {PlaneCoordinate:0.###}") : "";

    public string GetStatusText() =>
        (IsCopyMode ? "[Cópia] " : "") +
        $"Espelhar em {AxisName}: clique no ponto por onde passa o plano, ou digite {AxisName} " +
        "(setas → X, ← Y, ↑ Z; Ctrl alterna cópia; Esc cancela)";

    public Point3D? GetReferencePoint() => null;

    /// <summary>
    /// Auxílios visuais, na cor do eixo: o plano do espelho como um retângulo com grade, cobrindo a
    /// seleção e o resultado, a normal no ponto do cursor, e — em amarelo — as linhas que ligam o
    /// centro da seleção ao centro espelhado, cruzando o plano em ângulo reto.
    /// </summary>
    public IEnumerable<GuideSegment> GetGuideSegments()
    {
        if (!_hasMouse) yield break;

        var (r, g, b) = AxisColor(_normal);
        var mirror = Placement.Mirror(_pointOnPlane, _normal);
        var (u, v) = InPlaneAxes(_normal);

        // Extensão do retângulo: a seleção e o espelho dela projetados no plano, com folga.
        var corners = Corners(_boundsMin, _boundsMax).SelectMany(c => new[] { c, mirror.Apply(c) }).Append(_pointOnPlane).ToList();
        double uMin = corners.Min(c => Coord(c, u)), uMax = corners.Max(c => Coord(c, u));
        double vMin = corners.Min(c => Coord(c, v)), vMax = corners.Max(c => Coord(c, v));
        double margin = Math.Max(Math.Max(uMax - uMin, vMax - vMin) * 0.1, 1.0);
        uMin -= margin; uMax += margin; vMin -= margin; vMax += margin;

        double depth = _pointOnPlane.X * _normal.X + _pointOnPlane.Y * _normal.Y + _pointOnPlane.Z * _normal.Z;
        Point3D At(double a, double c) => PointFrom(u * a + v * c + _normal * depth);

        // Contorno e grade (5 divisões em cada direção).
        const int divisions = 5;
        for (int i = 0; i <= divisions; i++)
        {
            double a = uMin + (uMax - uMin) * i / divisions;
            double c = vMin + (vMax - vMin) * i / divisions;
            float fade = i == 0 || i == divisions ? 1f : 0.45f;
            yield return new GuideSegment(At(a, vMin), At(a, vMax), r * fade, g * fade, b * fade);
            yield return new GuideSegment(At(uMin, c), At(uMax, c), r * fade, g * fade, b * fade);
        }

        // Normal no ponto do cursor: mostra o eixo mesmo com o plano visto de perfil.
        double normalLength = margin * 2;
        yield return new GuideSegment(_pointOnPlane - _normal * normalLength, _pointOnPlane + _normal * normalLength, r, g, b);

        // Centro da seleção → plano → centro espelhado.
        var center = Center(_boundsMin, _boundsMax);
        var mirroredCenter = mirror.Apply(center);
        var foot = PointFrom((ToVector(center) + ToVector(mirroredCenter)) * 0.5);
        yield return new GuideSegment(center, foot, 1f, 1f, 0.2f);
        yield return new GuideSegment(foot, mirroredCenter, 1f, 1f, 0.2f);
    }

    // ---------------------------------------------------------------- Internos

    private string AxisName => _normal.X != 0 ? "X" : _normal.Y != 0 ? "Y" : "Z";

    private double PlaneCoordinate => _normal.X != 0 ? _pointOnPlane.X : _normal.Y != 0 ? _pointOnPlane.Y : _pointOnPlane.Z;

    private void Commit()
    {
        var target = new Point3D(
            _normal.X != 0 ? _pointOnPlane.X : 0,
            _normal.Y != 0 ? _pointOnPlane.Y : 0,
            _normal.Z != 0 ? _pointOnPlane.Z : 0);
        if (IsCopyMode)
            FeaturesCopied?.Invoke(target, _normal, _features);
        else
            FeaturesMirrored?.Invoke(target, _normal, _features);
        _hasMouse = false;
    }

    private static (Point3D Min, Point3D Max) Bounds(IEnumerable<IFeature> features)
    {
        var points = features.SelectMany(f => f.GetVertices()).ToList();
        if (points.Count == 0) return (Point3D.Origin, Point3D.Origin);
        return (new Point3D(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
                new Point3D(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
    }

    private static Point3D Center(Point3D min, Point3D max) =>
        new((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);

    private static IEnumerable<Point3D> Corners(Point3D min, Point3D max)
    {
        foreach (double x in new[] { min.X, max.X })
        foreach (double y in new[] { min.Y, max.Y })
        foreach (double z in new[] { min.Z, max.Z })
            yield return new Point3D(x, y, z);
    }

    /// <summary>Dois eixos globais que formam o plano perpendicular à normal (que é sempre um eixo global).</summary>
    private static (Vector3D U, Vector3D V) InPlaneAxes(Vector3D normal) =>
        normal.X != 0 ? (Vector3D.UnitY, Vector3D.UnitZ) :
        normal.Y != 0 ? (Vector3D.UnitX, Vector3D.UnitZ) :
        (Vector3D.UnitX, Vector3D.UnitY);

    private static double Coord(Point3D p, Vector3D axis) => p.X * axis.X + p.Y * axis.Y + p.Z * axis.Z;

    private static Vector3D ToVector(Point3D p) => new(p.X, p.Y, p.Z);

    private static Point3D PointFrom(Vector3D v) => new(v.X, v.Y, v.Z);

    private static (float R, float G, float B) AxisColor(Vector3D axis) =>
        axis.X != 0 ? (1f, 0.3f, 0.3f) : axis.Y != 0 ? (0.3f, 1f, 0.3f) : (0.3f, 0.5f, 1f);

    private static Matrix4x4 ToMatrix(Placement placement)
    {
        var o = placement.Apply(Point3D.Origin);
        var x = placement.ApplyVector(Vector3D.UnitX);
        var y = placement.ApplyVector(Vector3D.UnitY);
        var z = placement.ApplyVector(Vector3D.UnitZ);
        // System.Numerics usa vetor-linha (v·M): cada linha é a imagem de um eixo, a 4ª é a translação.
        return new Matrix4x4(
            (float)x.X, (float)x.Y, (float)x.Z, 0,
            (float)y.X, (float)y.Y, (float)y.Z, 0,
            (float)z.X, (float)z.Y, (float)z.Z, 0,
            (float)o.X, (float)o.Y, (float)o.Z, 1);
    }
}
