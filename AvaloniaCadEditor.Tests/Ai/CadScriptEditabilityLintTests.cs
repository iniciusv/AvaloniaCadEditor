using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Prompts;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// O linter de editabilidade (plano 2026-11-01-codigo-editavel-por-padrao): erros = regra dos sólidos
/// rastreáveis; avisos = estilo que impede as ferramentas manuais de reescrever o código.
/// </summary>
public class CadScriptEditabilityLintTests
{
    private static string[] Rules(string code, EditabilitySeverity severity) =>
        CadScriptEditabilityLint.Analyze(code).Findings.Where(f => f.Severity == severity).Select(f => f.RuleId).ToArray();

    // ------------------------------------------------------------------ erros

    [Fact]
    public void CodigoLiteral_NaoTemAchados()
    {
        const string code = """
            var chapa_1 = new BoxFeature(Point3D.Origin, 80, 50, 6) { Name = "chapa_1" };
            AddFeature(chapa_1);
            var furo_1 = new CylinderFeature(new Point3D(10, 10, 0), Vector3D.UnitZ, 2.5, 6) { Name = "furo_1", IsTransientTool = true };
            AddFeature(furo_1);
            var chapa_furada = BooleanFeature.CreateDifference("chapa_furada", chapa_1, furo_1);
            AddFeature(chapa_furada);
            """;

        var report = CadScriptEditabilityLint.Analyze(code);

        report.Findings.Should().BeEmpty();
        report.Metrics.Solids.Should().Be(3);
        report.Metrics.TrackableSolids.Should().Be(3);
        report.Metrics.EditablePointRatio.Should().Be(1.0);
    }

    [Fact]
    public void SolidoCriadoEmLaco_ComNomeInterpolado_SaoErros()
    {
        const string code = """
            public sealed record Trilho(double Comprimento = 200) : Part
            {
                protected override IFeature Build()
                {
                    var corpo = new BoxFeature(Point3D.Origin, 15, Comprimento, 10) { Name = "corpo" };
                    var furos = new List<IFeature>();
                    for (var i = 0; i < 4; i++)
                    {
                        var furo = new CylinderFeature(new Point3D(7.5, 20 + i * 40, 0), Vector3D.UnitZ, 1.75, 10) { Name = $"furo_{i}", IsTransientTool = true };
                        furos.Add(furo);
                    }
                    return BooleanFeature.CreateDifference("trilho", corpo, furos.ToArray());
                }
            }
            """;

        var report = CadScriptEditabilityLint.Analyze(code);

        Rules(code, EditabilitySeverity.Error).Should().BeEquivalentTo(new[]
        {
            CadScriptEditabilityLint.SolidInLoop, CadScriptEditabilityLint.ComputedName
        });
        report.Errors.First().Line.Should().Be(9);
        report.Metrics.TrackableSolids.Should().Be(2, "corpo e trilho são rastreáveis; o furo do laço não");
    }

    [Fact]
    public void SolidoCriadoEmLinq_EErro()
    {
        const string code = """
            var furos = new[] { 10.0, 20.0 }.Select(x => new CylinderFeature(new Point3D(x, 0, 0), Vector3D.UnitZ, 1, 5) { Name = "furo" }).ToList();
            AddFeature(furos[0]);
            """;

        Rules(code, EditabilitySeverity.Error).Should().Contain(CadScriptEditabilityLint.SolidInLoop);
    }

    [Fact]
    public void NomeConcatenadoOuVariavel_EErro()
    {
        const string code = """
            var prefixo = "furo";
            var furo_a = new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, 1, 5) { Name = prefixo + "_a" };
            AddFeature(furo_a);
            var p = Add(new Placa(), name: prefixo);
            """;

        Rules(code, EditabilitySeverity.Error).Should().Equal(
            CadScriptEditabilityLint.ComputedName, CadScriptEditabilityLint.ComputedName);
    }

    [Fact]
    public void NomeRepetidoNoArquivo_EErro()
    {
        const string code = """
            var furo_1 = new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, 1, 5) { Name = "furo_1" };
            AddFeature(furo_1);
            var furo_2 = new CylinderFeature(new Point3D(5, 0, 0), Vector3D.UnitZ, 1, 5) { Name = "furo_1" };
            AddFeature(furo_2);
            """;

        var errors = CadScriptEditabilityLint.Analyze(code).Errors.ToList();

        errors.Should().ContainSingle(e => e.RuleId == CadScriptEditabilityLint.DuplicateName);
        errors.Single(e => e.RuleId == CadScriptEditabilityLint.DuplicateName).Line.Should().Be(3);
    }

