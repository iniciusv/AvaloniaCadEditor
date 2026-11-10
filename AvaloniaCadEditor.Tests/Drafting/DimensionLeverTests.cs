using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Levers;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Drafting;

/// <summary>
/// Ponta a ponta: a cota do desenho, a sonda que troca os números do código, as opções e a gravação.
/// A peça é escrita no estilo literal que a IA gera por padrão: posições literais e um parâmetro só.
/// </summary>
public class DimensionLeverTests
{
    private const string BlocoRecord = """
        public sealed record Bloco(double Largura = 40) : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(new Point3D(0, 0, 0), Largura, 16, 17) { Name = "corpo" };
                var furo_a = new CylinderFeature(new Point3D(10, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_a", IsTransientTool = true };
                var porca_a = new CylinderFeature(new Point3D(10, 16, 5), new Vector3D(0, -1, 0), 3, 4) { Name = "porca_a", IsTransientTool = true };
                var furo_b = new CylinderFeature(new Point3D(28, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_b", IsTransientTool = true };
                return BooleanFeature.CreateDifference("bloco", corpo, furo_a, porca_a, furo_b);
            }
        }
        """;

    private static ScriptDocument[] Documents(string scene = "Add(new Bloco(), name: \"bloco\");") => new[]
    {
        new ScriptDocument(ScriptDocument.DefaultSceneFile, scene),
        new ScriptDocument("Pecas/Bloco.cs", BlocoRecord)
    };

    private static LeverStudy Study(ScriptDocument[] documents)
    {
        var probe = new RoslynLeverProbeFactory().Create(documents, "Bloco");
        probe.Should().NotBeNull("a cena instrumentada precisa compilar");
        var study = LeverStudy.Run(probe!, "bloco", Placement.Identity);
        study.Should().NotBeNull();
        return study!;
    }

    /// <summary>
    /// A folha de frente da mesma peça que a cena cria (referencial = mundo). O desenho do app parte da
    /// feature executada; aqui ela é escrita à mão com os mesmos nomes, que é o que o medidor usa.
    /// </summary>
    private static DraftingSheet Sheet(LeverStudy study)
    {
        study.Baseline.Should().NotBeEmpty();
        return DraftingAnalyzer.Analyze(new DraftingInput([Bloco()], StandardView.Front, Point3D.Origin, new Point3D(40, 16, 17)))!;
    }

