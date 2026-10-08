using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Analysis;

/// <summary>
/// Camada 1: o único percurso da árvore de features. Acumula o <see cref="Placement"/> de cada
/// <see cref="PartInstance"/> e <see cref="TransformedFeature"/>, decide o papel (material ou vazio)
/// pelas booleanas e por <c>IsTransientTool</c>, e devolve as primitivas como
/// <see cref="PrimitiveFact"/> no referencial pedido.
/// </summary>
public static class FactExtractor
{
    /// <summary>
    /// Tipos de feature que o percurso transforma em fatos ou atravessa (contêineres). Uma feature
    /// nova que não esteja aqui nem em <see cref="IgnoredFeatureTypes"/> cai no <c>default</c> e fica
    /// <b>invisível</b> para as cotas e as guias, sem erro — <c>FactExtractorCoverageTests</c> falha
    /// para obrigar a decisão. Mantenha em sincronia com o <c>switch</c> de <c>Walk</c>.
    /// </summary>
    public static IReadOnlyCollection<Type> HandledFeatureTypes { get; } =
    [
        typeof(BooleanFeature), typeof(PartInstance), typeof(TransformedFeature), typeof(FilletFeature),
        typeof(CylinderFeature), typeof(ConeFeature), typeof(ExtrudeFeature), typeof(BoxFeature), typeof(PrismFeature)
    ];

    /// <summary>Tipos de feature deixados de fora de propósito, com o motivo.</summary>
    public static IReadOnlyDictionary<Type, string> IgnoredFeatureTypes { get; } = new Dictionary<Type, string>
    {
        [typeof(WorkPlaneFeature)] = "plano de trabalho, não é sólido",
        [typeof(ImportedMeshFeature)] = "só malha (STL): não há parâmetros de onde tirar medidas exatas",
        [typeof(SweepFeature)] = "tubo varrido ainda não vira fatos (o caminho seria um cilindro por trecho reto do caminho)"
    };

    public static IReadOnlyList<PrimitiveFact> Extract(IEnumerable<AnalysisSubject> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        var facts = new List<PrimitiveFact>();
        var seen = new HashSet<(Guid, long, long, long, long, long, long)>();
        foreach (var subject in subjects)
        {
            var walker = new Walker(subject, facts, seen);
            walker.Walk(subject.Feature, subject.ToFrame, PrimitiveRole.Body, prefix: "", nameOverride: null);
        }
        return facts;
    }

    private sealed class Walker(AnalysisSubject subject, List<PrimitiveFact> facts, HashSet<(Guid, long, long, long, long, long, long)> seen)
    {
        private readonly Guid _owner = subject.OwnerId ?? subject.Feature.Id;
        private readonly Dictionary<string, int> _names = new();

        public void Walk(IFeature? feature, Placement toFrame, PrimitiveRole role, string prefix, string? nameOverride)
        {
            if (feature == null) return;

            switch (feature)
            {
                case BooleanFeature boolean:
                    foreach (var b in boolean.BaseFeatures) Walk(b, toFrame, role, prefix, null);
                    var toolRole = boolean.Operation == BooleanOperationType.Subtract ? PrimitiveRole.Void : role;
                    foreach (var tool in boolean.ToolFeatures) Walk(tool, toFrame, toolRole, prefix, null);
                    return;

                case PartInstance instance:
                    Walk(instance.Definition, instance.Placement.Then(toFrame), role, $"{prefix}{instance.Name}/", null);
                    return;

                case TransformedFeature transformed:
                    // Cópia/movimento de uma primitiva: o fato leva o nome da cópia.
                    Walk(transformed.Source, transformed.Placement.Then(toFrame), role, prefix, transformed.Name);
                    return;

                case FilletFeature fillet:
                    Walk(fillet.BaseFeature, toFrame, role, prefix, nameOverride ?? fillet.Name);
                    return;

                case CylinderFeature cylinder:
                    AddRound(feature, PrimitiveKind.Cylinder, Role(feature, role), toFrame, prefix, nameOverride,
                        cylinder.GlobalBaseCenter, cylinder.GlobalDirection, cylinder.Radius, cylinder.Radius, cylinder.Height);
                    return;

                case ConeFeature cone:
                    AddRound(feature, PrimitiveKind.Cone, Role(feature, role), toFrame, prefix, nameOverride,
                        cone.GlobalBaseCenter, cone.GlobalDirection, cone.BottomRadius, cone.TopRadius, cone.Height);
                    return;

                case ExtrudeFeature { Profile: CircleProfile circle } extrude:
                    AddRound(feature, PrimitiveKind.Cylinder, Role(feature, role), toFrame, prefix, nameOverride,
                        circle.Plane.ToWorldSpace(circle.Center), ExtrudeAxis(extrude), circle.Radius, circle.Radius, Math.Abs(extrude.Height));
                    return;

                case ExtrudeFeature { Profile: { } profile } extrude:
                    AddSection(feature, PrimitiveKind.Prism, Role(feature, role), toFrame, prefix, nameOverride,
                        profile.GetVertices(), ExtrudeAxis(extrude), Math.Abs(extrude.Height));
                    return;

                case BoxFeature box:
                    AddSection(feature, PrimitiveKind.Box, Role(feature, role), toFrame, prefix, nameOverride,
                        box.GetVertices().Take(4).ToList(), box.DirectionZ, box.Height);
                    return;

                case PrismFeature prism:
                    AddSection(feature, PrimitiveKind.Prism, Role(feature, role), toFrame, prefix, nameOverride,
                        prism.Vertices.Select(prism.Plane.ToWorldSpace).ToList(), prism.Plane.Normal, prism.Height);
                    return;

                default:
                    foreach (var dependency in feature.Dependencies) Walk(dependency, toFrame, role, prefix, null);
                    return;
            }
        }

