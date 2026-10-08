using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Analysis;

public class RelationAnalyzerTests
{
    private static ModelRelations Analisar(params IFeature[] roots) =>
        RelationAnalyzer.Analyze(FactExtractor.Extract(roots.Select(r => new AnalysisSubject(r, Placement.Identity, "peca"))));

    private static CylinderFeature FuroY(string nome, double x, double z, double raio = 1.7) =>
        new(new Point3D(x, 20, z), new Vector3D(0, -1, 0), raio, 30) { Name = nome, IsTransientTool = true };

    private static CylinderFeature FuroZ(string nome, double x, double y, double raio = 1.7) =>
        new(new Point3D(x, y, -1), Vector3D.UnitZ, raio, 30) { Name = nome, IsTransientTool = true };

    private static BoxFeature Corpo(double largura = 100) => new(Point3D.Origin, largura, 16, 20) { Name = "corpo" };

    [Fact]
    public void FileiraDe4FurosComPasso20_ViraPadraoComPasso()
    {
        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(),
            FuroY("f1", 10, 5), FuroY("f2", 30, 5), FuroY("f3", 50, 5), FuroY("f4", 70, 5)));

        var padrao = r.LinearPatterns.Should().ContainSingle().Subject;
        padrao.Count.Should().Be(4);
        padrao.Pitch.Should().BeApproximately(20, 1e-9);
        padrao.AlongAxis.Should().Be(FrameAxis.X);
        padrao.CrossAxis.Should().Be(FrameAxis.Z);
        padrao.Members.Select(m => m.Name).Should().Equal("f1", "f2", "f3", "f4");
    }

    [Fact]
    public void PassosDiferentes_PadraoSemPasso()
    {
        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(),
            FuroY("f1", 10, 5), FuroY("f2", 30, 5), FuroY("f3", 55, 5)));

        var padrao = r.LinearPatterns.Should().ContainSingle().Subject;
        padrao.Pitch.Should().BeNull();
        padrao.Steps.Should().Equal(20, 25);
    }

    [Fact]
    public void DiametrosDiferentesNaMesmaAltura_NaoFormamOMesmoPadrao()
    {
        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(),
            FuroY("f1", 10, 5), FuroY("f2", 30, 5, raio: 3)));

        r.LinearPatterns.Should().BeEmpty();
    }

    [Fact]
    public void FuroERebaixoNoMesmoEixo_SaoCoaxiais()
    {
        var furo = FuroY("furo", 10, 5);
        var rebaixo = new CylinderFeature(new Point3D(10, 16, 5), new Vector3D(0, -1, 0), 3.6, 4) { Name = "rebaixo", IsTransientTool = true };

        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(), furo, rebaixo));

        r.CoaxialSets.Should().ContainSingle().Which.Members.Select(m => m.Name).Should().Equal("furo", "rebaixo");
    }

    [Fact]
    public void PecaEspelhada_PlanoDeSimetriaGlobal()
    {
        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(),
            FuroY("esq", 20, 5), FuroY("dir", 80, 5), FuroZ("cima_esq", 30, 8), FuroZ("cima_dir", 70, 8)));

        var plano = r.GlobalSymmetry(FrameAxis.X);
        plano.Should().NotBeNull();
        plano!.Coordinate.Should().BeApproximately(50, 1e-9);
        plano.Pairs.Should().HaveCount(2);
        plano.OnPlane.Select(f => f.Name).Should().Contain("corpo");
    }

    [Fact]
    public void ParSimetricoForaDoMeio_PlanoLocalNaoGlobal()
    {
        // Par de furos simétrico em torno de X = 30, mas o corpo vai de 0 a 100 e há um furo solto.
        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(),
            FuroY("a", 20, 5), FuroY("b", 40, 5), FuroZ("c", 20, 8), FuroZ("d", 40, 8), FuroY("solto", 90, 12)));

        var local = r.SymmetryPlanes.Should().ContainSingle(p => p.Normal == FrameAxis.X && System.Math.Abs(p.Coordinate - 30) < 1e-9).Subject;
        local.IsGlobal.Should().BeFalse();
        r.GlobalSymmetry(FrameAxis.X).Should().BeNull();
    }

    [Fact]
    public void FurosDeTamanhosDiferentes_NaoFormamParSimetrico()
    {
        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(),
            FuroY("a", 20, 5, raio: 1.7), FuroY("b", 80, 5, raio: 3)));

        r.SymmetryPlanes.Where(p => p.Normal == FrameAxis.X).SelectMany(p => p.Pairs)
            .Should().NotContain(p => p.A.IsRound);
        r.GlobalSymmetry(FrameAxis.X).Should().BeNull();
    }

    [Fact]
    public void FileiraDePassoConstanteForaDoMeio_SoOMeioDaFileiraESimetria()
    {
        // 5 furos de passo 20 (10..90) num corpo de 150: todo furo e todo meio de vão é centro de
        // algum par (30 = 10~50, 40 = 10~70 e 30~50...), mas só 50 espelha a fileira inteira.
        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(150),
            FuroZ("f1", 10, 8), FuroZ("f2", 30, 8), FuroZ("f3", 50, 8), FuroZ("f4", 70, 8), FuroZ("f5", 90, 8)));

        r.SymmetryPlanes.Where(p => p.Normal == FrameAxis.X).Select(p => p.Coordinate)
            .Should().Equal(50.0);
    }

    [Fact]
    public void FuroComRebaixo_SaoUmaUnidade_ENaoFormamParDeSimetriaComOutro()
    {
        // Dois furos com rebaixo espelhados em x = 30: dois pares de sólidos (furos e rebaixos), mas um
        // par só de posições — não basta para uma simetria local.
        var rebaixoA = new CylinderFeature(new Point3D(20, 8, 15), Vector3D.UnitZ, 3, 6) { Name = "rebaixo_a", IsTransientTool = true };
        var rebaixoB = new CylinderFeature(new Point3D(40, 8, 15), Vector3D.UnitZ, 3, 6) { Name = "rebaixo_b", IsTransientTool = true };
        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(), FuroZ("furo_a", 20, 8), rebaixoA, FuroZ("furo_b", 40, 8), rebaixoB));

        var furoA = r.Facts.Single(f => f.Name == "furo_a");
        r.Units.Of(furoA).Should().Be(r.Units.Of(r.Facts.Single(f => f.Name == "rebaixo_a")));
        r.Units.NameOf(furoA).Should().Be("furo_a+rebaixo_a");
        r.SymmetryPlanes.Should().NotContain(p => p.Normal == FrameAxis.X && System.Math.Abs(p.Coordinate - 30) < 1e-9);
    }

    [Fact]
    public void FuroVerticalEHorizontalNoMesmoX_CaemNoMesmoAlinhamentoX()
    {
        var r = Analisar(BooleanFeature.CreateDifference("bloco", Corpo(), FuroY("horizontal", 25, 5), FuroZ("vertical", 25, 8)));

        r.FrameClusters[FrameAxis.X].Tight
            .Should().Contain(c => System.Math.Abs(c.Coordinate - 25) < 1e-9 && c.Holes.Count == 2);
    }

    [Fact]
    public void ParedeQuaseAlinhada_ViraQuaseAlinhamento()
    {
        // Mesmo deslize de modelagem da chapa das guias: uma parede em 10,858 e outra em 11.
        var perfil = new PolygonProfile(new List<Point3D>
        {
            new(-11, -3, 0), new(11, -3, 0), new(11, 15, 0), new(10.858, 15, 0), new(10.858, 21, 0), new(-11, 21, 0)
        });
        var r = Analisar(new ExtrudeFeature(perfil, 8, Vector3D.UnitZ, "chapa"));

        r.FrameClusters[FrameAxis.X].NearMisses.Should().Contain(n =>
            n.Any(c => System.Math.Abs(c.Coordinate - 10.858) < 1e-6) && n.Any(c => System.Math.Abs(c.Coordinate - 11) < 1e-6));
    }
}
