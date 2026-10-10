using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Converter um sólido solto em peça: o código que o produz vai para o Build() de um record novo em
/// Pecas/, a cena passa a chamar Add(new Peca(...)) e o sólido continua idêntico — conferido
/// executando as duas versões da cena e comparando o <see cref="PartConversionCheck"/>.
/// </summary>
public class PartConverterTests
{
    private const string SceneFile = ScriptDocument.DefaultSceneFile;

    private static ScriptDocument[] Scene(string code, params ScriptDocument[] others) =>
        new[] { new ScriptDocument(SceneFile, code) }.Concat(others).ToArray();

    /// <summary>Linha do AddFeature que registra <paramref name="variable"/> (a que o FeatureOrigin daria).</summary>
    private static int RegistrationLine(string code, string variable)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        int index = Array.FindIndex(lines, l => l.Contains($"AddFeature({variable}", StringComparison.Ordinal));
        index.Should().BeGreaterThanOrEqualTo(0, $"a cena de teste registra {variable}");
        return index + 1;
    }

    private static PartConversion Convert(IReadOnlyList<ScriptDocument> documents, string variable, string typeName = "Peca") =>
        new RoslynPartConverter().Convert(documents, SceneFile, RegistrationLine(documents[0].Text, variable), typeName);

    private static string SceneText(PartConversion conversion) => conversion.Changes.Single(c => c.FilePath == SceneFile).Text;

    private static string PartText(PartConversion conversion) => conversion.Changes.Single(c => c.FilePath != SceneFile).Text;

    /// <summary>Converte, executa antes e depois e devolve a conversão e o veredito da comparação.</summary>
    private static async Task<(PartConversion Conversion, PartConversionCheck.Verdict Verdict)> ConvertAndVerify(
        IReadOnlyList<ScriptDocument> documents, string variable, string typeName = "Peca")
    {
        var evaluator = new RoslynSceneEvaluator();
        var before = await evaluator.EvaluateAsync(documents);
        before.Success.Should().BeTrue(before.ErrorMessage);

        var conversion = Convert(documents, variable, typeName);
        conversion.Refusal.Should().BeNull();

        var after = await evaluator.EvaluateAsync(conversion.ApplyTo(documents));
        var root = before.Features.Single(f => FeatureOrigin.Of(f)?.Line == RegistrationLine(documents[0].Text, variable));
        return (conversion, PartConversionCheck.Verify(root, before.Features, after, typeName));
    }

    [Fact]
    public async Task Convert_CaixaSozinha_ViraPecaSemParametrosEIdentica()
    {
        const string code = """
            var base_1 = new BoxFeature(new Point3D(0, 0, 0), 60, 40, 10) { Name = "base_1" };
            AddFeature(base_1);
            """;

        var (conversion, verdict) = await ConvertAndVerify(Scene(code), "base_1");

        verdict.Problem.Should().BeNull();
        verdict.GeometryVerified.Should().BeTrue();
        conversion.Changes.Select(c => c.FilePath).Should().Contain("Pecas/Peca.cs");
        SceneText(conversion).Should().Contain("var base_1 = Add(new Peca(), name: \"base_1\");");
        SceneText(conversion).Should().NotContain("BoxFeature");
        PartText(conversion).Should().Contain("public sealed record Peca() : Part")
            .And.Contain("var base_1 = new BoxFeature(new Point3D(0, 0, 0), 60, 40, 10) { Name = \"base_1\" };")
            .And.Contain("return base_1;")
            .And.NotContain("AddFeature");
    }

    [Fact]
    public async Task Convert_BooleanaComFerramentas_LevaTudoEApagaOsAddFeatureInternos()
    {
        const string code = """
            var base_1 = new BoxFeature(new Point3D(0, 0, 0), 60, 40, 10) { Name = "base_1" };
            AddFeature(base_1);
            var furo_1 = new CylinderFeature(new Point3D(30, 20, -1), new Vector3D(0, 0, 1), 3, 12) { Name = "furo_1" };
            furo_1.IsTransientTool = true; // só corta
            AddFeature(furo_1);
            var corte_1 = BooleanFeature.CreateDifference("corte_1", base_1, furo_1);
            AddFeature(corte_1);
            var outro = new BoxFeature(new Point3D(100, 0, 0), 10, 10, 10) { Name = "outro" };
            AddFeature(outro);
            """;

        var (conversion, verdict) = await ConvertAndVerify(Scene(code), "corte_1");

        verdict.Problem.Should().BeNull();
        verdict.GeometryVerified.Should().BeTrue();
        string scene = SceneText(conversion);
        scene.Should().NotContain("base_1").And.NotContain("furo_1");
        scene.Should().Contain("var corte_1 = Add(new Peca(), name: \"corte_1\");")
            .And.Contain("AddFeature(outro);");
        PartText(conversion).Should().Contain("furo_1.IsTransientTool = true; // só corta")
            .And.Contain("return corte_1;");
    }

    [Fact]
    public async Task Convert_VariavelQueACenaTambemUsa_ViraParametroPassadoNaChamada()
    {
        const string code = """
            var altura = Math.Max(5.0, 10.0);
            var largura = 60.0;
            var base_1 = new BoxFeature(new Point3D(0, 0, 0), largura, 40, altura) { Name = "base_1" };
            AddFeature(base_1);
            var marcador = new BoxFeature(new Point3D(100, 0, altura), largura, 1, 1) { Name = "marcador" };
            AddFeature(marcador);
            """;

        var (conversion, verdict) = await ConvertAndVerify(Scene(code), "base_1");

        verdict.Problem.Should().BeNull();
        verdict.GeometryVerified.Should().BeTrue();
        // Sem padrão (não é constante) antes do que tem padrão (60 é constante).
        PartText(conversion).Should().Contain("public sealed record Peca(double Altura, double Largura = 60) : Part")
            .And.Contain("new BoxFeature(new Point3D(0, 0, 0), Largura, 40, Altura)");
        SceneText(conversion).Should().Contain("var base_1 = Add(new Peca(Altura: altura, Largura: largura), name: \"base_1\");")
            .And.Contain("var altura = Math.Max(5.0, 10.0);")
            .And.Contain("var largura = 60.0;");
    }

    [Fact]
    public async Task Convert_VariavelSoDoSolido_VaiParaDentroDoBuild()
    {
        const string code = """
            var espessura = 10.0;
            var base_1 = new BoxFeature(new Point3D(0, 0, 0), 60, 40, espessura) { Name = "base_1" };
            AddFeature(base_1);
            """;

        var (conversion, verdict) = await ConvertAndVerify(Scene(code), "base_1");

        verdict.Problem.Should().BeNull();
        PartText(conversion).Should().Contain("public sealed record Peca() : Part")
            .And.Contain("var espessura = 10.0;");
        SceneText(conversion).Should().NotContain("espessura");
    }

    [Fact]
    public void Convert_SolidoIntermediarioUsadoPorOutraBooleana_Recusa()
    {
        const string code = """
            var base_1 = new BoxFeature(new Point3D(0, 0, 0), 60, 40, 10) { Name = "base_1" };
            AddFeature(base_1);
            var outro = new BoxFeature(new Point3D(100, 0, 0), 60, 40, 10) { Name = "outro" };
            var furo_1 = new CylinderFeature(new Point3D(30, 20, -1), new Vector3D(0, 0, 1), 3, 12) { Name = "furo_1", IsTransientTool = true };
            AddFeature(furo_1);
            var corte_1 = BooleanFeature.CreateDifference("corte_1", base_1, furo_1);
            AddFeature(corte_1);
            var corte_2 = BooleanFeature.CreateDifference("corte_2", outro, furo_1);
            AddFeature(corte_2);
            """;

        var conversion = Convert(Scene(code), "corte_1");

        conversion.Refusal.Should().Contain("furo_1").And.Contain("linha 8");
        conversion.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Convert_SolidoMovidoPelaInterface_OPlacementViraOAt()
    {
        const string code = """
            var base_1 = new BoxFeature(new Point3D(0, 0, 0), 60, 40, 10) { Name = "base_1" };
            AddFeature(base_1.Transformed(Placement.Translate(new Vector3D(5, 0, 0))));
            """;

        var (conversion, verdict) = await ConvertAndVerify(Scene(code), "base_1");

        verdict.Problem.Should().BeNull();
        verdict.GeometryVerified.Should().BeTrue();
        SceneText(conversion).Should().Contain(
            "var base_1 = Add(new Peca(), at: Placement.Translate(new Vector3D(5, 0, 0)), name: \"base_1\");");
    }

    [Fact]
    public async Task Convert_ComentarioColadoEFuncaoLocal_VaoComOSolido_TituloDaSecaoFica()
    {
        const string code = """
            // --- Seção da base ---

            // corpo principal
            var base_1 = new BoxFeature(new Point3D(0, 0, 0), Lado(), 40, 10) { Name = "base_1" };
            AddFeature(base_1);

            double Lado() => 60;
            """;

        var (conversion, verdict) = await ConvertAndVerify(Scene(code), "base_1");

        verdict.Problem.Should().BeNull();
        PartText(conversion).Should().Contain("// corpo principal").And.Contain("double Lado() => 60;");
        SceneText(conversion).Should().Contain("// --- Seção da base ---")
            .And.NotContain("// corpo principal")
            .And.NotContain("Lado");
    }

    [Fact]
    public void Convert_TipoQueJaExiste_Recusa()
    {
        const string code = """
            var base_1 = new BoxFeature(new Point3D(0, 0, 0), 60, 40, 10) { Name = "base_1" };
            AddFeature(base_1);
            """;
        var existing = new ScriptDocument("Pecas/Suporte.cs", """
            public sealed record Suporte(double Largura = 10) : Part
            {
                protected override IFeature Build() => new BoxFeature(new Point3D(0, 0, 0), Largura, 10, 10) { Name = "suporte" };
            }
            """);

        Convert(Scene(code, existing), "base_1", "Suporte").Refusal.Should().Contain("Suporte");
        Convert(Scene(code), "base_1", "1Peca").Refusal.Should().Contain("1Peca");
    }

    [Fact]
    public async Task Verify_GeometriaDiferente_Reprova()
    {
        var evaluator = new RoslynSceneEvaluator();
        var before = await evaluator.EvaluateAsync(Scene("""
            var base_1 = new BoxFeature(new Point3D(0, 0, 0), 60, 40, 10) { Name = "base_1" };
            AddFeature(base_1);
            """));
        var after = await evaluator.EvaluateAsync(Scene(
            "var base_1 = Add(new Peca(), name: \"base_1\");",
            new ScriptDocument("Pecas/Peca.cs", """
                public sealed record Peca() : Part
                {
                    protected override IFeature Build() => new BoxFeature(new Point3D(0, 0, 0), 60, 40, 12) { Name = "base_1" };
                }
                """)));

        var verdict = PartConversionCheck.Verify(before.Features.Single(), before.Features, after, "Peca");

        verdict.Problem.Should().NotBeNull();
    }
}
