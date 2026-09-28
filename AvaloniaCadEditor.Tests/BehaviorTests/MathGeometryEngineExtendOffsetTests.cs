using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Testa diretamente a matemática de ExtendEntity, GetApparentIntersection e OffsetProfile
/// implementada em MathGeometryEngine, usada pelas ferramentas Extend e Offset.
/// </summary>
public class MathGeometryEngineExtendOffsetTests
{
    private readonly MathGeometryEngine _engine = new();

    [Fact]
    public void ExtendEntity_DevePrologarAPontaMaisProximaDoClique_AteALinhaFronteira()
    {
        var line = new LineEntity(new Point3D(0, 0), new Point3D(5, 0));
        var boundary = new LineEntity(new Point3D(10, -5), new Point3D(10, 5));

        // Clique perto de (5,0): a ponta EndPoint deve ser a estendida.
        var result = _engine.ExtendEntity(line, new Entity[] { boundary }, new Point3D(5, 0)) as LineEntity;

        result.Should().NotBeNull();
        result!.StartPoint.Should().Be(new Point3D(0, 0));
        result.EndPoint.X.Should().BeApproximately(10, 1e-6);
        result.EndPoint.Y.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void ExtendEntity_DeveEscolherAFronteiraMaisProxima()
    {
        var line = new LineEntity(new Point3D(0, 0), new Point3D(5, 0));
        var farBoundary = new LineEntity(new Point3D(10, -5), new Point3D(10, 5));
        var nearBoundary = new LineEntity(new Point3D(6, -5), new Point3D(6, 5));

        var result = _engine.ExtendEntity(line, new Entity[] { farBoundary, nearBoundary }, new Point3D(5, 0)) as LineEntity;

        result.Should().NotBeNull();
        result!.EndPoint.X.Should().BeApproximately(6, 1e-6);
    }

    [Fact]
    public void ExtendEntity_DeveEstenderAteUmCirculo()
    {
        var line = new LineEntity(new Point3D(0, 0), new Point3D(3, 0));
        var circle = new CircleEntity(new Point3D(10, 0), 2); // borda mais próxima em x=8

        var result = _engine.ExtendEntity(line, new Entity[] { circle }, new Point3D(3, 0)) as LineEntity;

        result.Should().NotBeNull();
        result!.EndPoint.X.Should().BeApproximately(8, 1e-6);
        result.EndPoint.Y.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void ExtendEntity_SemFronteiraNaDirecao_DeveRetornarNull()
    {
        var line = new LineEntity(new Point3D(0, 0), new Point3D(5, 0));
        var parallelBoundary = new LineEntity(new Point3D(0, 5), new Point3D(5, 5)); // paralela, nunca cruza

        var result = _engine.ExtendEntity(line, new Entity[] { parallelBoundary }, new Point3D(5, 0));

        result.Should().BeNull();
    }

    [Fact]
    public void GetApparentIntersection_DeveEncontrarCantoDeLAberto()
    {
        var lineA = new LineEntity(new Point3D(0, 0), new Point3D(5, 0));
        var lineB = new LineEntity(new Point3D(10, 5), new Point3D(10, 10));

        var point = _engine.GetApparentIntersection(lineA, lineB);

        point.Should().NotBeNull();
        point!.Value.X.Should().BeApproximately(10, 1e-6);
        point.Value.Y.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void GetApparentIntersection_LinhasParalelas_DeveRetornarNull()
    {
        var lineA = new LineEntity(new Point3D(0, 0), new Point3D(5, 0));
        var lineB = new LineEntity(new Point3D(0, 5), new Point3D(5, 5));

        var point = _engine.GetApparentIntersection(lineA, lineB);

        point.Should().BeNull();
    }

    [Fact]
    public void OffsetProfile_Quadrado_ExpandirParaFora_DeveAumentarLado()
    {
        var profile = new PolygonProfile(new[]
        {
            new Point3D(0, 0), new Point3D(10, 0), new Point3D(10, 10), new Point3D(0, 10)
        }, plane: WorkPlane.XY);

        var result = _engine.OffsetProfile(profile, 2.0).OfType<LineEntity>().ToList();

        result.Should().HaveCount(4);
        var xs = result.SelectMany(l => new[] { l.StartPoint.X, l.EndPoint.X }).Distinct().OrderBy(x => x).ToList();
        var ys = result.SelectMany(l => new[] { l.StartPoint.Y, l.EndPoint.Y }).Distinct().OrderBy(y => y).ToList();

        xs.Should().HaveCount(2);
        xs[0].Should().BeApproximately(-2, 1e-6);
        xs[1].Should().BeApproximately(12, 1e-6);
        ys[0].Should().BeApproximately(-2, 1e-6);
        ys[1].Should().BeApproximately(12, 1e-6);
    }

    [Fact]
    public void OffsetProfile_Quadrado_ContrairParaDentro_DeveDiminuirLado()
    {
        var profile = new PolygonProfile(new[]
        {
            new Point3D(0, 0), new Point3D(10, 0), new Point3D(10, 10), new Point3D(0, 10)
        }, plane: WorkPlane.XY);

        var result = _engine.OffsetProfile(profile, -2.0).OfType<LineEntity>().ToList();

        var xs = result.SelectMany(l => new[] { l.StartPoint.X, l.EndPoint.X }).Distinct().OrderBy(x => x).ToList();
        xs[0].Should().BeApproximately(2, 1e-6);
        xs[1].Should().BeApproximately(8, 1e-6);
    }

    [Fact]
    public void OffsetProfile_ContracaoMaiorQueARegiao_DeveRetornarVazio()
    {
        var profile = new PolygonProfile(new[]
        {
            new Point3D(0, 0), new Point3D(10, 0), new Point3D(10, 10), new Point3D(0, 10)
        }, plane: WorkPlane.XY);

        var result = _engine.OffsetProfile(profile, -6.0);

        result.Should().BeEmpty();
    }

    [Fact]
    public void OffsetProfile_Circulo_DeveAjustarRaio()
    {
        var profile = new CircleProfile(new Point3D(0, 0), 5, plane: WorkPlane.XY);

        var expanded = _engine.OffsetProfile(profile, 2.0).OfType<CircleEntity>().Single();
        expanded.Radius.Should().BeApproximately(7, 1e-6);

        var shrunk = _engine.OffsetProfile(profile, -2.0).OfType<CircleEntity>().Single();
        shrunk.Radius.Should().BeApproximately(3, 1e-6);

        var invalid = _engine.OffsetProfile(profile, -10.0);
        invalid.Should().BeEmpty();
    }
}
