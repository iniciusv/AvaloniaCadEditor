using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Ferramenta Sweep: tubo de seção circular varrido por uma polilinha clicada no work plane.
/// <para>
/// A seção é circular e perpendicular ao caminho — por isso NÃO se escolhe um perfil do sketch,
/// como faz o <see cref="ExtrudeCommand"/>: um perfil desenhado no work plane fica no mesmo plano
/// do caminho, e varrer uma seção paralela ao caminho não quer dizer nada. O raio é digitado.
/// </para>
/// <para>
/// Como as demais ferramentas, não constrói geometria: emite o CadScript equivalente por
/// <see cref="CodeGenerated"/>, e quem executa é o editor de script.
/// </para>
/// <para>
/// Limites desta versão: caminho no plano do sketch (clique dá ponto no plano) e seção circular.
/// O preview é a polilinha do caminho; o sólido aparece ao concluir, porque o preview 3D hoje está
/// amarrado ao tipo <see cref="ExtrudeCommand"/> em uma dúzia de lugares.
/// </para>
/// </summary>
public class SweepCommand : ICommandTool
{
    public event EventHandler<string>? CodeGenerated;
    public string Name => "Sweep";

    /// <summary>Raio da seção, em mm. Digitável antes do primeiro clique.</summary>
    public double SectionRadius { get; private set; } = 5.0;

    /// <summary>
    /// Raio dos cantos. Zero significa "derive de SectionRadius" — um canto precisa de raio maior
    /// que a seção, senão a parede interna cruza o eixo do arco e o sólido dobra para dentro de si
    /// mesmo. O dobro é uma dobra folgada e sempre válida.
    /// </summary>
    private double _cornerRadius;
    public double EffectiveCornerRadius => _cornerRadius > 0 ? _cornerRadius : SectionRadius * 2.0;

    private readonly List<Point3D> _points = new();
    private Point3D? _cursor;
    private WorkPlane? _plane;

    /// <summary>Pontos do caminho já fixados, em coordenadas LOCAIS ao plano do sketch.</summary>
    public IReadOnlyList<Point3D> Points => _points;

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        _plane = sketch.Plane;
        // Clicar duas vezes no mesmo lugar fecharia um trecho de comprimento zero.
        if (_points.Count > 0 && _points[^1].DistanceTo(point) < 1e-6) return;
        _points.Add(point);
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        _plane = sketch.Plane;
        _cursor = point;
    }

    public void Cancel()
    {
        _points.Clear();
        _cursor = null;
        _cornerRadius = 0;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape) Cancel();
        else if (key == CadKey.Enter) Commit(sketch);
    }

    /// <summary>
    /// Antes do primeiro ponto, o valor digitado é o raio da SEÇÃO; com o caminho já começado, é o
    /// raio dos CANTOS, e conclui. Mesma convenção do <see cref="DrawPolygonCommand"/>, onde o
    /// número digitado muda de significado conforme o estágio.
    /// </summary>
    public void OnValueEntered(Sketch sketch, double value)
    {
        if (value <= 0) return;

        if (_points.Count == 0)
        {
            SectionRadius = value;
            return;
        }

        _cornerRadius = value;
        Commit(sketch);
    }

    public IEnumerable<Entity> GetPreviewEntities()
    {
        var plane = _plane ?? WorkPlane.XY;

        for (int i = 0; i + 1 < _points.Count; i++)
        {
            yield return new LineEntity(_points[i], _points[i + 1]) { Plane = plane };
        }

        // Elástico até o cursor: mostra para onde o próximo trecho vai.
        if (_points.Count > 0 && _cursor is { } cursor && _points[^1].DistanceTo(cursor) > 1e-6)
        {
            yield return new LineEntity(_points[^1], cursor) { Plane = plane };
        }
    }

    public string GetMeasurementText()
    {
        if (_points.Count == 0)
            return $"Seção R: {SectionRadius:F2} (digite para mudar, depois clique o caminho)";

        return _points.Count < 2
            ? $"Seção R: {SectionRadius:F2}  — clique o próximo ponto do caminho"
            : $"Canto R: {EffectiveCornerRadius:F2} (digite para mudar)  — Enter conclui";
    }

    public string GetStatusText() => _points.Count == 0
        ? "Sweep: digite o raio da seção e clique os pontos do caminho."
        : "Sweep: clique os pontos do caminho; Enter conclui, Esc cancela.";

    public Point3D? GetReferencePoint() => _points.Count > 0 ? _points[^1] : null;

    private void Commit(Sketch sketch)
    {
        if (_points.Count < 2) return;

        // Os cliques vêm em coordenadas locais ao plano do sketch; SweepPath quer o mundo.
        var plane = sketch.Plane;
        var world = _points.Select(plane.ToWorldSpace).ToList();

        CodeGenerated?.Invoke(this, FeatureCodeGenerator.GenerateSweepCode(world, SectionRadius, EffectiveCornerRadius));
        Cancel();
    }
}