    private static AnalysisSubject Bloco() =>
        new(BooleanFeature.CreateDifference("bloco",
                new BoxFeature(Point3D.Origin, 40, 16, 17) { Name = "corpo" },
                new CylinderFeature(new Point3D(10, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_a", IsTransientTool = true },
                new CylinderFeature(new Point3D(10, 16, 5), new Vector3D(0, -1, 0), 3, 4) { Name = "porca_a", IsTransientTool = true },
                new CylinderFeature(new Point3D(28, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo_b", IsTransientTool = true }),
            Placement.Identity);

    /// <summary>Cota manual da borda esquerda do corpo ao centro do furo_a (10).</summary>
    private static (DraftingSheet Sheet, DimensionGauge Gauge) BordaAoFuro(LeverStudy study)
    {
        var sheet = Sheet(study);
        var dimension = DimensionFactory.Linear(sheet.FindAnchor("corpo.esq_inf")!, sheet.FindAnchor("furo_a.centro")!, DimensionKind.Horizontal, -5);
        var gauge = DimensionGauge.For(sheet, dimension);
        gauge.Should().NotBeNull();
        return (sheet, gauge!);
    }

    [Fact]
    public void Sonda_AchaLiteraisDoRecordArgumentosEPadroes_ComRotuloLegivel()
    {
        using var probe = new RoslynLeverProbeFactory().Create(Documents("Add(new Bloco(50), name: \"bloco\");"), "Bloco")!;

        probe.Levers.Should().Contain(l => l.Kind == CodeLeverKind.Literal && l.Label == "furo_a · globalBaseCenter.x" && l.Value == 10);
        probe.Levers.Should().Contain(l => l.Kind == CodeLeverKind.Literal && l.Label == "furo_a · radius" && l.Value == 1.7);
        probe.Levers.Should().Contain(l => l.Kind == CodeLeverKind.Argument && l.Value == 50 && l.Path == "Largura");
        probe.Levers.Should().Contain(l => l.Kind == CodeLeverKind.Default && l.Label == "Largura (padrão)" && l.Value == 40);
        probe.Levers.Select(l => l.Text).Where(t => !IsNumber(t)).Should().BeEmpty("só números: o nome dos sólidos é texto");
    }

    private static bool IsNumber(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

    [Fact]
    public void Sonda_DeixaDeForaContagemInteiraEConst()
    {
        const string record = """
            public sealed record Grade(double Passo = 10) : Part
            {
                private const double Folga = 0.2;
                protected override IFeature Build()
                {
                    int lados = 6;
                    var corpo = new CylinderFeature(new Point3D(0, 0, 0), new Vector3D(0, 0, 1), 5 + Folga, 2 * lados) { Name = "corpo" };
                    return corpo;
                }
            }
            """;
        using var probe = new RoslynLeverProbeFactory().Create(
            [new ScriptDocument(ScriptDocument.DefaultSceneFile, "Add(new Grade(), name: \"g\");"), new ScriptDocument("Pecas/Grade.cs", record)], "Grade")!;

        probe.Levers.Select(l => l.Text).Should().NotContain("0.2", "const exige constante")
            .And.NotContain("6", "a contagem é int")
            .And.NotContain("2", "2 * lados é conta inteira");
        probe.Levers.Should().Contain(l => l.Text == "5" && l.Path == "radius");
        probe.Run(new Dictionary<int, double>()).Should().ContainSingle(f => f.Name == "g");
    }

    [Fact]
    public void Sonda_SemTrocar_DaAMesmaCenaETrocandoMoveSoOFuro()
    {
        using var study = Study(Documents());

        study.Baseline.Select(f => f.Name).Should().Contain(new[] { "corpo", "furo_a", "porca_a", "furo_b" });
        var furoX = study.Effects.Single(e => e.Lever.Label == "furo_a · globalBaseCenter.x");
        furoX.Changes.Should().ContainSingle().Which.Name.Should().Be("furo_a");
        furoX.Changes[0].Kind.Should().Be(FactChangeKind.Moved);
    }

    [Fact]
    public void PadraoNaoPassadoNaCena_EhAlavancaDaLargura()
    {
        using var study = Study(Documents());

        var largura = study.Effects.Single(e => e.Lever.Kind == CodeLeverKind.Default);
        largura.Changes.Should().ContainSingle(c => c.Name == "corpo").Which.Kind.Should().Be(FactChangeKind.MovedAndResized);
    }

    [Fact]
    public void Medidor_RemedeACotaSobreOsFatosTrocados()
    {
        using var study = Study(Documents());
        var (_, gauge) = BordaAoFuro(study);

        gauge.Measure(study.Baseline).Should().BeApproximately(10, 1e-9);
        gauge.MeasuredSources.Should().BeEquivalentTo("corpo", "furo_a");

        var furoX = study.Effects.Single(e => e.Lever.Label == "furo_a · globalBaseCenter.x");
        gauge.Measure(furoX.Facts).Should().BeApproximately(10 + furoX.Step, 1e-9);
    }

    [Fact]
    public void Opcoes_FuroSozinhoQuebraACoaxial_EOGrupoMoveAPorcaJunto()
    {
        using var study = Study(Documents());
        var (_, gauge) = BordaAoFuro(study);

        var options = LeverOptionFinder.Find(study, gauge.Measure).ToList();

        var sozinho = options.Single(o => !o.IsGroup && o.Primary.Label == "furo_a · globalBaseCenter.x");
        sozinho.Rate.Should().BeApproximately(1, 1e-6);
        sozinho.Moved.Should().BeEquivalentTo("furo_a");
        sozinho.Breaks.Should().ContainSingle(r => r.Kind == LeverRelationKind.Coaxial && r.Fact == "porca_a");

        var junto = options.Single(o => o.IsGroup && o.Primary.Label == "furo_a · globalBaseCenter.x");
        junto.Shares.Select(s => (s.Lever.Label, Math.Round(s.Weight, 6))).Should().BeEquivalentTo(new[]
        {
            ("furo_a · globalBaseCenter.x", 1.0),
            ("porca_a · globalBaseCenter.x", 1.0)
        });
        junto.Moved.Should().BeEquivalentTo("furo_a", "porca_a");
        junto.Keeps.Should().ContainSingle(r => r.Fact == "porca_a");
        junto.Breaks.Should().BeEmpty();

        options.IndexOf(junto).Should().BeLessThan(options.IndexOf(sozinho), "manter a relação vem antes de quebrá-la");
        options.Should().NotContain(o => o.Primary.Kind == CodeLeverKind.Default, "a largura não muda a distância da borda esquerda ao furo");
    }

    [Fact]
    public void Resolver_E_Gravar_MudaOsDoisLiteraisPara12()
    {
        var documents = Documents();
        using var study = Study(documents);
        var (_, gauge) = BordaAoFuro(study);
        var junto = LeverOptionFinder.Find(study, gauge.Measure).Single(o => o.IsGroup && o.Primary.Label == "furo_a · globalBaseCenter.x");

        var solution = LeverSolver.Solve(junto, 12, study.Evaluate, gauge.Measure);

        solution.Should().NotBeNull();
        solution!.Achieved.Should().BeApproximately(12, 1e-3);
        solution.Changes.Select(c => c.Text).Should().BeEquivalentTo("12", "12");

        var changed = LeverSourceEditor.Apply(documents, solution.Changes);
        changed.Should().ContainSingle();
        changed![0].Text.Should().Contain("new CylinderFeature(new Point3D(12, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = \"furo_a\"")
            .And.Contain("new CylinderFeature(new Point3D(12, 16, 5), new Vector3D(0, -1, 0), 3, 4) { Name = \"porca_a\"")
            .And.Contain("new Point3D(28, 16, 5)", "o furo_b não acompanha");
    }

    [Fact]
    public void Gravar_DepoisQueOCodigoMudou_Recusa()
    {
        var documents = Documents();
        using var study = Study(documents);
        var lever = study.Effects.Single(e => e.Lever.Label == "furo_a · globalBaseCenter.x").Lever;
        var edited = documents.Select(d => d.FilePath.EndsWith("Bloco.cs") ? d with { Text = "// novo\n" + d.Text } : d).ToList();

        LeverSourceEditor.Apply(edited, [new LeverChange(lever, 12)]).Should().BeNull();
    }
}

/// <summary>
/// De quem é a sonda. O estudo só fica com ela quando <see cref="LeverStudy.Run"/> o devolve; em qualquer
/// outra saída (cancelado, erro, peça que não está na cena) ela é descartada ali, porque quem chamou não
/// tem mais como alcançá-la e a cena compilada ficaria carregada para sempre.
/// </summary>
public class LeverStudyLifetimeTests
{
    private sealed class FakeProbe(int levers, Func<int, IReadOnlyList<IFeature>?> run) : ILeverProbe
    {
        public IReadOnlyList<CodeLever> Levers { get; } = Enumerable.Range(0, levers)
            .Select(i => new CodeLever(i, CodeLeverKind.Literal, new CodeLocation("Pecas/Bloco.cs", i, 2, 1, i + 1, ""), "10", 10, "corpo", ""))
            .ToList();

        /// <summary>Quantas vezes a cena foi executada (a primeira é a de base).</summary>
        public int Runs { get; private set; }

        public bool Disposed { get; private set; }

        public IReadOnlyList<IFeature>? Run(IReadOnlyDictionary<int, double> values) => run(Runs++);

        public void Dispose() => Disposed = true;
    }

    private static IReadOnlyList<IFeature> Cena() => [new BoxFeature(Point3D.Origin, 40, 16, 17) { Name = "bloco" }];

    [Fact]
    public void CanceladoNoMeio_ParaDeExecutarEDescartaASonda()
    {
        using var cancellation = new System.Threading.CancellationTokenSource();
        var probe = new FakeProbe(levers: 5, run =>
        {
            if (run == 1) cancellation.Cancel(); // durante o primeiro número
            return Cena();
        });

        Action act = () => LeverStudy.Run(probe, "bloco", Placement.Identity, cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
        probe.Runs.Should().Be(2, "a base e o número em que o cancelamento chegou; os outros 4 não executam");
        probe.Disposed.Should().BeTrue();
    }

    [Fact]
    public void JaCancelado_NemExecutaACena()
    {
        var probe = new FakeProbe(levers: 3, _ => Cena());

        Action act = () => LeverStudy.Run(probe, "bloco", Placement.Identity, new System.Threading.CancellationToken(canceled: true));

        act.Should().Throw<OperationCanceledException>();
        probe.Runs.Should().Be(0);
        probe.Disposed.Should().BeTrue();
    }

    [Fact]
    public void SondaQueLanca_DescartaASonda()
    {
        var probe = new FakeProbe(levers: 3, run => run == 0 ? Cena() : throw new InvalidOperationException("falhou"));

        Action act = () => LeverStudy.Run(probe, "bloco", Placement.Identity);

        act.Should().Throw<InvalidOperationException>();
        probe.Disposed.Should().BeTrue();
    }

    [Fact]
    public void PecaForaDaCena_DevolveNullEDescartaASonda()
    {
        var probe = new FakeProbe(levers: 3, _ => Cena());

        LeverStudy.Run(probe, "outra", Placement.Identity).Should().BeNull();
        probe.Disposed.Should().BeTrue();
    }

    [Fact]
    public void EstudoDevolvido_FicaComASondaAteSerDescartado()
    {
        var probe = new FakeProbe(levers: 3, _ => Cena());

        var study = LeverStudy.Run(probe, "bloco", Placement.Identity);

        study.Should().NotBeNull();
        probe.Runs.Should().Be(4, "a base e uma execução por número");
        probe.Disposed.Should().BeFalse("o estudo ainda responde perguntas com a sonda");
        study!.Dispose();
        probe.Disposed.Should().BeTrue();
    }
}

/// <summary>Regras de texto da gravação, sem compilar nada.</summary>
public class LeverSourceEditorTests
{
    private static CodeLever Lever(string text, string source)
    {
        int start = source.IndexOf(text, StringComparison.Ordinal);
        return new CodeLever(0, CodeLeverKind.Literal, new CodeLocation("a.cs", start, text.Length, 1, start + 1, source),
            text, double.Parse(text, System.Globalization.CultureInfo.InvariantCulture), "x", "");
    }

    private static string Apply(string source, string literal, double value) =>
        LeverSourceEditor.Apply([new ScriptDocument("a.cs", source)], [new LeverChange(Lever(literal, source), value)])!.Single().Text;

    [Fact]
    public void LiteralDouble_ContinuaDouble() =>
        Apply("var x = 2.5;", "2.5", 3).Should().Be("var x = 3.0;");

    [Fact]
    public void LiteralInteiro_GanhaCasasSoQuandoPrecisa() =>
        Apply("P(10, 5)", "10", 12.25).Should().Be("P(12.25, 5)");

    [Fact]
    public void NegativoDepoisDeMenos_VaiEntreParenteses() =>
        Apply("var y = a-5;", "5", -3).Should().Be("var y = a-(-3);");

    [Fact]
    public void LiteralComSinal_TrocaOTrechoInteiro() =>
        Apply("P(-5, 0)", "-5", 2).Should().Be("P(2, 0)");
}

/// <summary>O que muda entre duas extrações, pelo nome estável.</summary>
public class FactMotionTests
{
    private static IReadOnlyList<PrimitiveFact> Facts(double furoX, double largura = 40) =>
        FactExtractor.Extract([new AnalysisSubject(BooleanFeature.CreateDifference("p",
            new BoxFeature(Point3D.Origin, largura, 16, 17) { Name = "corpo" },
            new CylinderFeature(new Point3D(furoX, 16, 5), new Vector3D(0, -1, 0), 1.7, 20) { Name = "furo", IsTransientTool = true }),
            Placement.Identity)]);

    [Fact]
    public void FuroMovido_SoEleMuda()
    {
        var changes = FactMotion.Compare(Facts(10), Facts(12));

        changes.Should().ContainSingle().Which.Should().Match<FactChange>(c =>
            c.Name == "furo" && c.Kind == FactChangeKind.Moved && Math.Abs(c.Translation.X - 2) < 1e-9);
    }

    [Fact]
    public void MesmaPeca_NadaMuda() => FactMotion.Compare(Facts(10), Facts(10)).Should().BeEmpty();

    [Fact]
    public void CorpoMaisLargo_MudaDeTamanhoEDeCentro() =>
        FactMotion.Compare(Facts(10), Facts(10, 50)).Should().ContainSingle()
            .Which.Kind.Should().Be(FactChangeKind.MovedAndResized);
}

/// <summary>
/// A sonda sobre a cena real (AiCore): para cada peça posta na cena, a cena instrumentada compila, executa e,
/// sem trocar nada, dá exatamente os mesmos fatos que a execução normal.
/// </summary>
public class DimensionLeverAiCoreTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string AiCoreFolder([System.Runtime.CompilerServices.CallerFilePath] string? thisFile = null)
    {
        var dir = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(thisFile)!);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "AiCore"))) dir = dir.Parent;
        dir.Should().NotBeNull("este teste lê a cena AiCore do repo");
        return System.IO.Path.Combine(dir!.FullName, "AiCore");
    }

    [Fact]
    public async System.Threading.Tasks.Task CadaPecaDaCena_SondaSemTrocar_DaOsMesmosFatos()
    {
        var documents = AvaloniaCadEditor.Application.Scenes.SceneProject.Load(AiCoreFolder()).ReadDocuments();
        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        result.Success.Should().BeTrue(result.ErrorMessage);

        var instances = result.Features.OfType<PartInstance>().GroupBy(i => i.PartTypeName).Select(g => g.First()).ToList();
        instances.Should().NotBeEmpty();

        foreach (var instance in instances)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var probe = new RoslynLeverProbeFactory().Create(documents, instance.PartTypeName);
            probe.Should().NotBeNull($"a cena instrumentada para {instance.PartTypeName} precisa compilar");
            using var study = LeverStudy.Run(probe!, instance.Name, Placement.Identity);
            study.Should().NotBeNull($"{instance.PartTypeName} precisa executar");

            var expected = FactExtractor.Extract([AnalysisSubject.For(instance, Placement.Identity)]);
            FactMotion.Compare(expected, study!.Baseline).Should().BeEmpty($"sem trocar nada, {instance.PartTypeName} é a mesma peça");
            output.WriteLine($"{instance.PartTypeName}: {probe!.Levers.Count} números, {study.Effects.Count} mudam a peça, {watch.ElapsedMilliseconds} ms");
        }
    }
}

/// <summary>
/// O desenho real da placa de suporte (vista de topo, referencial da peça isolada): cada cota é remedida
/// sobre os fatos da sonda com o valor que o desenho mostra, e a maioria tem números que a mudam.
/// </summary>
public class DimensionLeverPlacaTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string AiCoreFolder([System.Runtime.CompilerServices.CallerFilePath] string? thisFile = null)
    {
        var dir = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(thisFile)!);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "AiCore"))) dir = dir.Parent;
        return System.IO.Path.Combine(dir!.FullName, "AiCore");
    }

    [Fact]
    public async System.Threading.Tasks.Task CotasDaPlaca_RemedidasPelaSonda_BatemComODesenho()
    {
        var documents = AvaloniaCadEditor.Application.Scenes.SceneProject.Load(AiCoreFolder()).ReadDocuments();
        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        result.Success.Should().BeTrue(result.ErrorMessage);
        var placa = result.Features.OfType<PartInstance>().First(f => f.PartTypeName == "PlacaSuporteCarrinhoY");
        var positions = new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel().BuildFeatureMesh(placa).Positions;
        var frame = AvaloniaCadEditor.Application.Isolation.PartFrameResolver.Resolve(placa, positions)!;
        var sheet = DraftingAnalyzer.Analyze(DraftingInput.ForIsolated(placa, frame, positions, StandardView.Top)!)!;

        using var study = LeverStudy.Run(new RoslynLeverProbeFactory().Create(documents, placa.PartTypeName)!, placa.Name, frame.WorldToFrame)!;

        var measurable = sheet.Dimensions.Where(d => d.Kind != DimensionKind.Callout).ToList();
        measurable.Should().NotBeEmpty();
        int withOptions = 0;
        foreach (var dimension in measurable)
        {
            var gauge = DimensionGauge.For(sheet, dimension);
            gauge.Should().NotBeNull($"a cota {dimension.Text} ({dimension.Key}) tem referências");
            gauge!.Measure(study.Baseline).Should().BeApproximately(dimension.Value, 1e-6, $"a sonda vê a mesma peça que o desenho ({dimension.Key})");

            var options = LeverOptionFinder.Find(study, gauge.Measure);
            if (options.Count > 0) withOptions++;
            output.WriteLine($"{dimension.Text,-14} {options.Count} opções; 1ª: {options.FirstOrDefault()?.Primary.Label}");
        }
        withOptions.Should().BeGreaterThan(measurable.Count / 2);
    }
}
