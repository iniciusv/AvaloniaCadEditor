using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.ReferenceGuides;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ReferenceGuides;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.ReferenceGuides;

public class ReferenceGuideServiceTests
{
    /// <summary>
    /// Reproduces the "chapa com furos" scene used to motivate this feature: a rounded
    /// rectangular plate with a row of small holes on each side and a large hole centered
    /// on each rounded end, plus a deliberate 10.858-vs-11 modeling slip on the right wall.
    /// </summary>
    private static IReadOnlyList<IFeature> BuildDrilledPlateScene()
    {
        var poly1 = new PolygonProfile(new List<Point3D>
        {
            new(-1.142, -9, -4), new(-1.142, -3, -4), new(-11, -3, -4), new(-11, 15, -4),
            new(-1.142, 15, -4), new(-1.142, 21, -4), new(-0.938, 22.553, -4), new(-0.338, 24, -4),
            new(0.615, 25.243, -4), new(1.858, 26.196, -4), new(3.305, 26.796, -4), new(4.858, 27, -4),
            new(6.411, 26.796, -4), new(7.858, 26.196, -4), new(9.101, 25.243, -4), new(10.054, 24, -4),
            new(10.653, 22.553, -4), new(10.858, 21, -4), new(10.858, 15, -4), new(11, -3, -4),
            new(10.858, -9, -4), new(10.653, -10.553, -4), new(10.054, -12, -4), new(9.101, -13.243, -4),
            new(7.858, -14.196, -4), new(6.411, -14.796, -4), new(4.858, -15, -4), new(3.305, -14.796, -4),
            new(1.858, -14.196, -4), new(0.615, -13.243, -4), new(-0.338, -12, -4), new(-0.938, -10.553, -4)
        });
        var extrude = new ExtrudeFeature(poly1, 8, new Vector3D(0, 0, 8), "Extrusão do Contorno Base");

        var cyl3 = new CylinderFeature(new Point3D(4.858, -9, -5), new Vector3D(0, 0, 1), 3.1, 10, "Furo 1") { IsTransientTool = true };
        var cyl4 = new CylinderFeature(new Point3D(-6.5, 1, -5), new Vector3D(0, 0, 1), 1.7, 10, "Furo 2") { IsTransientTool = true };
        var cyl5 = new CylinderFeature(new Point3D(6.5, 11, -5), new Vector3D(0, 0, 1), 1.7, 10, "Furo 3") { IsTransientTool = true };
        var cyl6 = new CylinderFeature(new Point3D(6.5, 1, -5), new Vector3D(0, 0, 1), 1.7, 10, "Furo 4") { IsTransientTool = true };
        var cyl7 = new CylinderFeature(new Point3D(-6.5, 11, -5), new Vector3D(0, 0, 1), 1.7, 10, "Furo 5") { IsTransientTool = true };
        var cyl8 = new CylinderFeature(new Point3D(4.858, 21, -5), new Vector3D(0, 0, 1), 3.1, 10, "Furo 6") { IsTransientTool = true };

        var boolOp = BooleanFeature.CreateDifference("Base com Furos", extrude, cyl3, cyl4, cyl5, cyl6, cyl7, cyl8);

        // Mirrors the real script: every feature is added independently, and the boolean too.
        return new List<IFeature> { extrude, cyl3, cyl4, cyl5, cyl6, cyl7, cyl8, boolOp };
    }

    [Fact]
    public void Analyze_FurosAlinhados_DeveGerarLinhasDeFurosEmYComDoisFurosCada()
    {
        var result = new ReferenceGuideService().Analyze(BuildDrilledPlateScene());

        var holeRows = result.Lines.Where(l => l.Kind == GuideKind.HoleRow).ToList();

        holeRows.Should().Contain(l => Math.Abs(l.Coordinate - 1) < 1e-2 && l.SourceFeatureIds.Count == 2);
        holeRows.Should().Contain(l => Math.Abs(l.Coordinate - 11) < 1e-2 && l.SourceFeatureIds.Count == 2);
    }

