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

/// <summary>
/// Os vértices de um contorno. Dois pontos seguidos com a mesma coordenada são uma aresta reta: a cota da
/// largura oferece primeiro os dois juntos, porque mudar um só entorta a aresta.
/// </summary>
public class LeverAlignedVerticesTests
{
    // Um "L": x = 30 nos pontos 1 e 2, y = 0 nos pontos 6 e 1 (o último é vizinho do primeiro).
    private const string Record = """
        public sealed record Chapa() : Part
        {
            protected override IFeature Build()
            {
                var perfil = new PolygonProfile(new System.Collections.Generic.List<Point3D>
                {
                    new Point3D(30, 0, 0),
                    new Point3D(30, 20, 0),
                    new Point3D(10, 20, 0),
                    new Point3D(10, 8, 0),
                    new Point3D(0, 8, 0),
                    new Point3D(0, 0, 0)
                });
                return new ExtrudeFeature(perfil, 5, new Vector3D(0, 0, 5), "corpo");
            }
        }
        """;

    private static readonly ScriptDocument[] Documents =
    {
        new(ScriptDocument.DefaultSceneFile, "Add(new Chapa(), name: \"p\");"),
        new("Pecas/Chapa.cs", Record)
    };

    private static async Task<(LeverStudy Study, DimensionGauge Largura)> Estudo()
    {
        var result = await new RoslynSceneEvaluator().EvaluateAsync(Documents);
        result.Success.Should().BeTrue(result.ErrorMessage);
        var peca = result.Features.Single(f => f.Name == "p");
        var sheet = DraftingAnalyzer.Analyze(new DraftingInput(
            [AnalysisSubject.For(peca, Placement.Identity)], StandardView.Top, new Point3D(0, 0, 0), new Point3D(30, 20, 5)))!;
        var largura = DimensionFactory.Linear(sheet.FindAnchor("peca.esq_inf")!, sheet.FindAnchor("peca.dir_sup")!, DimensionKind.Horizontal, -5);
        largura.Value.Should().BeApproximately(30, 1e-9);

        var study = LeverStudy.Run(new RoslynLeverProbeFactory().Create(Documents, "Chapa")!, "p", Placement.Identity)!;
        return (study, DimensionGauge.For(sheet, largura)!);
    }

    [Fact]
    public void Sonda_DizOndeCadaCoordenadaFicaNaLista_EORotuloDizOPonto()
    {
        using var probe = new RoslynLeverProbeFactory().Create(Documents, "Chapa")!;

        var xs = probe.Levers.Where(l => l.Vertex != null && l.Path == "x").OrderBy(l => l.Vertex!.Index).ToList();
        xs.Select(l => (l.Vertex!.Index, l.Vertex.Count, l.Vertex.Coordinate)).Should().Equal(
            (0, 6, "30"), (1, 6, "30"), (2, 6, "10"), (3, 6, "10"), (4, 6, "0"), (5, 6, "0"));
        xs.Select(l => l.Vertex!.ListId).Distinct().Should().ContainSingle();
        xs[0].Label.Should().Be("perfil · ponto 1 · x");
        xs[1].Label.Should().Be("perfil · ponto 2 · x", "sem o ponto, os dois teriam o mesmo rótulo");
    }

    [Fact]
    public async Task Largura_OfereceAArestaInteiraAntesDeUmPontoSo()
    {
        var (study, largura) = await Estudo();
        using var _ = study;

        var options = LeverOptionFinder.Find(study, largura.Measure).Where(o => o.IsDirect).ToList();

        var aresta = options[0];
        aresta.Shares.Select(s => (s.Lever.Label, s.Weight)).Should().Equal(
            ("perfil · ponto 1 · x", 1.0), ("perfil · ponto 2 · x", 1.0));
        aresta.Rate.Should().BeApproximately(1, 1e-6, "os dois juntos levam a borda 1 para 1: a taxa não é a soma das duas");
        aresta.Keeps.Should().ContainSingle().Which.Should().Be(new LeverRelation(LeverRelationKind.Aligned, "perfil · pontos 1 e 2", "x"));
        aresta.Breaks.Should().BeEmpty();

        var sozinhos = options.Where(o => !o.IsGroup).ToList();
        sozinhos.Select(o => o.Primary.Label).Should().BeEquivalentTo("perfil · ponto 1 · x", "perfil · ponto 2 · x");
        sozinhos.Should().OnlyContain(o => o.Breaks.Any(b => b.Kind == LeverRelationKind.Aligned), "um ponto só entorta a aresta");

        // A outra forma de mudar a largura é a borda esquerda (x = 0, pontos 5 e 6). Um ponto dela que anda
        // sozinho para dentro não muda a caixa da peça, porque o outro continua em x = 0: só a aresta
        // inteira aparece, e é por isso que o estudo a mede à parte.
        var esquerda = options.Where(o => o.IsGroup).Should().HaveCount(2, "cada aresta aparece uma vez, não uma por ponto").And.Subject.Last();
        esquerda.Keeps.Single().Fact.Should().Be("perfil · pontos 5 e 6");
        esquerda.Rate.Should().BeApproximately(-1, 1e-6);
    }

