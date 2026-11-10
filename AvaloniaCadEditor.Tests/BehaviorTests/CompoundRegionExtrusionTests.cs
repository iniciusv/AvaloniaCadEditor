using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Regiões vizinhas marcadas com Ctrl viram UM sólido do contorno da união (Box se for retângulo, um
/// Prism se não), e não um sólido por região unidos por booleana.
/// </summary>
public class CompoundRegionExtrusionTests
{
    private static List<IProfile> Regions(params (double X1, double Y1, double X2, double Y2)[] lines) =>
        Regions(Array.Empty<CircleEntity>(), lines);

    private static List<IProfile> Regions(CircleEntity[] circles, params (double X1, double Y1, double X2, double Y2)[] lines)
    {
        var sketch = new Sketch();
        foreach (var (x1, y1, x2, y2) in lines)
            sketch.AddEntity(new LineEntity(new Point3D(x1, y1), new Point3D(x2, y2)));
        foreach (var c in circles) sketch.AddEntity(c);
        return new ProfileRecognitionService().RecognizeProfiles(sketch).ToList();
    }

    private static readonly (double, double, double, double)[] Square20 =
        { (0, 0, 20, 0), (20, 0, 20, 20), (20, 20, 0, 20), (0, 20, 0, 0) };

    private static int Count(string code, string text) => Regex.Matches(code, Regex.Escape(text)).Count;

    private static string Extrude(IEnumerable<IProfile> regions) =>
        FeatureCodeGenerator.GenerateExtrusionCode(new CompoundProfile(regions), 5, new Sketch());

    [Fact]
    public void MetadesDeUmQuadrado_ViramUmBoxSo()
    {
        var halves = Regions(Square20.Append((10, 0, 10, 20)).ToArray());
        halves.Should().HaveCount(2);

        var code = Extrude(halves);

        Count(code, "new BoxFeature(").Should().Be(1);
        code.Should().Contain("width: 20, length: 20, height: 5");
        code.Should().NotContain("CreateUnion").And.NotContain("PrismFeature");
    }

    [Fact]
    public void QuadradoMordidoMaisOMeioDiscoDeDentro_VoltaASerOQuadrado()
    {
        var regions = Regions(new[] { new CircleEntity(new Point3D(20, 10), 5) }, Square20);
        var inside = regions.Where(r => r.Center.X < 20).ToList();
        inside.Should().HaveCount(2);

        var code = Extrude(inside);

        Count(code, "new BoxFeature(").Should().Be(1);
        code.Should().Contain("width: 20, length: 20, height: 5");
        code.Should().NotContain("CreateUnion");
    }

    [Fact]
    public void AsTresRegioesDoQuadradoComCirculo_ViramUmPrismaSo()
    {
        var regions = Regions(new[] { new CircleEntity(new Point3D(20, 10), 5) }, Square20);
        regions.Should().HaveCount(3);

        var merged = SketchRegionMerger.MergeAdjacent(regions);

        double halfDisk = regions.Single(r => r.Center.X > 20).Area;
        merged.Should().ContainSingle().Which.Area.Should().BeApproximately(400 + halfDisk, 1e-6);
        var code = Extrude(regions);
        Count(code, "new PrismFeature(").Should().Be(1);
        code.Should().NotContain("CreateUnion");
    }

    [Fact]
    public void QuadradoMordidoMaisOMeioDiscoDeFora_SoSeTocamEmDoisPontos_NaoSaoJuntados()
    {
        // O meio disco de dentro fica entre os dois: a união cercaria um buraco.
        var regions = Regions(new[] { new CircleEntity(new Point3D(20, 10), 5) }, Square20);
        var bitten = regions.OrderByDescending(r => r.Area).First();
        var outside = regions.Single(r => r.Center.X > 20);

        SketchRegionMerger.MergeAdjacent(new[] { bitten, outside }).Should().HaveCount(2);
    }

    [Fact]
    public void RegioesEmL_ComDivisaEmT_ViramUmPrismaComOVolumeCerto()
    {
        // A metade esquerda tem o lado direito inteiro (x = 10, de 0 a 20); a de baixo à direita só encosta
        // na metade dele. A divisa tem que se cancelar mesmo assim.
        var regions = Regions(Square20.Append((10, 0, 10, 20)).Append((10, 10, 20, 10)).ToArray());
        regions.Should().HaveCount(3);
        var left = regions.Single(r => r.Center.X < 10);
        var lowerRight = regions.Single(r => r.Center.X > 10 && r.Center.Y < 10);

        var merged = SketchRegionMerger.MergeAdjacent(new[] { left, lowerRight });

        var l = merged.Should().ContainSingle().Which.Should().BeOfType<PolygonProfile>().Subject;
        l.Vertices.Should().HaveCount(6);
        l.Area.Should().BeApproximately(300, 1e-6);

        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(new PrismFeature(l.Vertices, 5));
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(1500, 1e-3);
    }

    [Fact]
    public void RegioesSeparadas_ContinuamComoUniao()
    {
        var a = new RectangleProfile(new Point3D(0, 0, 0), 10, 10);
        var b = new RectangleProfile(new Point3D(50, 0, 0), 10, 10);

        var code = Extrude(new IProfile[] { a, b });

        Count(code, "new BoxFeature(").Should().Be(2);
        code.Should().Contain("CreateUnion");
    }

    [Fact]
    public void AnelEmVoltaDeUmBuraco_NaoEJuntado()
    {
        // Grade 3×3 marcada sem a do meio: a união tem buraco, e um prisma não tem.
        var lines = new List<(double, double, double, double)>();
        for (int i = 0; i <= 3; i++)
        {
            lines.Add((i * 10, 0, i * 10, 30));
            lines.Add((0, i * 10, 30, i * 10));
        }
        var cells = Regions(lines.ToArray());
        cells.Should().HaveCount(9);
        var ring = cells.Where(c => c.Center.DistanceTo(new Point3D(15, 15)) > 1).ToList();

        SketchRegionMerger.MergeAdjacent(ring).Should().HaveCount(8);
        Extrude(ring).Should().Contain("CreateUnion");
    }

    [Fact]
    public void RegiaoMaisCirculoSoltoDentro_NaoMexeNoCirculo()
    {
        var regions = Regions(new[] { new CircleEntity(new Point3D(10, 10), 3) }, Square20);

        SketchRegionMerger.MergeAdjacent(regions).Should().HaveCount(2);
    }

    [Fact]
    public void Previa_DeMetadesDeUmQuadrado_EOQuadradoJuntado()
    {
        var halves = Regions(Square20.Append((10, 0, 10, 20)).ToArray());
        var cmd = new AvaloniaCadEditor.Application.Commands.ExtrudeCommand();
        cmd.StartDirectExtrusion(new Sketch(), new CompoundProfile(halves), Point3D.Origin);

        var preview = (ExtrudeFeature)cmd.GetPreviewFeature()!;

        preview.Profile.Should().BeOfType<RectangleProfile>().Which.Area.Should().BeApproximately(400, 1e-6);
        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(preview);
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(400 * 20, 1e-3);
    }

    [Fact]
    public void Previa_DeRegioesSeparadas_NaoLigaUmaRegiaoAOutra()
    {
        // CompoundProfile.GetVertices põe os vértices das duas em fila: um contorno só feito deles
        // ligaria um retângulo ao outro, no sólido e nas linhas da prévia.
        var a = new RectangleProfile(new Point3D(0, 0, 0), 10, 10);
        var b = new RectangleProfile(new Point3D(50, 0, 0), 10, 10);
        var cmd = new AvaloniaCadEditor.Application.Commands.ExtrudeCommand();
        cmd.StartDirectExtrusion(new Sketch(), new CompoundProfile(new IProfile[] { a, b }), Point3D.Origin);

        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(cmd.GetPreviewFeature()!);
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(2 * 100 * 20, 1e-3);

        var lines = cmd.GetPreviewEntities().OfType<LineEntity>().ToList();
        lines.Should().NotBeEmpty();
        lines.Should().OnlyContain(l => Math.Abs(l.StartPoint.X - l.EndPoint.X) <= 10 + 1e-9,
            "nenhuma linha atravessa o vão entre as duas regiões");
    }

    [Fact]
    public void MetadesDeUmQuadrado_ServemDeSecaoDoSweep()
    {
        var halves = Regions(Square20.Append((10, 0, 10, 20)).ToArray());

        var section = FeatureCodeGenerator.SweepSectionFromProfile(new CompoundProfile(halves));

        section.Error.Should().BeNull();
        section.Expression.Should().Be("SweepSection.Rectangle(20, 20)");
    }
}
