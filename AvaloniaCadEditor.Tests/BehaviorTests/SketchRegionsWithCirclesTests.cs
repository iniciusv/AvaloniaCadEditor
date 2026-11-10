using System;
using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Círculo e linhas do mesmo plano formam um só arranjo: um círculo que cruza um contorno o divide em
/// regiões, como uma linha já dividia.
/// </summary>
public class SketchRegionsWithCirclesTests
{
    private static Sketch Square(double size)
    {
        var sketch = new Sketch();
        sketch.AddEntity(new LineEntity(new Point3D(0, 0), new Point3D(size, 0)));
        sketch.AddEntity(new LineEntity(new Point3D(size, 0), new Point3D(size, size)));
        sketch.AddEntity(new LineEntity(new Point3D(size, size), new Point3D(0, size)));
        sketch.AddEntity(new LineEntity(new Point3D(0, size), new Point3D(0, 0)));
        return sketch;
    }

    /// <summary>A área que a triangulação pinta, para conferir que o preenchimento fica dentro da região.</summary>
    private static double PaintedArea(IProfile profile)
    {
        var (verts, indices) = profile.GetTessellatedFace();
        double sum = 0;
        for (int i = 0; i < indices.Count; i += 3)
        {
            var a = verts[(int)indices[i]];
            var b = verts[(int)indices[i + 1]];
            var c = verts[(int)indices[i + 2]];
            sum += Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
        }
        return sum;
    }

    [Fact]
    public void CirculoCruzandoUmLado_DivideEmTresRegioesSemSobreposicao()
    {
        var sketch = Square(20);
        var circle = new CircleEntity(new Point3D(20, 10), 5);
        sketch.AddEntity(circle);

        var profiles = new ProfileRecognitionService().RecognizeProfiles(sketch);

        profiles.Should().HaveCount(3);
        profiles.Should().NotContain(p => p is CircleProfile);

        double halfDisk = Math.PI * 25 / 2;
        var areas = profiles.Select(p => p.Area).OrderBy(a => a).ToList();
        areas[0].Should().BeApproximately(halfDisk, halfDisk * 0.01);
        areas[1].Should().BeApproximately(halfDisk, halfDisk * 0.01);
        areas[2].Should().BeApproximately(400 - halfDisk, 1);

        // As regiões do lado de dentro somam o quadrado: nada sobra nem se sobrepõe.
        var inside = profiles.Where(p => p.Center.X < 20).ToList();
        inside.Should().HaveCount(2);
        inside.Sum(p => p.Area).Should().BeApproximately(400, 1e-6);

        profiles.Should().OnlyContain(p => p.SourceEntityIds.Contains(circle.Id));
    }

    [Fact]
    public void QuadradoMordido_PintaSoDentroDaRegiao()
    {
        var sketch = Square(20);
        sketch.AddEntity(new CircleEntity(new Point3D(20, 10), 5));

        var bitten = new ProfileRecognitionService().RecognizeProfiles(sketch).OrderByDescending(p => p.Area).First();

        // O leque a partir do centro pintaria a mordida também (área do quadrado inteiro).
        PaintedArea(bitten).Should().BeApproximately(bitten.Area, 1e-6);
    }

    [Fact]
    public void CirculoNoCanto_DivideEmTresRegioes()
    {
        var sketch = Square(20);
        sketch.AddEntity(new CircleEntity(new Point3D(20, 20), 6));

        var profiles = new ProfileRecognitionService().RecognizeProfiles(sketch);

        profiles.Should().HaveCount(3);
        double quarter = Math.PI * 36 / 4;
        profiles.Select(p => p.Area).Min().Should().BeApproximately(quarter, quarter * 0.01);
        profiles.Max(p => p.Area).Should().BeApproximately(400 - quarter, 1);
    }

    [Fact]
    public void DoisCirculosQueSeCruzam_DaoTresRegioes()
    {
        var sketch = new Sketch();
        sketch.AddEntity(new CircleEntity(new Point3D(0, 0), 10));
        sketch.AddEntity(new CircleEntity(new Point3D(10, 0), 10));

        var profiles = new ProfileRecognitionService().RecognizeProfiles(sketch);

        profiles.Should().HaveCount(3);
        profiles.Should().OnlyContain(p => p is PolygonProfile);
        double disk = Math.PI * 100;
        profiles.Sum(p => p.Area).Should().BeApproximately(2 * disk - LensArea(10, 10), disk * 0.01);
    }

