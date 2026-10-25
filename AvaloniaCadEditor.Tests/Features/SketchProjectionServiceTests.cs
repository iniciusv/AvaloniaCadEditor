using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Geometry;
using FluentAssertions;
using Moq;
using static AvaloniaCadEditor.Tests.Features.TestMeshes;

namespace AvaloniaCadEditor.Tests.Features;

public class SketchProjectionServiceTests
{
    private static readonly WorkPlane Iso = new(new Vector3D(1, 1, 1), Point3D.Origin);

    private readonly Sketch _sketch = new();
    private readonly Dictionary<Guid, TriangleMesh> _meshes = new();
    private readonly SketchProjectionService _service;

    public SketchProjectionServiceTests()
    {
        var meshManager = new Mock<ISceneMeshManager>();
        meshManager
            .Setup(m => m.GetFeatureMesh(It.IsAny<Sketch>(), It.IsAny<Guid>(), It.IsAny<IFeature?>(), It.IsAny<bool>()))
            .Returns((Sketch _, Guid id, IFeature? _, bool _) => _meshes.TryGetValue(id, out var mesh) ? mesh : TriangleMesh.Empty);
        _service = new SketchProjectionService(meshManager.Object, new ClipperPolygonUnion());
    }

    private BoxFeature AddBox(string name, TriangleMesh mesh)
    {
        var box = new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = name };
        _sketch.AddFeature(box);
        _meshes[box.Id] = mesh;
        return box;
    }

    private static RaycastHit Hit(IFeature feature, Point3D point, Vector3D normal) => new(0, point, normal, feature.Id);

    [Fact]
    public void VolumeProjetado_AcompanhaAMalhaNova()
    {
        var box = AddBox("caixa", Box(0, 0, 10, 10, 0, 5));
        _service.Add(_sketch, WorkPlane.XY, ProjectionSource.Body(box, Hit(box, new Point3D(5, 5, 5), new Vector3D(0, 0, 1))));

        _service.ReferencesFor(_sketch, WorkPlane.XY).Single().Points.Max(p => p.X).Should().BeApproximately(10, 1e-6);

        _meshes[box.Id] = Box(0, 0, 20, 10, 0, 5);
        _service.ReferencesFor(_sketch, WorkPlane.XY).Single().Points.Max(p => p.X).Should().BeApproximately(20, 1e-6);
    }

    [Fact]
    public void FeatureApagada_SaiDasProjecoes()
    {
        var box = AddBox("caixa", Box(0, 0, 10, 10, 0, 5));
        _service.Add(_sketch, WorkPlane.XY, ProjectionSource.Body(box, Hit(box, new Point3D(5, 5, 5), new Vector3D(0, 0, 1))));

        _sketch.RemoveFeature(box);

        _service.ReferencesFor(_sketch, WorkPlane.XY).Should().BeEmpty();
        _service.HasAny(WorkPlane.XY).Should().BeFalse();
    }

    [Fact]
    public void FeatureRecriadaComOutroId_EhReencontradaPeloNome()
    {
        var antiga = AddBox("caixa", Box(0, 0, 10, 10, 0, 5));
        _service.Add(_sketch, WorkPlane.XY, ProjectionSource.Body(antiga, Hit(antiga, new Point3D(5, 5, 5), new Vector3D(0, 0, 1))));

        _sketch.RemoveFeature(antiga);
        AddBox("caixa", Box(0, 0, 30, 10, 0, 5));

        _service.ReferencesFor(_sketch, WorkPlane.XY).Single().Points.Max(p => p.X).Should().BeApproximately(30, 1e-6);
    }

    [Fact]
    public void PlanosDiferentes_NaoDividemProjecoes()
    {
        var box = AddBox("caixa", Box(0, 0, 10, 10, 0, 5));
        _service.Add(_sketch, WorkPlane.XY, ProjectionSource.Body(box, Hit(box, new Point3D(5, 5, 5), new Vector3D(0, 0, 1))));

        _service.ReferencesFor(_sketch, WorkPlane.XZ).Should().BeEmpty();
        _service.HasAny(WorkPlane.XZ).Should().BeFalse();
    }

    [Fact]
    public void ModoDoVolume_TrocaContornoPorTodasAsArestas()
    {
        var box = AddBox("caixa", Box(0, 0, 10, 10, 0, 10));
        _service.Add(_sketch, Iso, ProjectionSource.Body(box, Hit(box, new Point3D(10, 5, 5), new Vector3D(1, 0, 0))));

        var contorno = _service.ReferencesFor(_sketch, Iso);
        _service.VolumeMode = VolumeProjectionMode.AllEdges;
        var arestas = _service.ReferencesFor(_sketch, Iso);

        contorno.Should().ContainSingle(r => r.Closed && r.Points.Count == 6, "a sombra da caixa em vista isométrica é um hexágono");
        arestas.Should().HaveCount(12);
    }

    [Fact]
    public void Face_EhAMesmaQuandoClicadaEmOutroPonto_ENaoSeDuplica()
    {
        var box = AddBox("caixa", Box(0, 0, 10, 10, 0, 5));
        var topo = ProjectionSource.Face(box, Hit(box, new Point3D(2, 2, 5), new Vector3D(0, 0, 1)));
        var topoEmOutroPonto = ProjectionSource.Face(box, Hit(box, new Point3D(8, 7, 5), new Vector3D(0, 0, 1)));
        var lateral = ProjectionSource.Face(box, Hit(box, new Point3D(10, 5, 2), new Vector3D(1, 0, 0)));

        _service.Add(_sketch, WorkPlane.XY, topo);
        _service.Add(_sketch, WorkPlane.XY, topoEmOutroPonto);

        _service.Contains(_sketch, WorkPlane.XY, topoEmOutroPonto).Should().BeTrue();
        _service.Contains(_sketch, WorkPlane.XY, lateral).Should().BeFalse();
        _service.Contains(_sketch, WorkPlane.XY, ProjectionSource.Body(box, Hit(box, new Point3D(2, 2, 5), new Vector3D(0, 0, 1)))).Should().BeFalse();
        _service.ReferencesFor(_sketch, WorkPlane.XY).Should().ContainSingle();

        _service.Remove(_sketch, WorkPlane.XY, topoEmOutroPonto);
        _service.HasAny(WorkPlane.XY).Should().BeFalse();
    }
}