    [Fact]
    public async Task Aresta_ResolvidaEGravada_MudaOsDoisPontos()
    {
        var (study, largura) = await Estudo();
        using var _ = study;
        var aresta = LeverOptionFinder.Find(study, largura.Measure).First(o => o.IsGroup);

        var solution = LeverSolver.Solve(aresta, 36, study.Evaluate, largura.Measure)!;

        solution.Achieved.Should().BeApproximately(36, 1e-3);
        var text = LeverSourceEditor.Apply(Documents, solution.Changes)!.Single().Text;
        text.Should().Contain("new Point3D(36, 0, 0)").And.Contain("new Point3D(36, 20, 0)")
            .And.Contain("new Point3D(10, 20, 0)", "o resto do contorno não muda");
    }

    [Fact]
    public async Task UltimoEPrimeiroPonto_SaoVizinhos()
    {
        var (study, _) = await Estudo();
        using var __ = study;
        var result = await new RoslynSceneEvaluator().EvaluateAsync(Documents);
        var sheet = DraftingAnalyzer.Analyze(new DraftingInput(
            [AnalysisSubject.For(result.Features.Single(f => f.Name == "p"), Placement.Identity)],
            StandardView.Top, new Point3D(0, 0, 0), new Point3D(30, 20, 5)))!;
        var altura = DimensionGauge.For(sheet, DimensionFactory.Linear(
            sheet.FindAnchor("peca.esq_inf")!, sheet.FindAnchor("peca.dir_sup")!, DimensionKind.Vertical, -5))!;

        var grupos = LeverOptionFinder.Find(study, altura.Measure).Where(o => o.IsGroup).ToList();

        // y = 0 fica nos pontos 6 e 1; y = 20, nos pontos 2 e 3.
        grupos.SelectMany(o => o.Keeps).Select(r => r.Fact)
            .Should().BeEquivalentTo("perfil · pontos 6 e 1", "perfil · pontos 2 e 3");
    }
}

/// <summary>
/// A largura de uma peça simétrica. Uma aresta só tira o corpo do plano de simetria; a opção que vem
/// primeiro move as duas, em sentidos contrários, e a peça continua simétrica.
/// </summary>
public class LeverSymmetricWidthTests
{
    // Placa de 32 x 20 centrada em x = 0, com dois furos espelhados: o plano x = 0 é da peça inteira.
    private const string Contorno = """
        public sealed record Placa() : Part
        {
            protected override IFeature Build()
            {
                var perfil = new PolygonProfile(new System.Collections.Generic.List<Point3D>
                {
                    new Point3D(16, -10, 0),
                    new Point3D(16, 10, 0),
                    new Point3D(-16, 10, 0),
                    new Point3D(-16, -10, 0)
                });
                var corpo = new ExtrudeFeature(perfil, 5, new Vector3D(0, 0, 5), "corpo");
                var furo_dir = new CylinderFeature(new Point3D(FuroX, 0, 0), new Vector3D(0, 0, 1), 1.7, 5) { Name = "furo_dir", IsTransientTool = true };
                var furo_esq = new CylinderFeature(new Point3D(-10, 0, 0), new Vector3D(0, 0, 1), 1.7, 5) { Name = "furo_esq", IsTransientTool = true };
                return BooleanFeature.CreateDifference("p", corpo, furo_dir, furo_esq);
            }
        }
        """;