    [Fact]
    public void Analyze_FurosAlinhados_DeveGerarLinhasDeFurosEmXIgualAMaisOuMenos6E5()
    {
        var result = new ReferenceGuideService().Analyze(BuildDrilledPlateScene());

        var holeRows = result.Lines.Where(l => l.Kind == GuideKind.HoleRow).ToList();

        holeRows.Should().Contain(l => Math.Abs(l.Coordinate - 6.5) < 1e-2);
        holeRows.Should().Contain(l => Math.Abs(l.Coordinate + 6.5) < 1e-2);
    }

    [Fact]
    public void Analyze_PassoConstanteEntreFileiras_DeveGerarRotuloDeTresVezesDez()
    {
        var result = new ReferenceGuideService().Analyze(BuildDrilledPlateScene());

        result.Dimensions.Should().Contain(d => d.Kind == GuideKind.HoleRow && d.Label == "3× 10");
    }

    [Fact]
    public void Analyze_ParesSimetricos_DeveGerarLinhaDeSimetriaEmXIgualAZero()
    {
        var result = new ReferenceGuideService().Analyze(BuildDrilledPlateScene());

        result.Lines.Should().Contain(l => l.Kind == GuideKind.Symmetry && Math.Abs(l.Coordinate) < 1e-2);
    }

    [Fact]
    public void Analyze_ParedesRetasDoPerfil_DeveGerarLinhasDeParedeEmX()
    {
        var result = new ReferenceGuideService().Analyze(BuildDrilledPlateScene());

        var wallLines = result.Lines.Where(l => l.Kind == GuideKind.Wall).ToList();

        wallLines.Should().Contain(l => Math.Abs(l.Coordinate + 11) < 1e-2);
        wallLines.Should().Contain(l => Math.Abs(l.Coordinate + 1.142) < 1e-2);
    }

    [Fact]
    public void Analyze_ExtremidadesArredondadas_DeveDetectarCentroDeArcoCoincidindoComFuroGrande()
    {
        var result = new ReferenceGuideService().Analyze(BuildDrilledPlateScene());

        var arcCenters = result.Lines.Where(l => l.Kind == GuideKind.ArcCenter).ToList();
        arcCenters.Should().NotBeEmpty();

        // O centro do arco superior deve coincidir com o furo grande em (4.858, 21).
        var arcXCoords = arcCenters.Select(l => l.Coordinate);
        arcXCoords.Should().Contain(c => Math.Abs(c - 4.858) < 0.05 || Math.Abs(c - 21) < 0.05);
    }

    [Fact]
    public void Analyze_ParedeComPequenoDesalinhamento_DeveSinalizarQuaseAlinhamentoEntre10E858E11()
    {
        var result = new ReferenceGuideService().Analyze(BuildDrilledPlateScene());

        result.Dimensions.Should().Contain(d =>
            d.Kind == GuideKind.NearMiss &&
            Math.Abs(d.Value - 0.142) < 1e-2 &&
            d.Label.StartsWith("X:") && d.Label.Contains("10.858") && d.Label.Contains("11"));
    }

    [Fact]
    public void Analyze_ParedeComPequenoDesalinhamento_AsDuasLinhasEnvolvidasDevemFicarVermelhas()
    {
        var result = new ReferenceGuideService().Analyze(BuildDrilledPlateScene());

        // O usuário reportou que era difícil enxergar o que estava em conflito: a única pista
        // era um traço vermelho de 0.142 de comprimento. Agora as duas linhas conflitantes
        // (10.858 e 11 no eixo X - uma linha "vertical", com Start.X ≈ End.X) devem aparecer
        // inteiras e em vermelho, não em laranja (Wall) normal. Restrito ao eixo X porque
        // Y=11 é, coincidentemente, uma fileira de furos legítima nesta cena.
        result.Lines.Should().NotContain(l =>
            (l.Kind == GuideKind.Wall || l.Kind == GuideKind.HoleRow) &&
            (IsVerticalLineAt(l, 10.858) || IsVerticalLineAt(l, 11)));

        result.Lines.Should().Contain(l => l.Kind == GuideKind.NearMiss && IsVerticalLineAt(l, 10.858));
        result.Lines.Should().Contain(l => l.Kind == GuideKind.NearMiss && IsVerticalLineAt(l, 11));
    }