        private static PrimitiveRole Role(IFeature feature, PrimitiveRole inherited) =>
            feature.IsTransientTool ? PrimitiveRole.Void : inherited;

        private static Vector3D ExtrudeAxis(ExtrudeFeature extrude) =>
            extrude.Direction.Normalize() * (extrude.Height < 0 ? -1 : 1);

        private void AddRound(IFeature feature, PrimitiveKind kind, PrimitiveRole role, Placement toFrame, string prefix, string? nameOverride,
            Point3D baseCenter, Vector3D direction, double radius, double topRadius, double height)
        {
            var center = toFrame.Apply(baseCenter);
            var axis = toFrame.ApplyVector(direction).Normalize();
            if (height < 0)
            {
                center += axis * height;
                height = -height;
                (radius, topRadius) = (topRadius, radius);
            }

            var top = center + axis * height;
            double r = Math.Max(radius, topRadius);
            double Spread(double a) => r * Math.Sqrt(Math.Max(0, 1 - a * a));
            var min = new Point3D(
                Math.Min(center.X, top.X) - Spread(axis.X),
                Math.Min(center.Y, top.Y) - Spread(axis.Y),
                Math.Min(center.Z, top.Z) - Spread(axis.Z));
            var max = new Point3D(
                Math.Max(center.X, top.X) + Spread(axis.X),
                Math.Max(center.Y, top.Y) + Spread(axis.Y),
                Math.Max(center.Z, top.Z) + Spread(axis.Z));

            Add(feature, kind, role, prefix, nameOverride, axis, height, center, radius, topRadius, Array.Empty<Point3D>(), min, max);
        }

        private void AddSection(IFeature feature, PrimitiveKind kind, PrimitiveRole role, Placement toFrame, string prefix, string? nameOverride,
            IReadOnlyList<Point3D> profile, Vector3D direction, double height)
        {
            if (profile.Count < 3) return;

            var loop = profile.Select(toFrame.Apply).ToList();
            var axis = toFrame.ApplyVector(direction).Normalize();
            if (height < 0)
            {
                loop = loop.Select(p => p + axis * height).ToList();
                height = -height;
            }

            var all = loop.Concat(loop.Select(p => p + axis * height)).ToList();
            var min = new Point3D(all.Min(p => p.X), all.Min(p => p.Y), all.Min(p => p.Z));
            var max = new Point3D(all.Max(p => p.X), all.Max(p => p.Y), all.Max(p => p.Z));
            var baseCenter = new Point3D(loop.Average(p => p.X), loop.Average(p => p.Y), loop.Average(p => p.Z));

            Add(feature, kind, role, prefix, nameOverride, axis, height, baseCenter, 0, 0, loop, min, max);
        }

        private void Add(IFeature feature, PrimitiveKind kind, PrimitiveRole role, string prefix, string? nameOverride,
            Vector3D axis, double height, Point3D baseCenter, double radius, double topRadius, IReadOnlyList<Point3D> profile,
            Point3D min, Point3D max)
        {
            // A mesma primitiva no mesmo lugar aparece mais de uma vez quando a cena registra as
            // operandas e a booleana separadamente: um fato só. Uma cópia (outro Placement) é outro fato.
            var key = (feature.Id, Round(min.X), Round(min.Y), Round(min.Z), Round(max.X), Round(max.Y), Round(max.Z));
            if (!seen.Add(key)) return;

            facts.Add(new PrimitiveFact
            {
                Id = feature.Id,
                OwnerId = _owner,
                Subject = subject.Name,
                Name = UniqueName(prefix + (nameOverride ?? feature.Name)),
                Kind = kind,
                Role = role,
                Axis = axis,
                Height = height,
                BaseCenter = baseCenter,
                Radius = radius,
                TopRadius = topRadius,
                Profile = profile,
                BoundsMin = min,
                BoundsMax = max
            });
        }

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

        private static long Round(double value) => (long)Math.Round(value * 1e6);
    }
}
