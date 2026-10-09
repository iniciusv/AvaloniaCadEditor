using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.ViewModels;
using Vector3D = AvaloniaCadEditor.Domain.ValueObjects.Vector3D;

namespace AvaloniaCadEditor.UI.Controls.Input;

/// <summary>
/// Aplica a seleção por retângulo arrastado no viewport (ver <see cref="BoxSelectionGeometry"/>
/// para as regras janela/crossing). O que é selecionado depende só do tipo de seleção
/// (<see cref="CadViewModel.CurrentSelectionMode"/>), não de estar editando um esboço:
/// <list type="bullet">
/// <item>Volumes: sólidos.</item>
/// <item>Faces: faces dos sólidos (viram um <see cref="CompoundProfile"/> quando são várias,
/// igual ao Ctrl+clique).</item>
/// <item>Edges: linhas e círculos do esboço.</item>
/// </list>
/// </summary>
public static class BoxSelector
{
    private const int CircleSamples = 48;

    /// <summary>Seleção no momento em que o botão foi pressionado - com Ctrl o retângulo soma a
    /// ela (o clique de pressionar pode já ter limpado/alterado a seleção atual).</summary>
    public sealed record Snapshot(IReadOnlyList<IFeature> Features, IReadOnlyList<Entity> Entities, IProfile? Profile)
    {
        public static Snapshot Capture(CadViewModel vm) => new(
            vm.Sketch.Features.Where(f => f.IsSelected).ToList(),
            vm.Sketch.Entities.Where(e => e.IsSelected).ToList(),
            vm.SelectionService.SelectedObject as IProfile);
    }

    public static void Apply(CadViewModel vm, CameraState camera, Size viewport, Rect rect, bool window, Snapshot? additiveBase)
    {
        var projector = new Projector(camera, viewport);

        switch (vm.CurrentSelectionMode)
        {
            case CadViewModel.TargetSelectionMode.Edge:
                // A região marcada antes sobrevive ao retângulo: ela é a seção de uma varredura e
                // as arestas são o caminho (ver SweepCommand), então uma não apaga a outra.
                vm.SelectionService.SelectMany(vm.Sketch,
                    Array.Empty<IFeature>(),
                    Union(additiveBase?.Entities, PickEntities(vm, projector, rect, window)),
                    additiveBase?.Profile);
                break;

            case CadViewModel.TargetSelectionMode.Face:
                SelectFaces(vm, PickFaces(vm, projector, rect, window), additiveBase?.Profile);
                break;

            default:
                vm.SelectionService.SelectMany(vm.Sketch,
                    Union(additiveBase?.Features, PickFeatures(vm, projector, rect, window)),
                    Array.Empty<Entity>());
                break;
        }

        vm.ModelTree.RebuildTree();
    }

    private static List<T> Union<T>(IReadOnlyList<T>? existing, IEnumerable<T> picked)
    {
        var result = existing?.ToList() ?? new List<T>();
        foreach (var item in picked)
            if (!result.Contains(item)) result.Add(item);
        return result;
    }

    private static List<Entity> PickEntities(CadViewModel vm, Projector projector, Rect rect, bool window)
    {
        var picked = new List<Entity>();
        foreach (var entity in vm.Sketch.Entities)
        {
            if (!entity.IsVisible) continue;

            var plane = entity.Plane;
            switch (entity)
            {
                case LineEntity line:
                {
                    var pts = new[] { projector.Project(plane.ToWorldSpace(line.StartPoint)), projector.Project(plane.ToWorldSpace(line.EndPoint)) };
                    if (BoxSelectionGeometry.PolylineMatches(rect, pts, closed: false, window)) picked.Add(entity);
                    break;
                }
                case CircleEntity circle:
                {
                    var pts = new Point?[CircleSamples];
                    for (int i = 0; i < CircleSamples; i++)
                    {
                        double t = 2.0 * Math.PI * i / CircleSamples;
                        var local = new Point3D(circle.Center.X + circle.Radius * Math.Cos(t), circle.Center.Y + circle.Radius * Math.Sin(t), circle.Center.Z);
                        pts[i] = projector.Project(plane.ToWorldSpace(local));
                    }
                    if (BoxSelectionGeometry.PolylineMatches(rect, pts, closed: true, window)) picked.Add(entity);
                    break;
                }
            }
        }
        return picked;
    }

    private static List<IFeature> PickFeatures(CadViewModel vm, Projector projector, Rect rect, bool window)
    {
        var picked = new List<IFeature>();
        foreach (var feature in vm.Sketch.Features.ToList())
        {
            if (!feature.IsVisible || feature is WorkPlaneFeature) continue;

            var mesh = vm.GetFeatureMesh(feature.Id);
            if (mesh == null || mesh.TriangleCount == 0) continue;

            if (BoxSelectionGeometry.MeshMatches(rect, projector.ProjectMesh(mesh), mesh.Indices, window))
                picked.Add(feature);
        }
        return picked;
    }

