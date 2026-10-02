using System;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>Fase 3: editar parâmetros pelo inspector, ir para a definição e inserir peças.</summary>
public class PartEditingTests
{
    private const string ChapaRecord = """
        public sealed record Chapa([System.ComponentModel.DataAnnotations.Range(10, 500)] double Largura, double Comprimento = 40, double Espessura = 5) : Part
        {
            protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura);
        }
        """;

    private static readonly string[] ChapaParams = { "Largura", "Comprimento", "Espessura" };

    private static AvaloniaCadEditor.UI.ViewModels.CadViewModel CreateViewModel() =>
        TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new PureMathGeometryKernel());

    // ------------------------------------------------------------ PartSourceEditor

    [Fact]
    public void ReadArguments_MostraComoCadaParametroFoiEscrito()
    {
        string code = "var p = Add(new Chapa(100, Espessura: Medidas.E), at: Placement.At(0, 0, 0));";

        var args = PartSourceEditor.ReadArguments(code, 1, ChapaParams)!;

        args.Select(a => a.Expression).Should().Equal("100", null, "Medidas.E");
        args.Select(a => a.IsEditable).Should().Equal(true, true, false);
    }

    [Theory]
    [InlineData("Add(new Chapa(100));", "Largura", 0, "150", "Add(new Chapa(150));")]
    [InlineData("Add(new Chapa(100));", "Espessura", 2, "8", "Add(new Chapa(100, Espessura: 8));")]
    [InlineData("Add(new Chapa(100, Espessura: 3));", "Espessura", 2, "8", "Add(new Chapa(100, Espessura: 8));")]
    [InlineData("Add(new Chapa(-5));", "Largura", 0, "20", "Add(new Chapa(20));")]
    [InlineData("Add(b.Part with { Espessura = 4 });", "Espessura", 2, "6", "Add(b.Part with { Espessura = 6 });")]
    [InlineData("Add(b.Part with { Espessura = 4 });", "Largura", 0, "90", "Add(b.Part with { Espessura = 4, Largura = 90 });")]
    [InlineData("Add(p1, at: Placement.At(1, 2, 3));", "Largura", 0, "120", "Add(p1 with { Largura = 120 }, at: Placement.At(1, 2, 3));")]
    [InlineData("Scene.Add(new Chapa(100), name: \"a\");", "Largura", 0, "7.5", "Scene.Add(new Chapa(7.5), name: \"a\");")]
    public void SetArgument_ReescreveSoOArgumento(string code, string name, int index, string value, string expected)
    {
        PartSourceEditor.SetArgument(code, 1, name, index, value).Should().Be(expected);
    }

    [Fact]
    public void SetArgument_ChamadaEmVariasLinhas_EComentariosPreservados()
    {
        string code = "// topo\nvar p = Add(\n    new Chapa(100), // largura\n    at: Placement.At(0, 0, 0));\n";

        string result = PartSourceEditor.SetArgument(code, 2, "Largura", 0, "250");

        result.Should().Be("// topo\nvar p = Add(\n    new Chapa(250), // largura\n    at: Placement.At(0, 0, 0));\n");
    }

    [Fact]
    public void SetArgument_ValorCalculadoNoCodigo_ERecusado()
    {
        Action act = () => PartSourceEditor.SetArgument("Add(new Chapa(Medidas.L * 2));", 1, "Largura", 0, "10");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Medidas.L * 2*");
    }

    [Fact]
    public void TryParseValue_AceitaVirgulaEValidaInteiros()
    {
        PartSourceEditor.TryParseValue(typeof(double), "12,5", out var d, out _).Should().BeTrue();
        d.Should().Be(12.5);
        PartSourceEditor.TryParseValue(typeof(int), "3.5", out _, out var error).Should().BeFalse();
        error.Should().Contain("inteiro");
        PartSourceEditor.FormatValue(0.1).Should().Be("0.1");
        PartSourceEditor.FormatValue(100.0).Should().Be("100");
    }

    [Fact]
    public void FindPartTypes_EBuildAddStatement_UsamOMinimoDoRange()
    {
        var docs = new[]
        {
            new ScriptDocument("Pecas/Chapa.cs", "using System;\n\n" + ChapaRecord),
            new ScriptDocument("Pecas/Base.cs", "public abstract record Base(double X) : Part;")
        };

        var types = PartSourceEditor.FindPartTypes(docs);

        types.Should().ContainSingle();
        types[0].Name.Should().Be("Chapa");
        types[0].Line.Should().Be(3);
        PartSourceEditor.BuildAddStatement(types[0], 120)
            .Should().Be("Add(new Chapa(Largura: 10), at: Placement.At(120, 0, 0));");
    }

    [Fact]
    public async Task InsertTopLevelStatement_EntraAntesDosRecords_ECompila()
    {
        // O raw string herda as quebras de linha deste arquivo (CRLF no Windows); a inserção segue as do código.
        string code = "var a = Add(new Chapa(50));\n\n" + ChapaRecord.ReplaceLineEndings("\n") + "\n";

        string result = PartSourceEditor.InsertTopLevelStatement(code, "Add(new Chapa(60), at: Placement.At(100, 0, 0));");

        result.Should().StartWith("var a = Add(new Chapa(50));\nAdd(new Chapa(60), at: Placement.At(100, 0, 0));\n");
        var evaluation = await new RoslynSceneEvaluator().EvaluateAsync(result);
        evaluation.Success.Should().BeTrue(because: evaluation.ErrorMessage);
        evaluation.Features.Should().HaveCount(2);
    }

    // ------------------------------------------------------------ Mover instâncias

    [Theory]
    [InlineData("Add(new Chapa(100));", 20.0, 0.0, 0.0, "Add(new Chapa(100), at: Placement.At(20, 0, 0));")]
    [InlineData("Add(new Chapa(100), at: Placement.At(150, 0, 0));", 20.0, -5.0, 0.0, "Add(new Chapa(100), at: Placement.At(170, -5, 0));")]
    [InlineData("Add(new Chapa(100), at: Placement.At(i * 150, 0, 0));", 20.0, 0.0, 0.0, "Add(new Chapa(100), at: Placement.At(i * 150 + 20, 0, 0));")]
    [InlineData("Add(new Chapa(100), at: Placement.At(i * 150 + 20, 0, 0));", -20.0, 0.0, 0.0, "Add(new Chapa(100), at: Placement.At(i * 150, 0, 0));")]
    [InlineData("Add(new Chapa(100), at: Placement.At(10, 0));", 0.0, 0.0, 5.0, "Add(new Chapa(100), at: Placement.At(10, 0, z: 5));")]
    [InlineData("Add(new Chapa(100), at: Placement.At(0.1, 0, 0, rotationZDegrees: 45));", 0.2, 0.0, 0.0, "Add(new Chapa(100), at: Placement.At(0.3, 0, 0, rotationZDegrees: 45));")]
    [InlineData("Add(b.Part, at: Placement.At(b.World(f => f.Topo)));", 0.0, 0.0, 5.0, "Add(b.Part, at: Placement.At(b.World(f => f.Topo)).Then(Placement.Translate(new Vector3D(0, 0, 5))));")]
    [InlineData("Add(b.Part, at: Placement.At(b.World(f => f.Topo)).Then(Placement.Translate(new Vector3D(0, 0, 5))));", 10.0, 0.0, -5.0, "Add(b.Part, at: Placement.At(b.World(f => f.Topo)).Then(Placement.Translate(new Vector3D(10, 0, 0))));")]
    public void TranslateInstance_SomaNoAtSemPerderALogica(string code, double dx, double dy, double dz, string expected)
    {
        PartSourceEditor.TranslateInstance(code, 1, dx, dy, dz).Should().Be(expected);
    }

    // ------------------------------------------------------------ Cor de instâncias

    [Theory]
    [InlineData("Add(new Chapa(100));", "#FF8800", "Add(new Chapa(100), color: \"#FF8800\");")]
    [InlineData("Add(new Chapa(100), name: \"a\", color: \"#000000\");", "#FF8800", "Add(new Chapa(100), name: \"a\", color: \"#FF8800\");")]
    [InlineData("Add(new Chapa(100), at: p, name: \"a\", color: \"#000000\");", null, "Add(new Chapa(100), at: p, name: \"a\");")]
    [InlineData("Add(new Chapa(100), p, \"a\", \"#000000\");", "#FF8800", "Add(new Chapa(100), p, \"a\", \"#FF8800\");")]
    [InlineData("Add(new Chapa(100));", null, "Add(new Chapa(100));")]
    public void SetInstanceColor_TrocaAcrescentaOuRemoveOColor(string code, string? color, string expected)
    {
        PartSourceEditor.SetInstanceColor(code, 1, color).Should().Be(expected);
    }

    // ------------------------------------------------------------ Renomear

    [Theory]
    [InlineData("Add(new Chapa(100));", "Add(new Chapa(100), name: \"base\");")]
    [InlineData("Add(new Chapa(100), name: \"a\", color: \"#000000\");", "Add(new Chapa(100), name: \"base\", color: \"#000000\");")]
    [InlineData("Add(new Chapa(100), p, \"a\");", "Add(new Chapa(100), p, \"base\");")]
    public void RenameInstance_TrocaOuAcrescentaOName(string code, string expected)
    {
        PartSourceEditor.RenameInstance(code, 1, "base").Should().Be(expected);
    }

    [Fact]
    public void RenameInstance_NomeCalculado_Recusa()
    {
        var act = () => PartSourceEditor.RenameInstance("Add(new Chapa(100), name: $\"c_{i}\");", 1, "base");
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("var b = new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = \"caixa\" };\nAddFeature(b);", 2,
        "var b = new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = \"tampa\" };\nAddFeature(b);")]
    [InlineData("AddFeature(BooleanFeature.CreateDifference(\"caixa\", a, c));", 1,
        "AddFeature(BooleanFeature.CreateDifference(\"tampa\", a, c));")]
    public void RenameFeature_TrocaOLiteralDoNome(string code, int line, string expected)
    {
        PartSourceEditor.RenameFeature(code, line, "caixa", "tampa").Should().Be(expected);
    }

    // ------------------------------------------------------------ Girar instâncias

    [Theory]
    // Sem "at:": acrescenta.
    [InlineData("Add(new Chapa(100));", 0, 0, 0, 0, 0, 1, 90,
        "Add(new Chapa(100), at: Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90));")]
    // "at:" com outro tipo de posicionamento: acrescenta .Then(...).
    [InlineData("Add(new Chapa(100), at: Placement.At(150, 0, 0));", 150, 0, 0, 0, 0, 1, 90,
        "Add(new Chapa(100), at: Placement.At(150, 0, 0).Then(Placement.RotationAbout(new Point3D(150, 0, 0), Vector3D.UnitZ, 90)));")]
    // RotationAbout já existente, mesmo pivô/eixo: soma o ângulo em vez de encadear outro Then.
    [InlineData("Add(new Chapa(100), at: Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90));", 0, 0, 0, 0, 0, 1, 45,
        "Add(new Chapa(100), at: Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 135));")]
    // RotationAbout já existente, pivô diferente: não funde, encadeia mais um Then.
    [InlineData("Add(new Chapa(100), at: Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90));", 10, 0, 0, 0, 0, 1, 45,
        "Add(new Chapa(100), at: Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90).Then(Placement.RotationAbout(new Point3D(10, 0, 0), Vector3D.UnitZ, 45)));")]
    // .Then(RotationAbout(mesmo pivô/eixo)) no fim de um posicionamento qualquer: funde dentro do Then.
    [InlineData("Add(new Chapa(100), at: Placement.At(150, 0, 0).Then(Placement.RotationAbout(new Point3D(150, 0, 0), Vector3D.UnitZ, 90)));", 150, 0, 0, 0, 0, 1, 30,
        "Add(new Chapa(100), at: Placement.At(150, 0, 0).Then(Placement.RotationAbout(new Point3D(150, 0, 0), Vector3D.UnitZ, 120)));")]
    // Eixo que não é X/Y/Z: escreve o Vector3D por extenso.
    [InlineData("Add(new Chapa(100));", 0, 0, 0, 0, 0, -1, 10,
        "Add(new Chapa(100), at: Placement.RotationAbout(new Point3D(0, 0, 0), new Vector3D(0, 0, -1), 10));")]
    public void RotateInstance_SomaNoAtSemPerderALogica(string code, double px, double py, double pz, double ax, double ay, double az, double degrees, string expected)
    {
        var pivot = new Point3D(px, py, pz);
        var axis = new Vector3D(ax, ay, az);

        PartSourceEditor.RotateInstance(code, 1, pivot, axis, degrees).Should().Be(expected);
    }

    [Fact]
    public async Task RotarInstancia_ReescreveOAt_EAGiraNoModelo()
    {
        var vm = CreateViewModel();
        vm.ScriptEditor.GeneratedCSharpCode =
            "var base1 = Add(new Chapa(80), at: Placement.At(100, 0, 0));\n\n" + ChapaRecord;
        await vm.ScriptEditor.RunCodeAsync();
        var instance = vm.Sketch.Features.OfType<PartInstance>().Single();

        bool changed = vm.ScriptEditor.RotatePartInstances(new[] { instance }, new Point3D(0, 0, 0), Vector3D.UnitZ, 90);
        await vm.ScriptEditor.RunCodeAsync();

        changed.Should().BeTrue();
        vm.ScriptEditor.GeneratedCSharpCode.Should().StartWith(
            "var base1 = Add(new Chapa(80), at: Placement.At(100, 0, 0).Then(Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90)));");

        var rotated = vm.Sketch.Features.OfType<PartInstance>().Single();
        rotated.Placement.HasRotation.Should().BeTrue();
        // (100, 0, 0) girado 90° em Z em torno da origem do mundo vai para (0, 100, 0).
        rotated.Placement.Translation.X.Should().BeApproximately(0, 1e-6);
        rotated.Placement.Translation.Y.Should().BeApproximately(100, 1e-6);
    }

    // ------------------------------------------------------------ Copiar instâncias (Ctrl)

    [Fact]
    public void DuplicateInstance_CriaChamadaNovaSemVariavelNemNome_EDevolveALinhaDaCopia()
    {
        string code = "var a = Add(new Chapa(100), at: Placement.At(10, 0, 0), name: \"base\");\nvar b = 1;";

        var (source, line) = PartSourceEditor.DuplicateInstance(code, 1);

        source.Should().Be("var a = Add(new Chapa(100), at: Placement.At(10, 0, 0), name: \"base\");\nAdd(new Chapa(100), at: Placement.At(10, 0, 0));\nvar b = 1;");
        line.Should().Be(2);
        PartSourceEditor.TranslateInstance(source, line, 50, 0, 0).Should().Be(
            "var a = Add(new Chapa(100), at: Placement.At(10, 0, 0), name: \"base\");\nAdd(new Chapa(100), at: Placement.At(60, 0, 0));\nvar b = 1;");
    }

    [Fact]
    public async Task CopiarInstancias_MantemOsOriginaisECriaAsCopiasPosicionadas()
    {
        var vm = CreateViewModel();
        vm.ScriptEditor.GeneratedCSharpCode =
            "Add(new Chapa(80), at: Placement.At(0, 0, 0));\n" +
            "Add(new Chapa(60), at: Placement.At(0, 100, 0));\n\n" + ChapaRecord;
        await vm.ScriptEditor.RunCodeAsync();
        var originais = vm.Sketch.Features.OfType<PartInstance>().ToList();

        bool changed = vm.ScriptEditor.CopyPartInstances(originais,
            (source, line) => PartSourceEditor.TranslateInstance(source, line, 200, 0, 0));
        await vm.ScriptEditor.RunCodeAsync();

        changed.Should().BeTrue();
        vm.ScriptEditor.GeneratedCSharpCode.Should().StartWith(
            "Add(new Chapa(80), at: Placement.At(0, 0, 0));\n" +
            "Add(new Chapa(80), at: Placement.At(200, 0, 0));\n" +
            "Add(new Chapa(60), at: Placement.At(0, 100, 0));\n" +
            "Add(new Chapa(60), at: Placement.At(200, 100, 0));\n");
        vm.Sketch.Features.OfType<PartInstance>().Should().HaveCount(4);
    }

    [Fact]
    public void RotateInstance_LinhaSemAddCall_Lanca()
    {
        Action act = () => PartSourceEditor.RotateInstance("var a = 1;", 1, Point3D.Origin, Vector3D.UnitZ, 90);

        act.Should().Throw<InvalidOperationException>().WithMessage("*linha 1*");
    }

    [Fact]
    public void FindStatementSpan_AchaAInstrucaoDoAddNaLinha()
    {
        string code = "var a = 1;\nfor (int i = 0; i < 2; i++)\n    Add(new Chapa(1));\n";

        var span = PartSourceEditor.FindStatementSpan(code, 3)!.Value;

        code.Substring(span.Start, span.Length).Should().Be("Add(new Chapa(1));");
    }

    [Fact]
    public async Task MoverInstancias_ReescreveOAt_EAPecaDependenteAcompanha()
    {
        var vm = CreateViewModel();
        vm.ScriptEditor.GeneratedCSharpCode =
            "var base1 = Add(new Chapa(80));\n" +
            "var tampa = Add(base1.Part with { Espessura = 4 }, at: Placement.At(0, 0, base1.Part.Espessura));\n\n" +
            ChapaRecord;
        await vm.ScriptEditor.RunCodeAsync();

        bool changed = vm.ScriptEditor.MovePartInstances(vm.Sketch.Features.OfType<PartInstance>().ToList(), new Vector3D(100, 0, 5));
        await vm.ScriptEditor.RunCodeAsync();

        changed.Should().BeTrue();
        vm.ScriptEditor.GeneratedCSharpCode.Should().StartWith(
            "var base1 = Add(new Chapa(80), at: Placement.At(100, 0, 5));\n" +
            "var tampa = Add(base1.Part with { Espessura = 4 }, at: Placement.At(100, 0, base1.Part.Espessura + 5));");
        var moved = vm.Sketch.Features.OfType<PartInstance>().ToList();
        moved[0].Placement.Translation.X.Should().Be(100);
        moved[1].Placement.Translation.Z.Should().Be(10);
    }

    // ------------------------------------------------------------ Mover/girar/copiar feature solta (Transformed)

    [Theory]
    // Ainda não é Transformed: envolve.
    [InlineData("AddFeature(prism);", 5, 0, 0,
        "AddFeature(prism.Transformed(Placement.Translate(new Vector3D(5, 0, 0))));")]
    // Já é Transformed(Placement.Translate(...)): soma no deslocamento existente.
    [InlineData("AddFeature(prism.Transformed(Placement.Translate(new Vector3D(5, 0, 0))));", 2, 0, 0,
        "AddFeature(prism.Transformed(Placement.Translate(new Vector3D(7, 0, 0))));")]
    // Já é Transformed com outro tipo de placement: encadeia .Then.
    [InlineData("AddFeature(prism.Transformed(Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90)));", 5, 0, 0,
        "AddFeature(prism.Transformed(Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90).Then(Placement.Translate(new Vector3D(5, 0, 0)))));")]
    public void TranslateFeature_SomaNoTransformedSemPerderALogica(string code, double dx, double dy, double dz, string expected)
    {
        PartSourceEditor.TranslateFeature(code, 1, dx, dy, dz).Should().Be(expected);
    }

    [Theory]
    [InlineData("AddFeature(prism);", 90,
        "AddFeature(prism.Transformed(Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90)));")]
    [InlineData("AddFeature(prism.Transformed(Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90)));", 45,
        "AddFeature(prism.Transformed(Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 135)));")]
    public void RotateFeature_SomaNoTransformedSemPerderALogica(string code, double degrees, string expected)
    {
        PartSourceEditor.RotateFeature(code, 1, new Point3D(0, 0, 0), Vector3D.UnitZ, degrees).Should().Be(expected);
    }

    // ------------------------------------------------------------ Mover/girar uma feature usada por outra (ex.: booleana)
    //
    // Regressão: mover a ferramenta/base de uma BooleanFeature reescrevia só o argumento do
    // AddFeature (AddFeature(x) -> AddFeature(x.Transformed(...))), criando um sólido novo e visível
    // no lugar de destino - a booleana continuava usando o "x" original, parado, então o corte não
    // acompanhava. A correção move a transformação para a DECLARAÇÃO de x quando x é usado em outro
    // lugar do script.

    [Fact]
    public void TranslateFeature_UsadaPorBooleana_MoveADeclaracaoNaoOAddFeature()
    {
        string code =
            "var box = new BoxFeature(Point3D.Origin, 10, 10, 10);\n" +
            "AddFeature(box);\n" +
            "var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4);\n" +
            "AddFeature(tool);\n" +
            "var cut = BooleanFeature.CreateDifference(\"cut\", box, tool);\n" +
            "AddFeature(cut);\n";

        string result = PartSourceEditor.TranslateFeature(code, 4, 5, 0, 0);

        result.Should().Contain(
            "var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4).Transformed(Placement.Translate(new Vector3D(5, 0, 0)));");
        result.Should().Contain("AddFeature(tool);", "o AddFeature não muda - ele já só referenciava a variável");
        result.Should().NotContain("AddFeature(tool.Transformed", "não deve criar um sólido novo separado da booleana");
        result.Should().Contain("BooleanFeature.CreateDifference(\"cut\", box, tool);", "a booleana continua referenciando a mesma variável, agora já na posição nova");
    }

    [Fact]
    public void TranslateFeature_UsadaPorBooleana_MoverDeNovoSomaNoDeslocamentoDaDeclaracao()
    {
        string code =
            "var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4);\n" +
            "AddFeature(tool);\n" +
            "var cut = BooleanFeature.CreateDifference(\"cut\", box, tool);\n";

        string once = PartSourceEditor.TranslateFeature(code, 2, 5, 0, 0);
        string twice = PartSourceEditor.TranslateFeature(once, 2, 3, 0, 0);

        twice.Should().Contain(
            "var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4).Transformed(Placement.Translate(new Vector3D(8, 0, 0)));");
    }

    [Fact]
    public void RotateFeature_UsadaPorBooleana_MoveADeclaracaoNaoOAddFeature()
    {
        string code =
            "var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4);\n" +
            "AddFeature(tool);\n" +
            "var cut = BooleanFeature.CreateDifference(\"cut\", box, tool);\n";

        string result = PartSourceEditor.RotateFeature(code, 2, new Point3D(0, 0, 0), Vector3D.UnitZ, 90);

        result.Should().Contain(
            "var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4).Transformed(Placement.RotationAbout(new Point3D(0, 0, 0), Vector3D.UnitZ, 90));");
        result.Should().Contain("AddFeature(tool);");
    }

    [Fact]
    public void TranslateFeature_CenaJaInconsistente_RepataDevolvendoOAddFeatureParaAVariavelSimples()
    {
        // Estado quebrado: uma versão anterior moveu "tool" reescrevendo o AddFeature, então a
        // booleana ficou usando o sólido original e o Transformed ficou "solto" e visível.
        string code =
            "var box = new BoxFeature(Point3D.Origin, 10, 10, 10);\n" +
            "AddFeature(box);\n" +
            "var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4);\n" +
            "AddFeature(tool.Transformed(Placement.Translate(new Vector3D(1, 0, 0))));\n" +
            "var cut = BooleanFeature.CreateDifference(\"cut\", box, tool);\n" +
            "AddFeature(cut);\n";

        string result = PartSourceEditor.TranslateFeature(code, 4, 2, 0, 0);

        result.Should().Contain(
            "var tool = new BoxFeature(new Point3D(20, 0, 0), 4, 4, 4).Transformed(Placement.Translate(new Vector3D(3, 0, 0)));",
            "o deslocamento que estava solto no AddFeature (1) soma com o novo (2) na declaração");
        result.Should().Contain("AddFeature(tool);", "o AddFeature volta a só referenciar a variável - conserta o estado inconsistente");
        result.Should().NotContain("AddFeature(tool.Transformed");
    }

    [Fact]
    public void HoistTransformedAddFeatures_BooleanaCriadaDepoisDeMover_LevaOPlacementParaADeclaracao()
    {
        // Caso relatado: a caixa foi movida (AddFeature(x.Transformed(P))) e só depois virou base de
        // uma booleana gerada com "box_0578" - o resultado aparecia na posição original da caixa.
        string code =
            "var box_0578 = new BoxFeature(baseCorner: new Point3D(-6.05, 10, 25.99999), width: 20.5, length: 16.5, height: 13)\n" +
            "{\n" +
            "    Name = \"box_0578\"\n" +
            "};\n" +
            "AddFeature(box_0578.Transformed(Placement.Translate(new Vector3D(-14.565334, 0.5, 0.584834))));\n" +
            "var cylinder_3345 = new CylinderFeature(globalBaseCenter: new Point3D(-5.61533, 18.75, 39.58398), globalDirection: new Vector3D(0, 0, 1), radius: 3, height: -9);\n" +
            "AddFeature(cylinder_3345);\n" +
            "var boolOp_ec68 = new BooleanFeature(name: \"boolOp_ec68\", operation: BooleanOperationType.Subtract, baseFeature: box_0578, toolFeatures: new IFeature[] { cylinder_3345 });\n" +
            "AddFeature(boolOp_ec68);\n";

        string result = PartSourceEditor.HoistTransformedAddFeatures(code);

        result.Should().Contain(
            "}.Transformed(Placement.Translate(new Vector3D(-14.565334, 0.5, 0.584834)));\n" +
            "AddFeature(box_0578);\n");
        result.Should().NotContain("AddFeature(box_0578.Transformed");
        result.Should().Contain("baseFeature: box_0578,");
    }

    [Fact]
    public void HoistTransformedAddFeatures_SemOutroUso_NaoMexe()
    {
        string code =
            "var box = new BoxFeature(Point3D.Origin, 10, 10, 10);\n" +
            "AddFeature(box.Transformed(Placement.Translate(new Vector3D(1, 0, 0))));\n";

        PartSourceEditor.HoistTransformedAddFeatures(code).Should().Be(code);
    }

    [Fact]
    public void TranslateFeature_UsadaComMetodoEmOutroLugar_Lanca()
    {
        // "cyl" é usado como cyl.GetFaceCenter(...) fora do AddFeature: mudar o tipo estático da
        // declaração para TransformedFeature poderia quebrar essa chamada, então não arriscamos.
        string code =
            "var cyl = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, 5, 10);\n" +
            "AddFeature(cyl);\n" +
            "var h = cyl.GetFaceCenter(FaceType.Top);\n";

        Action act = () => PartSourceEditor.TranslateFeature(code, 2, 1, 0, 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cyl*");
    }

    [Fact]
    public void TranslateFeature_CasoRelatado_MoverFerramentaDeBooleanaComVariasBasesEFerramentas()
    {
        // Reprodução literal do bug relatado: mover cylinder_ddc7 (usado como uma das ferramentas de
        // boolOp_90c4, que também tem 2 bases) recriava o sólido no AddFeature em vez de mover o
        // corte - a booleana continuava usando o cylinder_ddc7 original, parado.
        string code =
            "var cylinder_df12 = new CylinderFeature(globalBaseCenter: new Point3D(1, 5, 0), globalDirection: new Vector3D(0, 0, 1), radius: 8.06226, height: 11.5)\n" +
            "{\n" +
            "    Name = \"cylinder_df12\"\n" +
            "};\n" +
            "AddFeature(cylinder_df12);\n" +
            "var cylinder_6627 = new CylinderFeature(globalBaseCenter: new Point3D(8, -6, 0), globalDirection: new Vector3D(0, 0, 1), radius: 7.28011, height: 12)\n" +
            "{\n" +
            "    Name = \"cylinder_6627\"\n" +
            "};\n" +
            "AddFeature(cylinder_6627);\n" +
            "var cylinder_ddc7 = new CylinderFeature(globalBaseCenter: new Point3D(15, 5, 0), globalDirection: new Vector3D(0, 0, 1), radius: 7.24158, height: 13)\n" +
            "{\n" +
            "    Name = \"cylinder_ddc7\"\n" +
            "};\n" +
            "AddFeature(cylinder_ddc7);\n" +
            "var cylinder_0193 = new CylinderFeature(globalBaseCenter: new Point3D(2.57287, 12.90735, 6), globalDirection: new Vector3D(0.09802, 0.99518, 0), radius: 2.23607, height: -42.66361)\n" +
            "{\n" +
            "    Name = \"cylinder_0193\"\n" +
            "};\n" +
            "AddFeature(cylinder_0193);\n" +
            "var boolOp_90c4 = new BooleanFeature(name: \"boolOp_90c4\", operation: BooleanOperationType.Subtract, baseFeatures: new IFeature[] { cylinder_df12, cylinder_6627 }, toolFeatures: new IFeature[] { cylinder_ddc7, cylinder_0193 });\n" +
            "AddFeature(boolOp_90c4);\n";

        string result = PartSourceEditor.TranslateFeature(code, 15, -6.427131, -6.092654, 0);

        result.Should().Contain(
            "new CylinderFeature(globalBaseCenter: new Point3D(15, 5, 0), globalDirection: new Vector3D(0, 0, 1), radius: 7.24158, height: 13)\n" +
            "{\n" +
            "    Name = \"cylinder_ddc7\"\n" +
            "}.Transformed(Placement.Translate(new Vector3D(-6.427131, -6.092654, 0)))",
            "o movimento vai para a declaração de cylinder_ddc7");
        result.Should().Contain("AddFeature(cylinder_ddc7);", "o AddFeature não vira um sólido novo separado da booleana");
        result.Should().NotContain("AddFeature(cylinder_ddc7.Transformed");
        result.Should().Contain(
            "toolFeatures: new IFeature[] { cylinder_ddc7, cylinder_0193 }",
            "a booleana continua com as mesmas duas ferramentas - agora cylinder_ddc7 já é a versão movida");
    }

    [Fact]
    public void TranslateFeature_UsadaEmOutroLugarSemDeclaracaoNesteArquivo_LancaComMensagemClara()
    {
        string code =
            "AddFeature(cyl);\n" +
            "var h = new IFeature[] { cyl };\n";

        Action act = () => PartSourceEditor.TranslateFeature(code, 1, 1, 0, 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cyl*");
    }

    [Fact]
    public void CopyFeature_AcrescentaVarETransformedComNomeUnico()
    {
        string code = "AddFeature(prism);";

        var (updated, line) = PartSourceEditor.CopyFeature(code, 1, "Placement.Translate(new Vector3D(5, 0, 0))");

        updated.Should().Be(
            "AddFeature(prism);\n" +
            "var prism_copia1 = prism.Transformed(Placement.Translate(new Vector3D(5, 0, 0)), name: \"prism_copia1\");\n" +
            "AddFeature(prism_copia1);");
        line.Should().Be(3);
    }

    [Fact]
    public void CopyFeature_NomeJaExistente_UsaOProximoDisponivel()
    {
        string code = "AddFeature(prism);\nvar prism_copia1 = 1;";

        var (updated, _) = PartSourceEditor.CopyFeature(code, 1, "Placement.Translate(new Vector3D(1, 0, 0))");

        updated.Should().Contain("var prism_copia2 = prism.Transformed(");
    }

    [Fact]
    public void CopyFeature_DeUmaCopia_UsaONomeDaOrigemComoBase()
    {
        string code = "AddFeature(prism.Transformed(Placement.Translate(new Vector3D(5, 0, 0)), name: \"prism_copia1\"));";

        var (updated, _) = PartSourceEditor.CopyFeature(code, 1, "Placement.Translate(new Vector3D(1, 0, 0))");

        updated.Should().Contain("var prism_copia2 = prism.Transformed(Placement.Translate(new Vector3D(5, 0, 0)), name: \"prism_copia1\").Transformed(Placement.Translate(new Vector3D(1, 0, 0)), name: \"prism_copia2\");");
    }

    [Fact]
    public void TranslateFeature_LinhaSemAddFeature_Lanca()
    {
        Action act = () => PartSourceEditor.TranslateFeature("var a = 1;", 1, 1, 0, 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*linha 1*");
    }

    [Fact]
    public async Task CopiarRotacionandoFeatureSolta_ComPlanoProprio_CriaACopiaEMantemOOriginal()
    {
        // Regressão: girar com Ctrl (copiar) o prism_60e9 do usuário (PrismFeature com WorkPlane
        // próprio) mostrava a projeção, mas nada era criado, porque só instâncias de peça (Add(...))
        // podiam ser copiadas/giradas - features soltas (AddFeature) eram sempre ignoradas.
        var vm = CreateViewModel();
        vm.ScriptEditor.GeneratedCSharpCode = """
            var prism_60e9_verts = new[]
            {
                new Point3D(40, 10, 0),
                new Point3D(40, 15, 0),
                new Point3D(0, 15, 0),
                new Point3D(0, 5, 0),
                new Point3D(10, 5, 0),
                new Point3D(40, 5, 0)
            };
            var prism_60e9_plane = new WorkPlane(new Point3D(-20, 0, 20), new Vector3D(0, 0, 1), new Vector3D(1, 0, 0));
            var prism_60e9 = new PrismFeature(prism_60e9_verts, height: 4, plane: prism_60e9_plane)
            {
                Name = "prism_60e9"
            };
            AddFeature(prism_60e9);
            """;
        await vm.ScriptEditor.RunCodeAsync();
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        var original = vm.Sketch.Features.Single();

        bool changed = vm.ScriptEditor.CopyLooseFeatures(new[] { original },
            PartSourceEditor.RotationCall(new Point3D(20, 10, 20), Vector3D.UnitZ, 90));
        await vm.ScriptEditor.RunCodeAsync();

        changed.Should().BeTrue();
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        vm.Sketch.Features.Should().HaveCount(2);
        vm.Sketch.Features.Should().Contain(f => f.Name == "prism_60e9");
        vm.Sketch.Features.Should().Contain(f => f.Name == "prism_60e9_copia1");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("prism_60e9.Transformed(Placement.RotationAbout(");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("AddFeature(prism_60e9_copia1);");

        // O original continua exatamente onde estava.
        var originalDepois = vm.Sketch.Features.Single(f => f.Name == "prism_60e9");
        originalDepois.GetVertices().Should().BeEquivalentTo(original.GetVertices());
    }

    [Fact]
    public async Task MoverFeatureSolta_ComPlanoProprio_DeslocaOsVerticesExatamenteNoMundo()
    {
        // Regressão: mover (sem cópia) reescrevia coordenadas locais ao plano como se fossem do
        // mundo, deslocando a peça errado. Transformed aplica sobre os vértices já no mundo
        // (Prism.GetVertices()), então funciona igual para qualquer feature.
        var vm = CreateViewModel();
        vm.ScriptEditor.GeneratedCSharpCode = """
            var verts = new[] { new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 5, 0), new Point3D(0, 5, 0) };
            var plane = new WorkPlane(new Point3D(20, 0, 0), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0));
            var prism = new PrismFeature(verts, height: 3, plane: plane) { Name = "prism" };
            AddFeature(prism);
            """;
        await vm.ScriptEditor.RunCodeAsync();
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        var before = vm.Sketch.Features.Single().GetVertices().ToList();

        bool changed = vm.ScriptEditor.MoveLooseFeatures(vm.Sketch.Features.ToList(), new Vector3D(5, 0, 0));
        await vm.ScriptEditor.RunCodeAsync();

        changed.Should().BeTrue();
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        var after = vm.Sketch.Features.Single().GetVertices().ToList();
        after.Should().HaveCount(before.Count);
        for (int i = 0; i < before.Count; i++)
        {
            after[i].Should().Be(before[i] + new Vector3D(5, 0, 0));
        }
    }

    // ------------------------------------------------------------ Inspector e editor

    [Fact]
    public async Task Inspector_EditaParametro_ReescreveOCodigo_ReexecutaEMantemSelecao()
    {
        var vm = CreateViewModel();
        vm.ScriptEditor.GeneratedCSharpCode = "var c = Add(new Chapa(100), at: Placement.At(0, 0, 0));\n\n" + ChapaRecord;
        await vm.ScriptEditor.RunCodeAsync();
        vm.SelectionService.SelectFeature(vm.Sketch.Features.OfType<PartInstance>().Single());

        var espessura = vm.PropertyInspector.PartParameters.Single(p => p.Name == "Espessura");
        espessura.IsEditable.Should().BeTrue();
        espessura.Text = "8";
        await espessura.ApplyAsync();

        espessura.Error.Should().BeNull();
        vm.ScriptEditor.GeneratedCSharpCode.Should().StartWith("var c = Add(new Chapa(100, Espessura: 8), at:");
        var selected = vm.SelectionService.SelectedObject.Should().BeAssignableTo<PartInstance>().Subject;
        selected.Name.Should().Be("Chapa_1");
        selected.Part.GetParameters().Single(p => p.Name == "Espessura").Value.Should().Be(8.0);
        vm.PropertyInspector.PartParameters.Single(p => p.Name == "Espessura").Text.Should().Be("8");
    }

    [Fact]
    public async Task Inspector_ValorForaDoRange_MostraErroSemMexerNoCodigo()
    {
        var vm = CreateViewModel();
        string code = "for (int i = 0; i < 3; i++)\n    Add(new Chapa(100), at: Placement.At(i * 150, 0, 0));\n\n" + ChapaRecord;
        vm.ScriptEditor.GeneratedCSharpCode = code;
        await vm.ScriptEditor.RunCodeAsync();
        vm.SelectionService.SelectFeature(vm.Sketch.Features.OfType<PartInstance>().First());

        vm.PropertyInspector.PartNote.Should().Contain("linha 2").And.Contain("3 instâncias");
        var largura = vm.PropertyInspector.PartParameters.Single(p => p.Name == "Largura");
        largura.Text = "5";
        await largura.ApplyAsync();

        largura.Error.Should().Contain("entre 10 e 500");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Be(code);
    }

    [Fact]
    public async Task Inspector_ParametroCalculado_FicaSomenteLeitura()
    {
        var vm = CreateViewModel();
        vm.ScriptEditor.GeneratedCSharpCode = "double l = 80;\nAdd(new Chapa(l));\n\n" + ChapaRecord;
        await vm.ScriptEditor.RunCodeAsync();
        vm.SelectionService.SelectFeature(vm.Sketch.Features.OfType<PartInstance>().Single());

        var largura = vm.PropertyInspector.PartParameters.Single(p => p.Name == "Largura");
        largura.IsEditable.Should().BeFalse();
        largura.Hint.Should().Contain("calculado no código: l");
        largura.Text.Should().Be("80");
    }

    [Fact]
    public async Task AbrirDefinicao_TrocaParaOArquivoDaPecaENaAbaCodigo()
    {
        using var folder = new SceneTestFolder();
        var project = folder.CreateScene();
        SceneProjectTemplate.CreatePartFile(project, "Suporte");
        folder.Write("Cena.cs", "Add(new Suporte(), at: Placement.At(0, 0, 0));\n");
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        vm.SelectionService.SelectFeature(vm.Sketch.Features.OfType<PartInstance>().Single());

        vm.PropertyInspector.OpenPartDefinitionCommand.Execute(null);

        vm.ScriptEditor.ActiveFile.Should().Be("Pecas/Suporte.cs");
        vm.ScriptEditor.RevealLine!.Line.Should().Be(2);
        vm.SidebarTabIndex.Should().Be(AvaloniaCadEditor.UI.ViewModels.CadViewModel.CodeTabIndex);
    }

    [Fact]
    public async Task InserirPeca_AcrescentaNaCenaAoLadoDoModelo_ESeleciona()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        vm.ScriptEditor.NewFileName = "Suporte";
        await vm.ScriptEditor.CreateFileAsync();

        vm.ScriptEditor.PartTypes.Should().Equal("Suporte");
        await vm.ScriptEditor.InsertPartAsync("Suporte");

        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        vm.ScriptEditor.ActiveFile.Should().Be("Cena.cs");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("Add(new Suporte(), at: Placement.At(110, 0, 0));");
        vm.SelectionService.SelectedObject.Should().BeAssignableTo<PartInstance>()
            .Which.Name.Should().Be("Suporte_1");
        System.IO.File.ReadAllText(folder.FullPath("Cena.cs")).Should().Contain("new Suporte()");
    }
}