    private const string Caixa = """
        public sealed record Placa() : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(new Point3D(-16, -10, 0), 32, 20, 5) { Name = "corpo" };
                var furo_dir = new CylinderFeature(new Point3D(FuroX, 0, 0), new Vector3D(0, 0, 1), 1.7, 5) { Name = "furo_dir", IsTransientTool = true };
                var furo_esq = new CylinderFeature(new Point3D(-10, 0, 0), new Vector3D(0, 0, 1), 1.7, 5) { Name = "furo_esq", IsTransientTool = true };
                return BooleanFeature.CreateDifference("p", corpo, furo_dir, furo_esq);
            }
        }
        """;

    private static ScriptDocument[] Documents(string record, double furoX = 10) => new[]
    {
        new ScriptDocument(ScriptDocument.DefaultSceneFile, "Add(new Placa(), name: \"p\");"),
        new ScriptDocument("Pecas/Placa.cs", record.Replace("FuroX", furoX.ToString(System.Globalization.CultureInfo.InvariantCulture)))
    };

    private static async Task<(LeverStudy Study, DimensionGauge Largura)> Estudo(ScriptDocument[] documents)
    {
        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        result.Success.Should().BeTrue(result.ErrorMessage);
        var sheet = DraftingAnalyzer.Analyze(new DraftingInput(
            [AnalysisSubject.For(result.Features.Single(f => f.Name == "p"), Placement.Identity)],
            StandardView.Top, new Point3D(-16, -10, 0), new Point3D(16, 10, 5)))!;
        var largura = DimensionFactory.Linear(sheet.FindAnchor("peca.esq_inf")!, sheet.FindAnchor("peca.dir_sup")!, DimensionKind.Horizontal, -5);
        largura.Value.Should().BeApproximately(32, 1e-9);

        var study = LeverStudy.Run(new RoslynLeverProbeFactory().Create(documents, "Placa")!, "p", Placement.Identity)!;
        return (study, DimensionGauge.For(sheet, largura)!);
    }

    private static readonly LeverRelation Centrado = new(LeverRelationKind.Centered, "corpo", "x = 0", FrameAxis.X);

    [Fact]
    public async Task Contorno_ALarguraMoveAsDuasArestas_EOCorpoContinuaNoCentro()
    {
        var documents = Documents(Contorno);
        var (study, largura) = await Estudo(documents);
        using var _ = study;

        var options = LeverOptionFinder.Find(study, largura.Measure).Where(o => o.IsDirect).ToList();

        var simetrica = options[0];
        simetrica.Shares.Select(s => (s.Lever.Label, Math.Round(s.Weight, 6))).Should().BeEquivalentTo(new[]
        {
            ("perfil · ponto 1 · x", 1.0), ("perfil · ponto 2 · x", 1.0),
            ("perfil · ponto 3 · x", -1.0), ("perfil · ponto 4 · x", -1.0)
        });
        Math.Abs(simetrica.Rate).Should().BeApproximately(2, 1e-6, "cada aresta anda 1 para fora");
        simetrica.Keeps.Should().Contain(Centrado);
        simetrica.Keeps.Where(k => k.Kind == LeverRelationKind.Aligned).Select(k => k.Fact)
            .Should().BeEquivalentTo("perfil · pontos 1 e 2", "perfil · pontos 3 e 4");
        simetrica.Breaks.Should().BeEmpty();
        simetrica.Moved.Should().BeEmpty("o corpo só cresce, sem sair do lugar");
        simetrica.Resized.Should().Contain("corpo");

        options.Where(o => o.IsGroup && o.Shares.Count == 4).Should().ContainSingle("a esquerda com a direita é o mesmo movimento que a direita com a esquerda");

        var umLado = options.Where(o => o.Shares.Count == 2 && o.Keeps.Any(k => k.Kind == LeverRelationKind.Aligned)).ToList();
        umLado.Should().HaveCount(2, "cada aresta sozinha continua oferecida");
        umLado.Should().OnlyContain(o => o.Breaks.Contains(Centrado));

        var solution = LeverSolver.Solve(simetrica, 40, study.Evaluate, largura.Measure)!;
        solution.Achieved.Should().BeApproximately(40, 1e-3);
        LeverSourceEditor.Apply(documents, solution.Changes)!.Single().Text
            .Should().Contain("new Point3D(20, -10, 0)").And.Contain("new Point3D(20, 10, 0)")
            .And.Contain("new Point3D(-20, 10, 0)").And.Contain("new Point3D(-20, -10, 0)")
            .And.Contain("new Point3D(-10, 0, 0)", "os furos não mudam");
    }

