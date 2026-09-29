using System;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
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
