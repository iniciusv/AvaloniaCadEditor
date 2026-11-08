using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.EditContext;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Cortar/Unir com uma peça aberta: a base é a peça; um sólido solto é consumido (sai da cena e entra no
/// Build()), e outra peça é copiada como sub-peça (a original fica na cena). Sempre em coordenadas locais.
/// </summary>
public class PartBooleanOperationTests
{
    private const string SceneFile = ScriptDocument.DefaultSceneFile;
    private const string BlocoFile = "Pecas/Bloco.cs";
    private const string PinoFile = "Pecas/Pino.cs";

    private const string BlocoPart = """
        public sealed record Bloco(double Largura = 40) : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(Point3D.Origin, Largura, 30, 10) { Name = "corpo" };
                return corpo;
            }
        }
        """;

    private const string PinoPart = """
        public enum Acabamento { Liso, Recartilhado }

        public sealed record Pino(double Diametro = 4, double Comprimento = 20, Acabamento Acabamento = Acabamento.Liso) : Part
        {
            protected override IFeature Build() =>
                new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, Diametro / 2, Comprimento) { Name = "haste" };
        }
        """;

    /// <summary>Bloco girado 90° em Z e erguido; uma caixa solta e um pino (peça) atravessando-o.</summary>
    private static ScriptDocument[] Docs(string extraScene = "", string blocoAt = "Placement.At(100, 50, 20, rotationZDegrees: 90)") => new[]
    {
        new ScriptDocument(SceneFile, $$"""
            var bloco = Add(new Bloco(), at: {{blocoAt}}, name: "bloco");
            var rasgo = new BoxFeature(new Point3D(85, 60, 25), 10, 5, 10) { Name = "rasgo" };
            AddFeature(rasgo);
            var pino = Add(new Pino(Diametro: 3, Acabamento: Acabamento.Recartilhado), at: Placement.At(90, 70, 15), name: "pino");
            {{extraScene}}
            """),
        new ScriptDocument(BlocoFile, BlocoPart),
        new ScriptDocument(PinoFile, PinoPart),
    };

    private static async Task<(PartEditContext Context, SceneEvaluationResult Result)> Open(IReadOnlyList<ScriptDocument> documents)
    {
        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        result.Success.Should().BeTrue(result.ErrorMessage + string.Join("\n", result.Diagnostics));
        return (PartEditContext.For(result.Features.Single(f => f.Name == "bloco"), result.Features)!, result);
    }

    private static Task<PartBooleanResult> Apply(IReadOnlyList<ScriptDocument> documents, PartEditContext context,
        BooleanOperationType operation, params string[] operands) =>
        new PartBooleanOperation(new RoslynSceneEvaluator(), new RoslynPartMerger())
            .ApplyAsync(documents, SceneFile, context, operands, operation);

    private static async Task<SceneEvaluationResult> After(IReadOnlyList<ScriptDocument> documents, PartBooleanResult result)
    {
        var applied = documents.Select(d => result.Changes.FirstOrDefault(c => c.FilePath == d.FilePath) ?? d).ToList();
        var evaluation = await new RoslynSceneEvaluator().EvaluateAsync(applied);
        evaluation.Success.Should().BeTrue(evaluation.ErrorMessage + string.Join("\n", evaluation.Diagnostics));
        return evaluation;
    }

    private static IEnumerable<IFeature> Children(IFeature feature) => feature switch
    {
        BooleanFeature b => b.BaseFeatures.Concat(b.ToolFeatures),
        TransformedFeature t => new[] { t.Source },
        _ => Enumerable.Empty<IFeature>()
    };

    private static IFeature Find(IFeature feature, string name) =>
        feature.Name == name ? feature : Children(feature).Select(c => TryFind(c, name)).FirstOrDefault(f => f != null)
            ?? throw new InvalidOperationException($"'{name}' não está na definição");

    private static IFeature? TryFind(IFeature feature, string name) =>
        feature.Name == name ? feature : Children(feature).Select(c => TryFind(c, name)).FirstOrDefault(f => f != null);

    // ---------------------------------------------------------------- Sólido solto: consumido

    [Fact]
    public async Task Cortar_ComSolidoSolto_ConsomeOSolido_EOGravaComoFerramentaLocal()
    {
        var documents = Docs();
        var (context, _) = await Open(documents);

        var result = await Apply(documents, context, BooleanOperationType.Subtract, "rasgo");

        result.Refusal.Should().BeNull();
        result.GeometryVerified.Should().BeTrue();
        result.ChangesScene(SceneFile).Should().BeTrue("o sólido solto sai da cena");
        result.Changes.Single(c => c.FilePath == SceneFile).Text.Should().NotContain("rasgo");
        result.Changes.Single(c => c.FilePath == BlocoFile).Text.Should().Contain("rasgo.IsTransientTool = true;");

        var after = await After(documents, result);
        after.Features.Should().NotContain(f => f.Name == "rasgo");
        var bloco = after.Features.OfType<PartInstance>().Single(i => i.Name == "bloco");
        // Mundo (85..95, 60..65, 25..35) → local pelo inverso de At(100, 50, 20, 90°): x = y − 50, y = 100 − x, z = z − 20.
        var vertices = Find(bloco.Definition, "rasgo").GetVertices();
        vertices.Min(v => v.X).Should().BeApproximately(10, 1e-6);
        vertices.Max(v => v.X).Should().BeApproximately(15, 1e-6);
        vertices.Min(v => v.Y).Should().BeApproximately(5, 1e-6);
        vertices.Max(v => v.Y).Should().BeApproximately(15, 1e-6);
        vertices.Min(v => v.Z).Should().BeApproximately(5, 1e-6);
    }

    // ---------------------------------------------------------------- Outra peça: copiada

    [Fact]
    public async Task Cortar_ComOutraPeca_CopiaComoSubPeca_EAOriginalFicaNaCena()
    {
        var documents = Docs();
        var (context, before) = await Open(documents);

        var result = await Apply(documents, context, BooleanOperationType.Subtract, "pino");

        result.Refusal.Should().BeNull();
        result.ChangesScene(SceneFile).Should().BeFalse("a peça original continua na cena");
        string part = result.Changes.Single().Text;
        part.Should().Contain("new Pino(Diametro: 3, Acabamento: Acabamento.Recartilhado).Instance(\"pino_copia\", at: ")
            .And.Contain("pino_copia.IsTransientTool = true;")
            .And.NotContain("Comprimento:", "o padrão do construtor não precisa ser escrito");

        var after = await After(documents, result);
        after.Features.Should().Contain(f => f.Name == "pino");
        var bloco = after.Features.OfType<PartInstance>().Single(i => i.Name == "bloco");
        var copy = Find(bloco.Definition, "pino_copia").Should().BeAssignableTo<PartInstance>().Subject;
        var original = before.Features.OfType<PartInstance>().Single(i => i.Name == "pino");
        var world = copy.Placement.Then(bloco.Placement);
        world.Apply(Point3D.Origin).DistanceTo(original.Placement.Apply(Point3D.Origin)).Should().BeLessThan(1e-6);
        world.ApplyVector(Vector3D.UnitZ).DotProduct(original.Placement.ApplyVector(Vector3D.UnitZ)).Should().BeApproximately(1, 1e-9);
    }

    [Theory]
    [InlineData("Placement.Rotation(Vector3D.UnitX, 90).Then(Placement.Translate(new Vector3D(100, 50, 20)))")]
    [InlineData("Placement.Rotation(new Vector3D(1, 1, 0), 30).Then(Placement.Translate(new Vector3D(100, 50, 20)))")]
    public async Task Unir_ComOutraPeca_NumaPecaGiradaEmQualquerEixo_FicaNoMesmoLugar(string blocoAt)
    {
        var documents = Docs(blocoAt: blocoAt);
        var (context, _) = await Open(documents);

        var result = await Apply(documents, context, BooleanOperationType.Union, "pino");

        result.Refusal.Should().BeNull("o PartMergeCheck confere a cópia no mundo, até com ângulo qualquer");
        result.GeometryVerified.Should().BeTrue();
        result.Changes.Single().Text.Should().Contain("Placement.FromAxes(");
    }

    [Fact]
    public async Task DoisOperandos_UmSoltoEUmaPeca_EntramOsDois()
    {
        var documents = Docs();
        var (context, _) = await Open(documents);

        var result = await Apply(documents, context, BooleanOperationType.Subtract, "rasgo", "pino");

        result.Refusal.Should().BeNull();
        result.InsertedNames.Should().Equal("rasgo", "pino_copia");
        result.Changes.Select(c => c.FilePath).Should().BeEquivalentTo(new[] { SceneFile, BlocoFile });
        await After(documents, result);
    }

    [Fact]
    public async Task APropriaPecaComoOperando_Recusa()
    {
        var documents = Docs("""var bloco_2 = Add(new Bloco(Largura: 20), at: Placement.At(0, 0, 0), name: "bloco_2");""");
        var (context, _) = await Open(documents);

        var result = await Apply(documents, context, BooleanOperationType.Subtract, "bloco_2");

        result.Refusal.Should().Contain("dentro dela");
        result.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task OperandoRecusado_NadaMuda_MesmoQueOutroTenhaPassado()
    {
        var documents = Docs("""var bloco_2 = Add(new Bloco(Largura: 20), at: Placement.At(0, 0, 0), name: "bloco_2");""");
        var (context, _) = await Open(documents);

        var result = await Apply(documents, context, BooleanOperationType.Subtract, "rasgo", "bloco_2");

        result.Refusal.Should().StartWith("bloco_2:");
        result.Changes.Should().BeEmpty("tudo ou nada");
    }

    // ---------------------------------------------------------------- Sweep pelo PartSolidInserter

    [Fact]
    public async Task Sweep_NumaPecaGirada_CaminhoEUpReferenceReescritos()
    {
        var documents = Docs();
        var (context, before) = await Open(documents);
        var section = FeatureCodeGenerator.SweepSectionFromRadius(1);
        string code = FeatureCodeGenerator.GenerateSweepCode(section,
            new[] { new Point3D(80, 60, 30), new Point3D(80, 80, 30), new Point3D(95, 80, 30) }, 2, upReference: Vector3D.UnitZ);

        var insertion = await new PartSolidInserter(new RoslynSceneEvaluator(), new RoslynPartMerger())
            .InsertAsync(documents, SceneFile, code, context, PartMergeMode.UnionAfterCuts);

        insertion.Refusal.Should().BeNull();
        string part = insertion.Changes.Single().Text;
        part.Should().NotContain(".Transformed(", "as coordenadas da varredura são reescritas, não embrulhadas");
        // (80, 60, 30) no mundo → local (10, 20, 10).
        part.Should().Contain("new Point3D(10, 20, 10)");
    }

    // ---------------------------------------------------------------- O que o app grava passa no lint da IA

    /// <summary>
    /// A IA lê e edita o que o app grava no Build(): as operações na peça aberta não podem deixar código que o
    /// lint de editabilidade derrubaria (CAD0200) se a IA o tivesse escrito.
    /// </summary>
    [Theory]
    [InlineData("rasgo", "Placement.At(100, 50, 20, rotationZDegrees: 90)")]
    [InlineData("pino", "Placement.At(100, 50, 20, rotationZDegrees: 90)")]
    [InlineData("pino", "Placement.Rotation(new Vector3D(1, 1, 0), 30).Then(Placement.Translate(new Vector3D(100, 50, 20)))")]
    public async Task OCodigoGravadoNaPeca_NaoTemErroDeEditabilidade(string operand, string blocoAt)
    {
        var documents = Docs(blocoAt: blocoAt);
        var (context, _) = await Open(documents);

        var result = await Apply(documents, context, BooleanOperationType.Subtract, operand);
        result.Refusal.Should().BeNull();

        var report = AvaloniaCadEditor.Application.Ai.CadScriptEditabilityLint.Analyze(result.Changes.Single(c => c.FilePath == BlocoFile).Text);
        _output.WriteLine(string.Join("\n", report.Findings.Select(f => $"{f.Severity} {f.RuleId} L{f.Line}: {f.Message}")));
        report.Findings.Where(f => f.Severity == AvaloniaCadEditor.Application.Ai.EditabilitySeverity.Error).Should().BeEmpty();
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public PartBooleanOperationTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    // ---------------------------------------------------------------- Código do Placement

    [Fact]
    public void PlacementSource_FormasLegiveis()
    {
        PlacementSource.Code(Placement.Translate(new Vector3D(1, 2, 3))).Should().Be("Placement.Translate(new Vector3D(1, 2, 3))");
        PlacementSource.Code(Placement.At(1, 2, 3, rotationZDegrees: 90)).Should().Be("Placement.At(1, 2, 3, rotationZDegrees: 90)");
        PlacementSource.Code(Placement.Rotation(Vector3D.UnitX, 90))
            .Should().Be("Placement.FromAxes(new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), new Vector3D(0, -1, 0), new Vector3D(0, 0, 0))");
    }
}