    [Fact]
    public void MoverSemNome_NaoEUmSolidoNovo()
    {
        const string code = """
            var furo_a = new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, 1, 5) { Name = "furo_a" };
            AddFeature(furo_a.Transformed(Placement.Translate(new Vector3D(10, 0, 0))));
            var furo_b = furo_a.Transformed(Placement.MirrorX(atX: 50), name: "furo_b");
            AddFeature(furo_b);
            """;

        var report = CadScriptEditabilityLint.Analyze(code);

        report.Findings.Should().BeEmpty();
        report.Metrics.Solids.Should().Be(2);
    }

    [Fact]
    public void SubPecaComInstance_ConfereONome()
    {
        const string code = """
            public sealed record Bloco(double Largura = 20) : Part
            {
                protected override IFeature Build()
                {
                    var corpo = new BoxFeature(Point3D.Origin, Largura, 20, 10) { Name = "corpo" };
                    var fixacao_1 = new FuroPorca(new Point3D(5, 5, 0), new Vector3D(0, 0, 1), 6).Instance("fixacao_1");
                    var fixacao_2 = new FuroPorca(new Point3D(15, 5, 0), new Vector3D(0, 0, 1), 6).Instance("fixacao_1");
                    return BooleanFeature.CreateDifference("bloco", corpo, fixacao_1, fixacao_2);
                }
            }
            """;

        var report = CadScriptEditabilityLint.Analyze(code);

        report.Errors.Select(e => e.RuleId).Should().Equal(CadScriptEditabilityLint.DuplicateName);
        report.Warnings.Select(w => w.RuleId).Should().Equal(CadScriptEditabilityLint.NameDiffersFromVariable);
    }

    // ------------------------------------------------------------------ avisos

    [Theory]
    [InlineData("10", true)]
    [InlineData("-3.5", true)]
    [InlineData("Largura", true)]
    [InlineData("Largura - 10", true)]
    [InlineData("(Largura + 2.5)", true)]
    [InlineData("Largura / 2", false)]
    [InlineData("-Lado / 2", false)]
    [InlineData("10 + Largura", false)]
    [InlineData("margem + i * passo", false)]
    [InlineData("peca.Comprimento", false)]
    [InlineData("Largura - Espessura - 2", false)]
    public void FormaEditavelDeUmNumero(string expression, bool editable)
    {
        var parsed = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(expression);

        CadScriptEditabilityLint.IsEditableNumber(parsed).Should().Be(editable);
    }

    [Fact]
    public void CoordenadaCalculada_EAviso_ENaoErro()
    {
        const string code = """
            public sealed record Chapa(double Largura = 60, double Comprimento = 40) : Part
            {
                protected override IFeature Build()
                {
                    var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, 5) { Name = "corpo" };
                    var furo_1 = new CylinderFeature(new Point3D(Largura - 10, 10, 0), Vector3D.UnitZ, 2, 5) { Name = "furo_1", IsTransientTool = true };
                    var furo_2 = new CylinderFeature(new Point3D(Largura / 4, Comprimento / 2, 0), Vector3D.UnitZ, 2, 5) { Name = "furo_2", IsTransientTool = true };
                    return BooleanFeature.CreateDifference("chapa", corpo, furo_1, furo_2);
                }
            }
            """;

        var report = CadScriptEditabilityLint.Analyze(code);

        report.HasErrors.Should().BeFalse();
        report.Warnings.Should().ContainSingle(w => w.RuleId == CadScriptEditabilityLint.ComputedCoordinate)
            .Which.Line.Should().Be(7);
        report.Metrics.Points.Should().Be(2);
        report.Metrics.EditablePoints.Should().Be(1);
    }

    [Fact]
    public void PlacementAtNaCena_ContaComoCoordenada_EWorldNaoConta()
    {
        const string code = """
            var largura = 100;
            var placa_1 = Add(new Placa(), at: Placement.At(0, 80, 0, rotationZDegrees: 90));
            var placa_2 = Add(new Placa(), at: Placement.At(largura / 2, 0, 0));
            var canto = placa_2.World(p => new Point3D(p.Largura, 0, 0));
            """;

        var report = CadScriptEditabilityLint.Analyze(code);

        report.Metrics.Points.Should().Be(2);
        report.Metrics.EditablePoints.Should().Be(1);
        report.Warnings.Should().ContainSingle(w => w.RuleId == CadScriptEditabilityLint.ComputedCoordinate).Which.Line.Should().Be(3);
    }

