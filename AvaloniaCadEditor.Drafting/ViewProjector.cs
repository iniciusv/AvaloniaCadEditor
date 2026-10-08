using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Drafting;

/// <summary>
/// Projeta as primitivas da árvore de features numa vista padrão. Trabalha só com os parâmetros
/// das features (centro, eixo, raio, cantos) — o kernel não expõe arestas analíticas e os furos
/// são prismas de 32 lados na malha, então é daqui que vêm as medidas exatas.
///
/// Uma primitiva só é projetada quando está alinhada à vista (eixo paralelo ou perpendicular à
/// direção de quem olha, e no segundo caso alinhado a U ou V); as oblíquas viram aviso.
/// </summary>
public static class ViewProjector
{
    private const double AlignTolerance = 1e-6;

    public static (IReadOnlyList<ViewEntity> Entities, IReadOnlyList<string> Warnings) Project(
        IFeature root, Placement rootToFrame, ViewBasis basis)
    {
        var walker = new Walker(basis);
        walker.Walk(root, rootToFrame, EntityRole.Body);
        return (walker.Entities, walker.Warnings);
    }

    private sealed class Walker(ViewBasis basis)
    {
        private readonly HashSet<Guid> _visited = new();
        private readonly Dictionary<string, int> _names = new();

        public List<ViewEntity> Entities { get; } = new();
        public List<string> Warnings { get; } = new();

        public void Walk(IFeature? feature, Placement toFrame, EntityRole role)
        {
            if (feature == null || !_visited.Add(feature.Id)) return;

            switch (feature)
            {
                case BooleanFeature boolean:
                    foreach (var b in boolean.BaseFeatures) Walk(b, toFrame, role);
                    var toolRole = boolean.Operation == BooleanOperationType.Subtract ? EntityRole.Void : role;
                    foreach (var tool in boolean.ToolFeatures) Walk(tool, toFrame, toolRole);
                    return;

                case PartInstance instance:
                    Walk(instance.Definition, instance.Placement.Then(toFrame), role);
                    return;

                case TransformedFeature transformed:
                    Walk(transformed.Source, transformed.Placement.Then(toFrame), role);
                    return;

                case FilletFeature fillet:
                    Walk(fillet.BaseFeature, toFrame, role);
                    return;

                case BoxFeature box:
                    AddBox(box, toFrame, RoleOf(box, role));
                    return;

                case CylinderFeature cylinder:
                    AddCylinder(cylinder.Name, RoleOf(cylinder, role), toFrame,
                        cylinder.GlobalBaseCenter, cylinder.GlobalDirection, cylinder.Radius, cylinder.Height);
                    return;

                case ExtrudeFeature { Profile: CircleProfile circle } extrude:
                    var axis = extrude.Direction.Normalize() * Math.Sign(extrude.Height == 0 ? 1 : extrude.Height);
                    AddCylinder(extrude.Name, RoleOf(extrude, role), toFrame,
                        circle.Plane.ToWorldSpace(circle.Center), axis, circle.Radius, Math.Abs(extrude.Height));
                    return;

                case ExtrudeFeature { Profile: { } profile } extrude:
                    var baseVertices = profile.GetVertices();
                    var up = extrude.Direction.Normalize() * extrude.Height;
                    AddPrism(extrude.Name, RoleOf(extrude, role), toFrame, baseVertices, baseVertices.Select(v => v + up).ToList(), extrude.Direction);
                    return;

                case PrismFeature prism:
                    var all = prism.GetVertices();
                    var count = prism.Vertices.Count;
                    AddPrism(prism.Name, RoleOf(prism, role), toFrame, all.Take(count).ToList(), all.Skip(count).ToList(), prism.Plane.Normal);
                    return;

                default:
                    foreach (var dependency in feature.Dependencies) Walk(dependency, toFrame, role);
                    return;
            }
        }

        private static EntityRole RoleOf(IFeature feature, EntityRole inherited) =>
            feature.IsTransientTool ? EntityRole.Void : inherited;

