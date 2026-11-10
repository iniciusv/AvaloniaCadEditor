using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Levers;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Drafting;

/// <summary>
/// Que números uma cota oferece primeiro. Uma medida (posição, comprimento, raio, com nome ou sem) entra na
/// lista padrão; o divisor de uma conta, o componente de uma direção e o multiplicador (o sinal de um
/// espelho) também mudam a cota, mas só aparecem como indiretas: gravar por eles deixa o código pior
/// (<c>passo / 1.87</c>, um espelho de <c>1.11</c>).
/// </summary>
public class LeverMeasureTests
{
    private const string Record = """
        public sealed record Espelhada(bool Espelhar = false, double Passo = 20) : Part
        {
            private const double Espessura = 5;

            protected override IFeature Build()
            {
                const double raioFuro = 1.5;
                var sx = Espelhar ? -1.0 : 1.0;
                var corpo = new BoxFeature(new Point3D(-24, 0, 0), 48, 10, Espessura) { Name = "corpo" };
                var furo = new CylinderFeature(new Point3D(Passo / 2 * sx, 5, 0), new Vector3D(0, 0, 1), raioFuro, Espessura) { Name = "furo", IsTransientTool = true };
                return BooleanFeature.CreateDifference("p", corpo, furo);
            }
        }
        """;

    private static ScriptDocument[] Documents(string record = Record, string scene = "Add(new Espelhada(), name: \"p\");") => new[]
    {
        new ScriptDocument(ScriptDocument.DefaultSceneFile, scene),
        new ScriptDocument("Pecas/Peca.cs", record)
    };

    private static ILeverProbe Probe(ScriptDocument[] documents, string type = "Espelhada")
    {
        var probe = new RoslynLeverProbeFactory().Create(documents, type);
        probe.Should().NotBeNull("a cena instrumentada precisa compilar");
        return probe!;
    }

    private static double Height(ILeverProbe probe, string fact, IReadOnlyDictionary<int, double> values)
    {
        var subject = probe.Run(values)!.Single(f => f.Name == "p");
        var found = FactExtractor.Extract([AnalysisSubject.For(subject, Placement.Identity)]).Single(f => f.Name == fact);
        return found.BoundsMax.Z - found.BoundsMin.Z;
    }

    // ------------------------------------------------------------------ papel de cada número

    [Fact]
    public void Sonda_DizOPapelDeCadaNumero()
    {
        using var probe = Probe(Documents());

        probe.Levers.Single(l => l.Owner == "furo" && l.Text == "2").Role
            .Should().Be(CodeLeverRole.Factor, "é o divisor de Passo / 2");
        probe.Levers.Where(l => l.Path.StartsWith("globalDirection", StringComparison.Ordinal)).Select(l => l.Role)
            .Should().HaveCount(3).And.OnlyContain(r => r == CodeLeverRole.Direction, "são os componentes do eixo do furo");
        probe.Levers.Single(l => l.Owner == "corpo" && l.Text == "-24").Role.Should().Be(CodeLeverRole.Measure);
        probe.Levers.Single(l => l.Label == "raioFuro").Role.Should().Be(CodeLeverRole.Measure);
        probe.Levers.Where(l => l.Owner == "sx").Select(l => l.Role)
            .Should().OnlyContain(r => r == CodeLeverRole.Measure, "a sintaxe não distingue um sinal de uma medida: quem acusa é a resposta da peça");
    }

    [Fact]
    public void DeslocamentoEmVector3D_EhMedida_NaoDirecao()
    {
        const string record = """
            public sealed record Movida() : Part
            {
                protected override IFeature Build()
                {
                    var corpo = new BoxFeature(new Point3D(0, 0, 0), 10, 10, 10) { Name = "corpo" };
                    return corpo.Transformed(Placement.Translate(new Vector3D(7, 0, 0)), "p");
                }
            }
            """;
        using var probe = Probe(Documents(record, "Add(new Movida(), name: \"p\");"), "Movida");

        probe.Levers.Single(l => l.Text == "7").Role.Should().Be(CodeLeverRole.Measure, "o offset de um Translate é um deslocamento em mm");
    }