    private static bool IsVerticalLineAt(GuideLine line, double x) =>
        Math.Abs(line.Start.X - line.End.X) < 1e-6 && Math.Abs(line.Coordinate - x) < 1e-2;

    [Fact]
    public void Analyze_CentrosDosDoisArcosArredondados_NaoDevemGerarFalsoQuaseAlinhamento()
    {
        // Reproduz o relato do usuário: analisando só a extrusão (sem furos), os centros dos
        // arcos do topo (Y=21) e da base (Y=-9) deveriam computar para o mesmo X=4.858. Antes
        // do ajuste de círculo por mínimos quadrados, ruído numérico do cálculo do centro do
        // arco fazia os dois caírem em 4.857/4.858 e virar um falso "quase-alinhamento".
        var poly1 = new PolygonProfile(new List<Point3D>
        {
            new(-1.142, -9, -4), new(-1.142, -3, -4), new(-11, -3, -4), new(-11, 15, -4),
            new(-1.142, 15, -4), new(-1.142, 21, -4), new(-0.938, 22.553, -4), new(-0.338, 24, -4),
            new(0.615, 25.243, -4), new(1.858, 26.196, -4), new(3.305, 26.796, -4), new(4.858, 27, -4),
            new(6.411, 26.796, -4), new(7.858, 26.196, -4), new(9.101, 25.243, -4), new(10.054, 24, -4),
            new(10.653, 22.553, -4), new(10.858, 21, -4), new(10.858, 15, -4), new(11, -3, -4),
            new(10.858, -9, -4), new(10.653, -10.553, -4), new(10.054, -12, -4), new(9.101, -13.243, -4),
            new(7.858, -14.196, -4), new(6.411, -14.796, -4), new(4.858, -15, -4), new(3.305, -14.796, -4),
            new(1.858, -14.196, -4), new(0.615, -13.243, -4), new(-0.338, -12, -4), new(-0.938, -10.553, -4)
        });
        var extrude = new ExtrudeFeature(poly1, 8, new Vector3D(0, 0, 8), "Extrusão do Contorno Base");

        var result = new ReferenceGuideService().Analyze(new List<IFeature> { extrude });

        result.Lines.Should().NotContain(l => l.Kind == GuideKind.NearMiss && Math.Abs(l.Coordinate - 4.858) < 0.05);
        result.Dimensions.Should().NotContain(d => d.Kind == GuideKind.NearMiss && d.Label.Contains("4.85"));

        var arcCenters = result.Lines.Where(l => l.Kind == GuideKind.ArcCenter).Select(l => l.Coordinate).ToList();
        arcCenters.Should().Contain(c => Math.Abs(c - 4.858) < 0.01);
    }

    [Fact]
    public void Analyze_CenaSemFuros_NaoDeveGerarLinhasDeFileiraDeFuros()
    {
        var poly = new PolygonProfile(new List<Point3D>
        {
            new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)
        });
        var extrude = new ExtrudeFeature(poly, 5, new Vector3D(0, 0, 5), "Placa sem furos");

        var result = new ReferenceGuideService().Analyze(new List<IFeature> { extrude });

        result.Lines.Should().NotContain(l => l.Kind == GuideKind.HoleRow);
    }

    [Fact]
    public void Analyze_CenaVazia_DeveRetornarConjuntoVazio()
    {
        var result = new ReferenceGuideService().Analyze(Array.Empty<IFeature>());

        result.Should().BeSameAs(ReferenceGuideSet.Empty);
    }
}