    [Fact]
    public async Task Caixa_ALarguraCresceEAOrigemRecuaAMetade()
    {
        var documents = Documents(Caixa);
        var (study, largura) = await Estudo(documents);
        using var _ = study;

        var simetrica = LeverOptionFinder.Find(study, largura.Measure).Where(o => o.IsDirect).First();

        simetrica.Shares.Select(s => (s.Lever.Label, Math.Round(s.Weight, 6))).Should().Equal(
            ("corpo · width", 1.0), ("corpo · baseCorner.x", -0.5));
        simetrica.Rate.Should().BeApproximately(1, 1e-6);
        simetrica.Keeps.Should().Equal(Centrado);
        simetrica.Breaks.Should().BeEmpty();

        var solution = LeverSolver.Solve(simetrica, 40, study.Evaluate, largura.Measure)!;
        LeverSourceEditor.Apply(documents, solution.Changes)!.Single().Text
            .Should().Contain("new BoxFeature(new Point3D(-20, -10, 0), 40, 20, 5)");
    }

    // Dois rasgos espelhados em x = 0, cada um uma caixa de 8 de largura (de 6 a 14 e de -14 a -6).
    private const string Rasgos = """
        public sealed record Placa() : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(new Point3D(-16, -10, 0), 32, 20, 5) { Name = "corpo" };
                var rasgo_dir = new BoxFeature(new Point3D(6, -3, 0), 8, 6, 5) { Name = "rasgo_dir", IsTransientTool = true };
                var rasgo_esq = new BoxFeature(new Point3D(-14, -3, 0), 8, 6, 5) { Name = "rasgo_esq", IsTransientTool = true };
                return BooleanFeature.CreateDifference("p", corpo, rasgo_dir, rasgo_esq);
            }
        }
        """;

    private static double? LarguraDoRasgo(IReadOnlyList<PrimitiveFact> facts) =>
        facts.FirstOrDefault(f => f.Name == "rasgo_dir") is { } f ? f.BoundsMax.X - f.BoundsMin.X : null;

    [Fact]
    public void RasgoEspelhado_CresceComOOutro_QuePrecisaDaLarguraEDoCanto()
    {
        var documents = Documents(Rasgos);
        using var study = LeverStudy.Run(new RoslynLeverProbeFactory().Create(documents, "Placa")!, "p", Placement.Identity)!;

        var options = LeverOptionFinder.Find(study, LarguraDoRasgo).Where(o => o.IsDirect).ToList();

        var sozinho = options.Single(o => !o.IsGroup && o.Primary.Label == "rasgo_dir · width");
        sozinho.Breaks.Should().ContainSingle(r => r.Kind == LeverRelationKind.Symmetry && r.Fact == "rasgo_esq",
            "o outro rasgo continua com 8: os dois deixam de ser o espelho um do outro, mesmo sem o rasgo_dir sair do lugar dele");

        var espelhado = options[0];
        espelhado.Shares.Select(s => (s.Lever.Label, Math.Round(s.Weight, 6))).Should().BeEquivalentTo(new[]
        {
            ("rasgo_dir · width", 1.0), ("rasgo_esq · width", 1.0), ("rasgo_esq · baseCorner.x", -1.0)
        }, "o rasgo da esquerda cresce para a esquerda, como o da direita cresce para a direita");
        espelhado.Keeps.Should().ContainSingle(r => r.Kind == LeverRelationKind.Symmetry && r.Fact == "rasgo_esq");
        espelhado.Breaks.Should().BeEmpty();

        var solution = LeverSolver.Solve(espelhado, 10, study.Evaluate, LarguraDoRasgo)!;
        LeverSourceEditor.Apply(documents, solution.Changes)!.Single().Text
            .Should().Contain("new BoxFeature(new Point3D(6, -3, 0), 10, 6, 5)")
            .And.Contain("new BoxFeature(new Point3D(-16, -3, 0), 10, 6, 5)");
    }

    [Fact]
    public async Task PecaQueNaoEhSimetrica_AArestaNaoQuebraNada()
    {
        var (study, largura) = await Estudo(Documents(Contorno, furoX: 8));
        using var _ = study;

        var options = LeverOptionFinder.Find(study, largura.Measure);

        options.SelectMany(o => o.Breaks.Concat(o.Keeps)).Should().NotContain(r => r.Kind == LeverRelationKind.Centered);
        options.Should().NotContain(o => o.Shares.Count == 4);
    }
}
