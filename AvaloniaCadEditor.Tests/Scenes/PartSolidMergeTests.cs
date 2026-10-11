using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Incorporar um sólido solto (desenhado no referencial do MUNDO) ao Build() de uma peça existente
/// (referencial LOCAL): as coordenadas são reescritas pelo inverso do placement da instância — inclusive
/// a rotação, que é o que costuma ser esquecido — e o resultado é conferido executando a cena.
/// </summary>
public class PartSolidMergeTests
{
    private const string SceneFile = ScriptDocument.DefaultSceneFile;
    private const string PartFile = "Pecas/BlocoBelt.cs";

    /// <summary>
    /// Meia-peça no estilo do BlocoFixacaoBelt: o furo_vertical local (-5.61533, 18.75, 9) é o mesmo
    /// cylinder_54d2 desenhado na cena em (5.61533, -18.75, 35), com a peça girada 180° e erguida 26 mm.
    /// </summary>
    private const string BeltPart = """
        public sealed record BlocoBelt(bool EspelhadoNoEixoX = false) : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(new Point3D(-20.615334, 10.5, 0), 20.5, 14.5, 17) { Name = "corpo" };
                var vao_correia = new BoxFeature(new Point3D(-22.61533, 15.75, 9), 17, 6, 8) { Name = "vao_correia", IsTransientTool = true };
                var furo_vertical = new CylinderFeature(new Point3D(-5.61533, 18.75, 9), Vector3D.UnitZ, 3, 8) { Name = "furo_vertical", IsTransientTool = true };

                // Une as metades e subtrai os vazios.
                var baseCorpos = new IFeature[] { corpo };
                return BooleanFeature.CreateDifference("bloco_belt", baseCorpos, vao_correia, furo_vertical);
            }
        }
        """;

    /// <summary>O sólido exatamente como o editor o escreve na cena (colado da cena real).</summary>
    private const string BeltScene = """
        var bloco = Add(new BlocoBelt(), at: Placement.At(0, 0, 26, rotationZDegrees: 180), name: "bloco");
        var prism_f62c_verts = new[]
        {
            new Point3D(26, 3.25, 0),
            new Point3D(26, 6.25, 0),
            new Point3D(30.3, 6.25, 0),
            new Point3D(30.3, 7.55, 0),
            new Point3D(41, 7.55, 0),
            new Point3D(41, 3.25, 0)
        };
        var prism_f62c_plane = new WorkPlane(new Point3D(-20.38467, -25, 35), new Vector3D(0, 0, 1), new Vector3D(1, 0, 0));
        var prism_f62c = new PrismFeature(prism_f62c_verts, height: 8, plane: prism_f62c_plane)
        {
            Name = "prism_f62c"
        };
        AddFeature(prism_f62c);
        var cylinder_821a = new CylinderFeature(globalBaseCenter: new Point3D(9.91533, -18.75, 35), globalDirection: new Vector3D(0, 0, 1), radius: 1.3, height: 10)
        {
            Name = "cylinder_821a"
        };
        AddFeature(cylinder_821a);
        var cylinder_54d2 = new CylinderFeature(globalBaseCenter: new Point3D(5.61533, -18.75, 35), globalDirection: new Vector3D(0, 0, 1), radius: 3, height: 10)
        {
            Name = "cylinder_54d2"
        };
        AddFeature(cylinder_54d2);
        var boolOp_7b90 = new BooleanFeature(name: "boolOp_7b90", operation: BooleanOperationType.Union, baseFeature: prism_f62c, toolFeatures: new IFeature[] { cylinder_821a });
        AddFeature(boolOp_7b90);
        var boolOp_16db = new BooleanFeature(name: "boolOp_16db", operation: BooleanOperationType.Subtract, baseFeature: boolOp_7b90, toolFeatures: new IFeature[] { cylinder_54d2 });
        AddFeature(boolOp_16db);
        """;

    private static ScriptDocument[] Docs(string scene, string part = BeltPart) =>
        new[] { new ScriptDocument(SceneFile, scene), new ScriptDocument(PartFile, part) };