    // ------------------------------------------------------------------ opções

    private static async Task<(LeverStudy Study, DimensionGauge Gauge)> BordaAoFuro(ScriptDocument[] documents)
    {
        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        result.Success.Should().BeTrue(result.ErrorMessage);
        var peca = result.Features.Single(f => f.Name == "p");
        var sheet = DraftingAnalyzer.Analyze(new DraftingInput(
            [AnalysisSubject.For(peca, Placement.Identity)], StandardView.Top, new Point3D(-24, 0, 0), new Point3D(24, 10, 5)))!;
        var dimension = DimensionFactory.Linear(sheet.FindAnchor("corpo.esq_inf")!, sheet.FindAnchor("furo.centro")!, DimensionKind.Horizontal, -5);
        dimension.Value.Should().BeApproximately(34, 1e-9, "da borda esquerda (x = -24) ao centro do furo (x = 10)");

        var study = LeverStudy.Run(Probe(documents), "p", Placement.Identity);
        study.Should().NotBeNull();
        return (study!, DimensionGauge.For(sheet, dimension)!);
    }

    [Fact]
    public async Task Opcoes_AsMedidasVemPrimeiro_FatorSinalEDirecaoFicamComoIndiretas()
    {
        var (study, gauge) = await BordaAoFuro(Documents());
        using var _ = study;

        var options = LeverOptionFinder.Find(study, gauge.Measure).ToList();

        var diretas = options.Where(o => o.IsDirect).ToList();
        diretas.Select(o => (o.Primary.Owner, o.Primary.Text)).Should().BeEquivalentTo(new[] { ("corpo", "-24"), ("Passo", "20") });
        diretas.Single(o => o.Primary.Owner == "Passo").Rate.Should().BeApproximately(0.5, 1e-6, "o furo fica em Passo / 2");

        options.Single(o => o.Primary.Text == "2").Caveat.Should().Be(LeverCaveat.Factor);
        var sinal = options.Single(o => o.Primary.Owner == "sx");
        sinal.Caveat.Should().Be(LeverCaveat.Scaled, "1.0 não é divisor nem direção, mas a peça não responde como a uma medida");
        sinal.Gain.Should().BeApproximately(10, 1e-3, "1 a mais no sinal leva o furo de x = 10 para x = 20");

        options.Take(diretas.Count).Should().OnlyContain(o => o.IsDirect, "as medidas vêm antes das indiretas");
        options.Where(o => !o.IsDirect).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Diametro_PuxaOConstDoRaio()
    {
        var documents = Documents();
        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        var peca = result.Features.Single(f => f.Name == "p");
        var sheet = DraftingAnalyzer.Analyze(new DraftingInput(
            [AnalysisSubject.For(peca, Placement.Identity)], StandardView.Top, new Point3D(-24, 0, 0), new Point3D(24, 10, 5)))!;
        var gauge = DimensionGauge.For(sheet, DimensionFactory.Diameter(sheet.Entities.OfType<ViewCircle>().Single(c => c.SourceName == "furo")))!;
        using var study = LeverStudy.Run(Probe(documents), "p", Placement.Identity)!;

        var option = LeverOptionFinder.Find(study, gauge.Measure).Where(o => o.IsDirect).Should().ContainSingle().Subject;

        option.Primary.Label.Should().Be("raioFuro");
        option.Rate.Should().BeApproximately(2, 1e-6);

        var solution = LeverSolver.Solve(option, 4, study.Evaluate, gauge.Measure)!;
        LeverSourceEditor.Apply(documents, solution.Changes)!.Single().Text
            .Should().Contain("const double raioFuro = 2.0;", "no fonte de verdade o const continua const: só o número muda");
    }

    // ------------------------------------------------------------------ const

    [Fact]
    public void Const_LocalEDeCampo_SaoNumerosQueASondaTroca()
    {
        using var probe = Probe(Documents());

        var raio = probe.Levers.Single(l => l.Label == "raioFuro");
        raio.Should().Match<CodeLever>(l => l.Kind == CodeLeverKind.Literal && l.Value == 1.5 && l.Text == "1.5");
        var espessura = probe.Levers.Single(l => l.Label == "Espessura");
        espessura.Value.Should().Be(5);

        // O campo vira propriedade na cópia: cada execução lê o valor de novo. Um campo estático guardaria o
        // da primeira execução e ignoraria as trocas seguintes.
        Height(probe, "corpo", new Dictionary<int, double>()).Should().BeApproximately(5, 1e-9);
        Height(probe, "corpo", new Dictionary<int, double> { [espessura.Id] = 8 }).Should().BeApproximately(8, 1e-9);
        Height(probe, "corpo", new Dictionary<int, double> { [espessura.Id] = 6 }).Should().BeApproximately(6, 1e-9);
        Height(probe, "corpo", new Dictionary<int, double>()).Should().BeApproximately(5, 1e-9);
    }

    [Fact]
    public void ConstUsadoOndeOCSharpExigeConstante_ContinuaConst_EOQueEntraNaContaDeleTambem()
    {
        const string record = """
            public sealed record Grade(double Passo = 10) : Part
            {
                protected override IFeature Build()
                {
                    const double limite = 8;          // num padrão de switch: tem de ser constante
                    const double folga = 0.25;        // livre
                    const double a = 3;               // entra na conta de um const int, que continua const
                    const int lados = (int)a * 2;
                    double altura = Passo switch { limite => 1.5, _ => 2.5 };
                    return new CylinderFeature(new Point3D(0, 0, 0), new Vector3D(0, 0, 1), a + folga, altura * lados) { Name = "p" };
                }
            }
            """;
        using var probe = Probe(Documents(record, "Add(new Grade(), name: \"g\");"), "Grade");

        probe.Levers.Select(l => l.Owner).Should().Contain("folga")
            .And.NotContain("limite", "é usado como padrão constante")
            .And.NotContain("a", "lados é const int e depende dele")
            .And.NotContain("lados", "é int");
        probe.Run(new Dictionary<int, double>()).Should().ContainSingle(f => f.Name == "g");
    }

    [Fact]
    public void ConstQueAAnaliseNaoPreviu_ASondaExisteSemEle()
    {
        // Função local estática lê const, mas não lê variável: tirar o const de "folga" não compila. A análise
        // não conhece esse caso, e a sonda tenta de novo com todo const como está.
        const string record = """
            public sealed record Anel() : Part
            {
                protected override IFeature Build()
                {
                    const double folga = 0.25;
                    static double ComFolga(double x) => x + folga;
                    return new CylinderFeature(new Point3D(0, 0, 0), new Vector3D(0, 0, 1), ComFolga(3), 12) { Name = "p" };
                }
            }
            """;
        using var probe = Probe(Documents(record, "Add(new Anel(), name: \"g\");"), "Anel");

        probe.Levers.Select(l => l.Owner).Should().NotContain("folga");
        probe.Levers.Should().Contain(l => l.Text == "12" && l.Path == "height");
        probe.Run(new Dictionary<int, double>()).Should().ContainSingle(f => f.Name == "g");
    }

    [Fact]
    public void SemTirarOConst_OsLiteraisDeleFicamDeFora()
    {
        var documents = Documents();
        var compilation = RoslynSceneEvaluator.CreateCompilation(documents, out var trees);

        var com = RoslynLeverInstrumenter.Instrument(compilation, trees, "Espelhada")!;
        var sem = RoslynLeverInstrumenter.Instrument(compilation, trees, "Espelhada", freeConstants: false)!;

        com.FreedConstants.Should().Be(2);
        com.Levers.Select(l => l.Label).Should().Contain(new[] { "raioFuro", "Espessura" });
        com.Compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        sem.FreedConstants.Should().Be(0);
        sem.Levers.Select(l => l.Label).Should().NotContain("raioFuro").And.NotContain("Espessura");
    }
}