    /// <summary>
    /// Separa a malha de cada sólido em faces (caixa/cilindro pelas faces paramétricas; os demais
    /// por grupos de triângulos coplanares) e testa cada face contra o retângulo.
    /// </summary>
    private static List<IProfile> PickFaces(CadViewModel vm, Projector projector, Rect rect, bool window)
    {
        var picked = new List<IProfile>();
        foreach (var feature in vm.Sketch.Features.ToList())
        {
            if (!feature.IsVisible || feature is WorkPlaneFeature) continue;

            var mesh = vm.GetFeatureMesh(feature.Id);
            if (mesh == null || mesh.TriangleCount == 0) continue;

            var projected = projector.ProjectMesh(mesh);
            var groups = new Dictionary<(int, int, int, int), FaceGroup>();

            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                var a = Vertex(mesh, mesh.Indices[i]);
                var b = Vertex(mesh, mesh.Indices[i + 1]);
                var c = Vertex(mesh, mesh.Indices[i + 2]);
                var cross = new Vector3D(b.X - a.X, b.Y - a.Y, b.Z - a.Z).CrossProduct(new Vector3D(c.X - a.X, c.Y - a.Y, c.Z - a.Z));
                if (cross.Length < 1e-9) continue;
                var n = cross.Normalize();

                (int, int, int, int) key;
                FaceType? faceType = feature switch
                {
                    BoxFeature box => box.GetFaceTypeFromNormal(n),
                    CylinderFeature cyl => cyl.GetFaceTypeFromNormal(n),
                    _ => null
                };
                if (feature is BoxFeature or CylinderFeature)
                {
                    if (faceType == null) continue; // lateral curva do cilindro: não selecionável por face
                    key = ((int)faceType.Value, 0, 0, 0);
                }
                else
                {
                    double d = n.X * a.X + n.Y * a.Y + n.Z * a.Z;
                    key = ((int)Math.Round(n.X * 1000), (int)Math.Round(n.Y * 1000), (int)Math.Round(n.Z * 1000), (int)Math.Round(d * 1000));
                }

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new FaceGroup(faceType, n, a);
                    groups[key] = group;
                }
                group.Indices.Add(mesh.Indices[i]);
                group.Indices.Add(mesh.Indices[i + 1]);
                group.Indices.Add(mesh.Indices[i + 2]);
            }

            foreach (var group in groups.Values)
            {
                if (!BoxSelectionGeometry.MeshMatches(rect, projected, group.Indices, window)) continue;

                IProfile profile = feature switch
                {
                    BoxFeature box => new ParametricBoxFace(box, group.FaceType!.Value),
                    CylinderFeature cyl => new ParametricCylinderFace(cyl, group.FaceType!.Value),
                    _ => new FaceProfile(feature,
                        ViewportInputHandler.GetStableWorkPlane(vm, new RaycastHit(0, group.PointOnFace, group.Normal, feature.Id)),
                        mesh)
                };
                picked.Add(profile);
            }
        }
        return picked;
    }

    private static void SelectFaces(CadViewModel vm, List<IProfile> picked, IProfile? existing)
    {
        var subs = new List<IProfile>();
        if (existing is CompoundProfile compound) subs.AddRange(compound.SubProfiles);
        else if (existing != null) subs.Add(existing);

        foreach (var face in picked)
            if (!subs.Any(s => SameFace(s, face))) subs.Add(face);

        vm.SelectionService.ClearSelection(vm.Sketch);
        if (subs.Count == 0) return;

        foreach (var s in subs) s.IsSelected = true;
        if (subs.Count == 1)
        {
            vm.SelectionService.SelectProfile(subs[0]);
        }
        else
        {
            vm.SelectionService.SelectProfile(new CompoundProfile(subs) { IsSelected = true });
        }
    }

    private static bool SameFace(IProfile a, IProfile b) => (a, b) switch
    {
        _ when ReferenceEquals(a, b) || a.Id == b.Id => true,
        (ParametricBoxFace pa, ParametricBoxFace pb) => pa.ParentBox.Id == pb.ParentBox.Id && pa.SelectedFace == pb.SelectedFace,
        (ParametricCylinderFace ca, ParametricCylinderFace cb) => ca.ParentCylinder.Id == cb.ParentCylinder.Id && ca.SelectedFace == cb.SelectedFace,
        (FaceProfile fa, FaceProfile fb) => fa.BaseFeature.Id == fb.BaseFeature.Id &&
                                            fa.Normal.DotProduct(fb.Normal) > 0.999 &&
                                            Math.Abs(fa.Normal.DotProduct(new Vector3D(fb.Center.X - fa.Center.X, fb.Center.Y - fa.Center.Y, fb.Center.Z - fa.Center.Z))) < 1e-4,
        _ => false
    };

    private static Point3D Vertex(TriangleMesh mesh, uint index) =>
        new(mesh.Positions[index * 3], mesh.Positions[index * 3 + 1], mesh.Positions[index * 3 + 2]);

    private sealed class FaceGroup(FaceType? faceType, Vector3D normal, Point3D pointOnFace)
    {
        public FaceType? FaceType { get; } = faceType;
        public Vector3D Normal { get; } = normal;
        public Point3D PointOnFace { get; } = pointOnFace;
        public List<uint> Indices { get; } = new();
    }

    /// <summary>Mesma projeção de <see cref="ReferenceGuides.ScreenProjection"/>, mas com a matriz
    /// view*proj calculada uma vez só (as malhas podem ter milhares de vértices).</summary>
    private sealed class Projector(CameraState camera, Size viewport)
    {
        private readonly Matrix4x4 _viewProj = camera.GetViewMatrix() * camera.GetProjectionMatrix();

        public Point? Project(Point3D p) => Project((float)p.X, (float)p.Y, (float)p.Z);

        public Point? Project(float x, float y, float z)
        {
            var clip = Vector4.Transform(new Vector4(x, y, z, 1.0f), _viewProj);
            if (clip.W <= 0.0001f) return null; // atrás da câmera

            var ndc = clip / clip.W;
            return new Point((ndc.X + 1.0) * 0.5 * viewport.Width, (1.0 - ndc.Y) * 0.5 * viewport.Height);
        }

        public Point?[] ProjectMesh(TriangleMesh mesh)
        {
            var result = new Point?[mesh.VertexCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = Project(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2]);
            return result;
        }
    }
}