    private static int RegistrationLine(string code, string variable)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        int index = Array.FindIndex(lines, l => l.Contains($"AddFeature({variable}", StringComparison.Ordinal));
        index.Should().BeGreaterThanOrEqualTo(0, $"a cena de teste registra {variable}");
        return index + 1;
    }

    private sealed record Outcome(PartMerge Merge, PartMergeCheck.Verdict Verdict, SceneEvaluationResult Before, SceneEvaluationResult After)
    {
        public string Part => Merge.Changes.Single(c => c.FilePath == PartFile).Text;
        public string Scene => Merge.Changes.Single(c => c.FilePath == SceneFile).Text;
    }

    private static async Task<SceneEvaluationResult> Evaluate(IReadOnlyList<ScriptDocument> documents)
    {
        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        result.Success.Should().BeTrue(result.ErrorMessage + string.Join("\n", result.Diagnostics));
        return result;
    }

    private static PartMergeRequest Request(SceneEvaluationResult before, string scene, string variable, string target, PartMergeMode mode)
    {
        var instance = before.Features.OfType<PartInstance>().Single(i => i.Name == target);
        var solid = before.Features.Single(f => f.Name == variable);
        return new PartMergeRequest(SceneFile, RegistrationLine(scene, variable), instance.PartTypeName,
            instance.Placement, (solid as TransformedFeature)?.Placement, mode);
    }

    private static async Task<Outcome> MergeAndVerify(
        IReadOnlyList<ScriptDocument> documents, string variable, string target = "bloco", PartMergeMode mode = PartMergeMode.UnionAfterCuts)
    {
        var before = await Evaluate(documents);
        var merge = new RoslynPartMerger().Merge(documents, Request(before, documents[0].Text, variable, target, mode));
        merge.Refusal.Should().BeNull();

        var after = await new RoslynSceneEvaluator().EvaluateAsync(merge.ApplyTo(documents));
        var solid = before.Features.Single(f => f.Name == variable);
        return new Outcome(merge, PartMergeCheck.Verify(solid, before.Features, after, target), before, after);
    }

    private static async Task<PartMerge> MergeOnly(
        IReadOnlyList<ScriptDocument> documents, string variable, string target = "bloco", PartMergeMode mode = PartMergeMode.UnionAfterCuts)
    {
        var before = await Evaluate(documents);
        return new RoslynPartMerger().Merge(documents, Request(before, documents[0].Text, variable, target, mode));
    }

    // ---------------------------------------------------------------- O caso real

    [Fact]
    public async Task Belt_PecaGirada180_ReescreveParaOReferencialLocal()
    {
        var outcome = await MergeAndVerify(Docs(BeltScene), "boolOp_16db");

        outcome.Verdict.Problem.Should().BeNull();
        outcome.Verdict.GeometryVerified.Should().BeTrue();

        // O cilindro desenhado no mundo cai exatamente sobre o furo_vertical da peça: a rotação de 180° foi desfeita.
        outcome.Part.Should().Contain("globalBaseCenter: new Point3D(-5.61533, 18.75, 9)")
            .And.Contain("globalBaseCenter: new Point3D(-9.91533, 18.75, 9)")
            .And.Contain("globalDirection: new Vector3D(0, 0, 1)", "a direção em Z não muda com uma rotação em Z");

        // O prisma gira com o plano: origem e eixo X do plano mudam, os vértices (no plano) não.
        outcome.Part.Should().Contain("new WorkPlane(new Point3D(20.38467, 25, 9), new Vector3D(0, 0, 1), new Vector3D(-1, 0, 0))")
            .And.Contain("new Point3D(26, 3.25, 0)")
            .And.Contain("new Point3D(41, 7.55, 0)");

        // Por padrão o sólido é unido DEPOIS dos cortes: o vao_correia não o apaga.
        outcome.Part.Should().Contain("var bloco_belt_base = BooleanFeature.CreateDifference(\"bloco_belt_base\", baseCorpos, vao_correia, furo_vertical);")
            .And.Contain("return BooleanFeature.CreateUnion(\"bloco_belt\", bloco_belt_base, boolOp_16db);");

        outcome.Scene.Should().NotContain("prism_f62c").And.NotContain("cylinder_").And.NotContain("boolOp_")
            .And.Contain("var bloco = Add(new BlocoBelt()");
        outcome.Merge.Warnings.Should().BeEmpty("tudo era literal: nada caiu no fallback (os avisos de instâncias/espelho são do PartMergeWarnings)");
    }

    [Fact]
    public async Task Belt_UnirAntesDosCortes_EntraNaColecaoDeBasesQueVemAntes()
    {
        var outcome = await MergeAndVerify(Docs(BeltScene), "boolOp_16db", mode: PartMergeMode.UnionBeforeCuts);

        outcome.Verdict.Problem.Should().BeNull();
        outcome.Part.Should().Contain("var baseCorpos = new IFeature[] { corpo, boolOp_16db };")
            .And.Contain("return BooleanFeature.CreateDifference(\"bloco_belt\", baseCorpos, vao_correia, furo_vertical);");
        outcome.Part.IndexOf("var boolOp_16db", StringComparison.Ordinal)
            .Should().BeLessThan(outcome.Part.IndexOf("var baseCorpos", StringComparison.Ordinal), "o sólido precisa existir antes da coleção que o lista");
    }

    [Fact]
    public async Task Subtrair_EntraComoFerramentaDaDiferenca()
    {
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(0, 0, 26, rotationZDegrees: 180), name: "bloco");
            var furo_novo = new CylinderFeature(new Point3D(10, -12, 20), Vector3D.UnitZ, 1.5, 30) { Name = "furo_novo" };
            AddFeature(furo_novo);
            """;

        var outcome = await MergeAndVerify(Docs(scene), "furo_novo", mode: PartMergeMode.SubtractAsTool);

        outcome.Verdict.Problem.Should().BeNull();
        outcome.Part.Should().Contain("new CylinderFeature(new Point3D(-10, 12, -6), Vector3D.UnitZ, 1.5, 30)")
            .And.Contain("furo_novo.IsTransientTool = true;")
            .And.Contain("BooleanFeature.CreateDifference(\"bloco_belt\", baseCorpos, vao_correia, furo_vertical, furo_novo);");
    }

    [Fact]
    public async Task UnirAntesDosCortes_SemCreateDifference_Recusa()
    {
        const string part = """
            public sealed record BlocoBelt() : Part
            {
                protected override IFeature Build() =>
                    new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "corpo" };
            }
            """;
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(5, 0, 0), name: "bloco");
            var dente = new BoxFeature(new Point3D(5, 0, 10), 2, 2, 2) { Name = "dente" };
            AddFeature(dente);
            """;

        var merge = await MergeOnly(Docs(scene, part), "dente", mode: PartMergeMode.UnionBeforeCuts);

        merge.Refusal.Should().Contain("CreateDifference");
    }

    [Fact]
    public async Task BuildComExpressao_ViraBlocoESoTranslada()
    {
        const string part = """
            public sealed record BlocoBelt() : Part
            {
                protected override IFeature Build() =>
                    new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "corpo" };
            }
            """;
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(5, 0, 0), name: "bloco");
            var dente = new BoxFeature(new Point3D(5, 0, 10), 2, 2, 2) { Name = "dente" };
            AddFeature(dente);
            """;

        var outcome = await MergeAndVerify(Docs(scene, part), "dente");

        outcome.Verdict.Problem.Should().BeNull();
        outcome.Verdict.GeometryVerified.Should().BeTrue();
        outcome.Part.Should().Contain("var dente = new BoxFeature(new Point3D(0, 0, 10), 2, 2, 2) { Name = \"dente\" };")
            .And.Contain("var bloco_belt_base = new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = \"corpo\" };")
            .And.Contain("return BooleanFeature.CreateUnion(\"bloco_belt\", bloco_belt_base, dente);")
            .And.NotContain("Direction", "sem rotação a caixa não precisa de direções");
    }

    [Fact]
    public async Task CaixaComPecaGirada90_GanhaDirecoes()
    {
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(3, 4, 0, rotationZDegrees: 90), name: "bloco");
            var dente = new BoxFeature(new Point3D(-12, -8, 17), 4, 2, 3) { Name = "dente" };
            AddFeature(dente);
            """;

        var outcome = await MergeAndVerify(Docs(scene), "dente");

        outcome.Verdict.Problem.Should().BeNull();
        outcome.Verdict.GeometryVerified.Should().BeTrue();
        outcome.Part.Should().Contain("DirectionX = new Vector3D(0, -1, 0)").And.Contain("DirectionY = new Vector3D(1, 0, 0)");
    }

    [Fact]
    public async Task WorkPlaneDeDoisArgumentos_ComRotacao_GanhaEixoXExplicito()
    {
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(0, 0, 26, rotationZDegrees: 180), name: "bloco");
            var placa_plane = new WorkPlane(new Vector3D(0, 0, 1), new Point3D(-10, -14, 43));
            var placa = new PrismFeature(new[] { new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 2, 0) }, 1, placa_plane) { Name = "placa" };
            AddFeature(placa);
            """;

        var outcome = await MergeAndVerify(Docs(scene), "placa");

        outcome.Verdict.Problem.Should().BeNull();
        outcome.Verdict.GeometryVerified.Should().BeTrue();
        outcome.Part.Should().Contain("var placa_plane = new WorkPlane(new Point3D(10, 14, 17), new Vector3D(0, 0, 1), new Vector3D(-1, 0, 0));");
    }

    [Fact]
    public async Task RegistradoDeslocado_OMovimentoEntraNaConta()
    {
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(0, 0, 26, rotationZDegrees: 180), name: "bloco");
            var pino = new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, 1, 5) { Name = "pino" };
            AddFeature(pino.Transformed(Placement.Translate(new Vector3D(10, -20, 43))));
            """;

        var outcome = await MergeAndVerify(Docs(scene), "pino");

        outcome.Verdict.Problem.Should().BeNull();
        outcome.Verdict.GeometryVerified.Should().BeTrue();
        outcome.Part.Should().Contain("new CylinderFeature(new Point3D(-10, 20, 17), Vector3D.UnitZ, 1, 5)");
        outcome.Scene.Should().NotContain("pino");
    }

    [Fact]
    public async Task RegistradoEspelhado_MantemOCodigoEEnvolveEmTransformedComEspelho()
    {
        // Reescrever coordenadas com espelho desfaria a reflexão dentro dos WorkPlane; o código fica
        // como está e o espelho vai inteiro para o .Transformed — sem virar um giro de 180° por engano.
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(0, 0, 26, rotationZDegrees: 180), name: "bloco");
            var pino = new CylinderFeature(new Point3D(2, -20, 43), Vector3D.UnitZ, 1, 5) { Name = "pino" };
            AddFeature(pino.Transformed(Placement.MirrorX(atX: 5)));
            """;

        var outcome = await MergeAndVerify(Docs(scene), "pino");

        outcome.Verdict.Problem.Should().BeNull();
        outcome.Verdict.GeometryVerified.Should().BeTrue();
        outcome.Part.Should().Contain("new Point3D(2, -20, 43)")
            .And.Contain(".Transformed(Placement.MirrorX().Then(Placement.At(");
        outcome.Scene.Should().NotContain("pino");
    }

    [Fact]
    public async Task ValorNaoLiteral_MantemOCodigoEEnvolveEmTransformed()
    {
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(0, 0, 26, rotationZDegrees: 180), name: "bloco");
            var pino = new CylinderFeature(new Point3D(Math.Sqrt(100), -20, 43), Vector3D.UnitZ, 1, 5) { Name = "pino" };
            AddFeature(pino);
            """;

        var outcome = await MergeAndVerify(Docs(scene), "pino");

        outcome.Verdict.Problem.Should().BeNull();
        outcome.Verdict.GeometryVerified.Should().BeTrue();
        outcome.Part.Should().Contain("new Point3D(Math.Sqrt(100), -20, 43)")
            .And.Contain("}.Transformed(Placement.At(0, 0, -26, rotationZDegrees: 180));");
        outcome.Merge.Warnings.Should().ContainSingle(w => w.Contains("Transformed"));
    }

    // ---------------------------------------------------------------- Recusas

    [Fact]
    public async Task SolidoQueLeVariavelDaCena_Recusa()
    {
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(0, 0, 26), name: "bloco");
            var raio = 2.0;
            var pino = new CylinderFeature(new Point3D(0, 0, 30), Vector3D.UnitZ, raio, 5) { Name = "pino" };
            AddFeature(pino);
            var outro = new CylinderFeature(new Point3D(50, 0, 0), Vector3D.UnitZ, raio, 5) { Name = "outro" };
            AddFeature(outro);
            """;

        var merge = await MergeOnly(Docs(scene), "pino");

        merge.Refusal.Should().Contain("raio");
    }

    [Fact]
    public async Task NomeQueAPecaJaUsa_Recusa()
    {
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(0, 0, 26), name: "bloco");
            var corpo = new BoxFeature(new Point3D(0, 0, 30), 2, 2, 2) { Name = "corpo" };
            AddFeature(corpo);
            """;

        var merge = await MergeOnly(Docs(scene), "corpo");

        merge.Refusal.Should().Contain("'corpo'");
    }

    // ---------------------------------------------------------------- Avisos

    [Fact]
    public async Task Avisos_OutrasInstanciasEEspelho()
    {
        const string scene = """
            var bloco = Add(new BlocoBelt(), at: Placement.At(0, 0, 26), name: "bloco");
            var bloco_2 = Add(new BlocoBelt(EspelhadoNoEixoX: true), at: Placement.At(100, 0, 26), name: "bloco_2");
            """;
        var before = await Evaluate(Docs(scene));
        var target = before.Features.OfType<PartInstance>().Single(i => i.Name == "bloco");

        var warnings = PartMergeWarnings.For(target, before.Features);

        warnings.Should().HaveCount(2);
        warnings[0].Should().Contain("bloco_2");
        warnings[1].Should().Contain("EspelhadoNoEixoX = false");
    }
}