    [Fact]
    public void RecordComplexo_GeraOsAvisosDeEstilo()
    {
        const string code = """
            public sealed record Suporte(
                [System.ComponentModel.DataAnnotations.Range(10, 200)] double Largura = 60,
                double Altura = 40,
                double Espessura = 5,
                bool ComReforco = false) : Part
            {
                protected override IFeature Build()
                {
                    double Meio(double v) => v / 2;
                    var passos = new[] { 1.0, 2.0 }.Select(v => v * 10).ToArray();
                    foreach (var p in passos) { }
                    return new BoxFeature(Point3D.Origin, Largura, Altura, Espessura) { Name = "suporte" };
                }

                private double Auxiliar() => 1;
            }
            """;

        var warnings = Rules(code, EditabilitySeverity.Warning);

        Rules(code, EditabilitySeverity.Error).Should().BeEmpty();
        warnings.Should().Contain(new[]
        {
            CadScriptEditabilityLint.TooManyParameters,
            CadScriptEditabilityLint.ParameterAttribute,
            CadScriptEditabilityLint.BoolOption,
            CadScriptEditabilityLint.LocalFunction,
            CadScriptEditabilityLint.LoopOrLinq
        });
        warnings.Count(w => w == CadScriptEditabilityLint.LocalFunction).Should().Be(2, "função local e método auxiliar");
        warnings.Count(w => w == CadScriptEditabilityLint.LoopOrLinq).Should().Be(2, "o Select e o foreach");
        CadScriptEditabilityLint.Analyze(code).Metrics.MaxPartParameters.Should().Be(4);
    }

    [Fact]
    public void Format_CortaListasLongas()
    {
        var findings = Enumerable.Range(1, 15)
            .Select(i => new EditabilityFinding(CadScriptEditabilityLint.ComputedCoordinate, EditabilitySeverity.Warning, i, "x"))
            .ToList();

        string text = EditabilityReport.Format(findings, max: 12);

        text.Split('\n').Should().HaveCount(13);
        text.Should().EndWith("- (+3 achados do mesmo tipo)");
    }

    // ------------------------------------------------------------------ o que o app já ensina

    [Fact]
    public void ExemplosDeTodosOsPerfis_NaoQuebramARegraDosSolidosRastreaveis()
    {
        var withErrors = CadScriptPromptProfiles.All
            .SelectMany(p => p.Examples.Select(e => (Profile: p.Id, e.Code)))
            .Select(x => (x.Profile, x.Code, Report: CadScriptEditabilityLint.Analyze(x.Code)))
            .Where(x => x.Report.HasErrors)
            .Select(x => $"{x.Profile}: {EditabilityReport.Format(x.Report.Errors)}")
            .ToList();

        withErrors.Should().BeEmpty();
    }

    [Fact]
    public void ExemplosDoPerfilEditavel_NaoTemNenhumAviso()
    {
        var profile = CadScriptPromptProfiles.Find(EditablePromptProfile.ProfileId);

        profile.Id.Should().Be(EditablePromptProfile.ProfileId);
        foreach (var example in profile.Examples)
        {
            CadScriptEditabilityLint.Analyze(example.Code).Findings.Should().BeEmpty(example.Code);
        }
    }

    /// <summary>
    /// Linha de base da cena AiCore (2026-11-01): só estes arquivos criam sólidos em laço ou com nome
    /// calculado. Consertar um deles é bem-vindo (tire-o da lista); um arquivo novo nesta lista é regressão.
    /// </summary>
    [Fact]
    public void CenaAiCore_NaoGanhaNovosSolidosNaoRastreaveis()
    {
        string[] known = { "BlocoConexaoMotor2020.cs", "MotorNema17.cs", "SuporteHotendV6.cs", "TrilhoMgn15.cs", "TrilhoMgn9.cs" };

        var withErrors = Directory.GetFiles(Path.Combine(AiCoreFolder(), "Pecas"), "*.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(AiCoreFolder(), "Cena.cs"))
            .Where(f => CadScriptEditabilityLint.Analyze(File.ReadAllText(f)).HasErrors)
            .Select(Path.GetFileName)
            .ToList();

        withErrors.Should().BeSubsetOf(known);
    }

    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore"))) dir = dir.Parent;
        dir.Should().NotBeNull("este teste lê a cena AiCore do repo");
        return Path.Combine(dir!.FullName, "AiCore");
    }
}