        /// <summary>Nomes repetidos (peças aninhadas) ganham sufixo, para os Ids de âncora não colidirem.</summary>
        private string UniqueName(string name)
        {
            if (!_names.TryGetValue(name, out var count))
            {
                _names[name] = 1;
                return name;
            }

            _names[name] = count + 1;
            return $"{name}#{count + 1}";
        }

        private void AddBox(BoxFeature box, Placement toFrame, EntityRole role)
        {
            foreach (var direction in new[] { box.DirectionX, box.DirectionY, box.DirectionZ })
            {
                if (Classify(toFrame.ApplyVector(direction)) == Alignment.Oblique)
                {
                    Warnings.Add($"{box.Name}: caixa oblíqua à vista, não desenhada.");
                    return;
                }
            }

            AddRect(box.Name, role, ProjectAll(box.GetVertices(), toFrame), CenterAxis.None);
        }

        private void AddCylinder(string name, EntityRole role, Placement toFrame, Point3D baseCenter, Vector3D direction, double radius, double height)
        {
            var center = toFrame.Apply(baseCenter);
            var axis = toFrame.ApplyVector(direction).Normalize();

            switch (Classify(axis))
            {
                case Alignment.AlongW:
                    Entities.Add(new ViewCircle(UniqueName(name), role, basis.Project(center), radius));
                    return;

                case Alignment.AlongU:
                case Alignment.AlongV:
                    var a = basis.Project(center);
                    var b = basis.Project(center + axis * height);
                    bool horizontal = Classify(axis) == Alignment.AlongU;
                    var min = horizontal
                        ? new P2(Math.Min(a.U, b.U), a.V - radius)
                        : new P2(a.U - radius, Math.Min(a.V, b.V));
                    var max = horizontal
                        ? new P2(Math.Max(a.U, b.U), a.V + radius)
                        : new P2(a.U + radius, Math.Max(a.V, b.V));
                    Entities.Add(new ViewRect(UniqueName(name), role, min, max, horizontal ? CenterAxis.Horizontal : CenterAxis.Vertical));
                    return;

                default:
                    Warnings.Add($"{name}: cilindro oblíquo à vista, não desenhado.");
                    return;
            }
        }

        private void AddPrism(string name, EntityRole role, Placement toFrame, IReadOnlyList<Point3D> baseVertices, IReadOnlyList<Point3D> topVertices, Vector3D direction)
        {
            if (baseVertices.Count < 3) return;

            switch (Classify(toFrame.ApplyVector(direction).Normalize()))
            {
                case Alignment.AlongW:
                    Entities.Add(new ViewPolygon(UniqueName(name), role, ProjectAll(baseVertices, toFrame)));
                    return;

                case Alignment.AlongU:
                case Alignment.AlongV:
                    // De lado, um prisma é o retângulo que envolve a seção e a altura.
                    AddRect(name, role, ProjectAll(baseVertices.Concat(topVertices).ToList(), toFrame), CenterAxis.None);
                    return;

                default:
                    Warnings.Add($"{name}: prisma oblíquo à vista, não desenhado.");
                    return;
            }
        }

        private void AddRect(string name, EntityRole role, IReadOnlyList<P2> points, CenterAxis axis)
        {
            var min = new P2(points.Min(p => p.U), points.Min(p => p.V));
            var max = new P2(points.Max(p => p.U), points.Max(p => p.V));
            Entities.Add(new ViewRect(UniqueName(name), role, min, max, axis));
        }

        private List<P2> ProjectAll(IEnumerable<Point3D> points, Placement toFrame) =>
            points.Select(p => basis.Project(toFrame.Apply(p))).ToList();

        private Alignment Classify(Vector3D direction)
        {
            var d = direction.Normalize();
            if (Math.Abs(Math.Abs(d.DotProduct(basis.W)) - 1) < AlignTolerance) return Alignment.AlongW;
            if (Math.Abs(Math.Abs(d.DotProduct(basis.U)) - 1) < AlignTolerance) return Alignment.AlongU;
            if (Math.Abs(Math.Abs(d.DotProduct(basis.V)) - 1) < AlignTolerance) return Alignment.AlongV;
            return Alignment.Oblique;
        }
    }

    private enum Alignment
    {
        AlongU,
        AlongV,
        AlongW,
        Oblique
    }
}
