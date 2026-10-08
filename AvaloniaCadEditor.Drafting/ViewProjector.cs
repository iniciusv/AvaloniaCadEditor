using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Drafting;

/// <summary>
/// Projeta os fatos da análise numa vista padrão. As medidas são as dos parâmetros das features
/// (centro, eixo, raio, seção) — o kernel não expõe arestas analíticas e os furos são prismas de
/// 32 lados na malha, então é daqui que vêm os valores exatos.
///
/// Um fato só é desenhado com o eixo paralelo à vista (círculo / seção) ou a U ou V (de lado);
/// os oblíquos viram aviso.
/// </summary>
public static class ViewProjector
{
    private const double AlignTolerance = 1e-6;

    public static (IReadOnlyList<ViewEntity> Entities, IReadOnlyList<string> Warnings) Project(
        IReadOnlyList<PrimitiveFact> facts, ViewBasis basis)
    {
        var entities = new List<ViewEntity>();
        var warnings = new List<string>();

        foreach (var fact in facts)
        {
            var role = fact.Role;
            var entity = fact.IsRound ? ProjectRound(fact, role, basis) : ProjectSection(fact, role, basis);
            if (entity == null)
            {
                warnings.Add($"{fact.Name}: oblíquo à vista, não desenhado.");
                continue;
            }
            entities.Add(entity with { Fact = fact });
        }

        return (entities, warnings);
    }

    private static ViewEntity? ProjectRound(PrimitiveFact fact, PrimitiveRole role, ViewBasis basis)
    {
        double r = fact.MaxRadius;
        switch (Classify(fact.Axis, basis))
        {
            case Alignment.AlongW:
                return new ViewCircle(fact.Name, role, basis.Project(fact.BaseCenter), r);

            case Alignment.AlongU:
            case Alignment.AlongV:
                var a = basis.Project(fact.BaseCenter);
                var b = basis.Project(fact.TopCenter);
                bool horizontal = Classify(fact.Axis, basis) == Alignment.AlongU;
                var min = horizontal ? new P2(Math.Min(a.U, b.U), a.V - r) : new P2(a.U - r, Math.Min(a.V, b.V));
                var max = horizontal ? new P2(Math.Max(a.U, b.U), a.V + r) : new P2(a.U + r, Math.Max(a.V, b.V));
                return new ViewRect(fact.Name, role, min, max, horizontal ? CenterAxis.Horizontal : CenterAxis.Vertical);

            default:
                return null;
        }
    }

    private static ViewEntity? ProjectSection(PrimitiveFact fact, PrimitiveRole role, ViewBasis basis)
    {
        switch (Classify(fact.Axis, basis))
        {
            case Alignment.AlongW:
                var points = fact.Profile.Select(basis.Project).ToList();
                return IsAxisAlignedRectangle(points)
                    ? Rect(fact.Name, role, points)
                    : new ViewPolygon(fact.Name, role, points);

            case Alignment.AlongU:
            case Alignment.AlongV:
                // De lado, uma seção extrudada é o retângulo que envolve a seção e a altura.
                return Rect(fact.Name, role, fact.SectionVertices.Select(basis.Project).ToList());

            default:
                return null;
        }
    }

    private static ViewRect Rect(string name, PrimitiveRole role, IReadOnlyList<P2> points) =>
        new(name, role, new P2(points.Min(p => p.U), points.Min(p => p.V)), new P2(points.Max(p => p.U), points.Max(p => p.V)));

    private static bool IsAxisAlignedRectangle(IReadOnlyList<P2> points)
    {
        if (points.Count != 4) return false;
        for (int i = 0; i < 4; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % 4];
            if (Math.Abs(a.U - b.U) > 1e-6 && Math.Abs(a.V - b.V) > 1e-6) return false;
        }
        return true;
    }

    private static Alignment Classify(Vector3D direction, ViewBasis basis)
    {
        var d = direction.Normalize();
        if (Math.Abs(Math.Abs(d.DotProduct(basis.W)) - 1) < AlignTolerance) return Alignment.AlongW;
        if (Math.Abs(Math.Abs(d.DotProduct(basis.U)) - 1) < AlignTolerance) return Alignment.AlongU;
        if (Math.Abs(Math.Abs(d.DotProduct(basis.V)) - 1) < AlignTolerance) return Alignment.AlongV;
        return Alignment.Oblique;
    }

    private enum Alignment
    {
        AlongU,
        AlongV,
        AlongW,
        Oblique
    }
}