    private static double LensArea(double r, double d) =>
        2 * r * r * Math.Acos(d / (2 * r)) - d / 2 * Math.Sqrt(4 * r * r - d * d);

    [Fact]
    public void CirculoSoltoDentroDoQuadrado_ContinuaParametrico()
    {
        var sketch = Square(20);
        sketch.AddEntity(new CircleEntity(new Point3D(10, 10), 4));

        var profiles = new ProfileRecognitionService().RecognizeProfiles(sketch);

        profiles.Should().HaveCount(2);
        profiles.Should().ContainSingle(p => p is CircleProfile);
        profiles.Should().ContainSingle(p => p is RectangleProfile);
    }

    [Fact]
    public void CirculoTangenteAoLado_NaoDivideNada()
    {
        var sketch = Square(20);
        sketch.AddEntity(new CircleEntity(new Point3D(25, 10), 5));

        var profiles = new ProfileRecognitionService().RecognizeProfiles(sketch);

        profiles.Should().HaveCount(2);
        profiles.Should().ContainSingle(p => p is CircleProfile);
        profiles.Should().ContainSingle(p => p is RectangleProfile);
    }

    [Fact]
    public void LinhaQueSoEncostaNoMeioDeUmLado_DivideEmDoisRetangulos()
    {
        // Sem o arranjo do PlanarTopologyService: o lado de baixo e o de cima não estão quebrados em x = 10.
        var sketch = new Sketch();
        sketch.AddEntity(new LineEntity(new Point3D(0, 0), new Point3D(20, 0)));
        sketch.AddEntity(new LineEntity(new Point3D(20, 0), new Point3D(20, 10)));
        sketch.AddEntity(new LineEntity(new Point3D(20, 10), new Point3D(0, 10)));
        sketch.AddEntity(new LineEntity(new Point3D(0, 10), new Point3D(0, 0)));
        sketch.AddEntity(new LineEntity(new Point3D(10, 0), new Point3D(10, 10)));

        var profiles = new ProfileRecognitionService().RecognizeProfiles(sketch);

        profiles.Should().HaveCount(2);
        profiles.Should().OnlyContain(p => p is RectangleProfile);
        profiles.Should().OnlyContain(p => Math.Abs(p.Area - 100) < 1e-6);
    }

    [Fact]
    public void LinhaQuePassaDoCanto_NaoEstragaORetangulo()
    {
        var sketch = Square(20);
        sketch.AddEntity(new LineEntity(new Point3D(20, 20), new Point3D(30, 30)));

        var profiles = new ProfileRecognitionService().RecognizeProfiles(sketch);

        profiles.Should().ContainSingle().Which.Should().BeOfType<RectangleProfile>();
    }

    [Fact]
    public void RegiaoComArco_ExtrudaComoPrisma()
    {
        var sketch = Square(20);
        sketch.AddEntity(new CircleEntity(new Point3D(20, 10), 5));
        var bitten = new ProfileRecognitionService().RecognizeProfiles(sketch).OrderByDescending(p => p.Area).First();

        var code = FeatureCodeGenerator.GenerateExtrusionCode(bitten, 5, null);

        code.Should().Contain("new PrismFeature(");
    }

    [Fact]
    public void Triangulador_PoligonoEmL_CobreAAreaExata()
    {
        var l = new[]
        {
            new Point3D(0, 0), new Point3D(20, 0), new Point3D(20, 5),
            new Point3D(5, 5), new Point3D(5, 20), new Point3D(0, 20)
        };

        var horario = Enumerable.Reverse(l).ToArray();

        PaintedArea(new PolygonProfile(l)).Should().BeApproximately(175, 1e-9);
        PaintedArea(new PolygonProfile(horario)).Should().BeApproximately(175, 1e-9);
        PolygonTriangulator.Triangulate(l).Should().HaveCount(3 * 4);
    }
}
