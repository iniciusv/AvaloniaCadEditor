using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>Instâncias em operações booleanas, criação de arquivos de peça e erros visíveis no editor.</summary>
public class SceneEditingTests
{
    private static AvaloniaCadEditor.UI.ViewModels.CadViewModel CreateViewModel() =>
        TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new PureMathGeometryKernel());

    // ------------------------------------------------------------ Booleanas com instâncias

    [Fact]
    public void InstanciaTransladada_PodeSerFuradaPorOperacaoBooleana()
    {
        using var _ = SceneHost.Begin(new SceneBuilder());
        var placa = Scene.Add(new Placa(100, 100, 10), at: Placement.At(200, 0, 0));
        var furo = new CylinderFeature(new Point3D(250, 50, 0), Vector3D.UnitZ, 10, 10) { IsTransientTool = true };
        var furada = BooleanFeature.CreateDifference("furada", placa, furo);
        var kernel = new OpenCascadeGeometryKernel();

        var semFuro = kernel.BuildFeatureMesh(placa);
        var comFuro = kernel.BuildFeatureMesh(furada);

        comFuro.TriangleCount.Should().BeGreaterThan(semFuro.TriangleCount, "o furo acrescenta faces à placa");
        MinX(comFuro).Should().BeApproximately(200f, 1e-3f, "o sólido foi movido junto com a instância");
    }

    /// <summary>Peça de teste cuja definição usa fillet — não pode virar operando de booleana girada
    /// (o kernel reconstrói extrusões/caixas/cilindros girados, mas não um fillet).</summary>
    private sealed record PlacaComFillet(double Lado) : Part
    {
        protected override IFeature Build()
        {
            var box = new BoxFeature(Point3D.Origin, Lado, Lado, 5);
            return new FilletFeature("filete", box, 1) { ApplyToAllEdges = true };
        }
    }

    [Fact]
    public void InstanciaGirada_SemFilletOuMalha_PodeSerOperandoBooleano()
    {
        // O kernel reconstrói o sólido já em coordenadas do mundo (rotação inclusa) para
        // extrusões/caixas/cilindros/booleanas comuns, então uma instância girada de uma peça
        // assim pode ser operando normalmente.
        using var _ = SceneHost.Begin(new SceneBuilder());
        var girada = Scene.Add(new Placa(100), at: Placement.At(0, 0, 0, rotationZDegrees: 45));
        var furo = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, 5, 10);

        Action act = () => BooleanFeature.CreateDifference("x", girada, furo);

        act.Should().NotThrow();
    }

    [Fact]
    public void InstanciaGiradaComFillet_EmOperacaoBooleana_FalhaComMensagemClara()
    {
        using var _ = SceneHost.Begin(new SceneBuilder());
        var girada = Scene.Add(new PlacaComFillet(100), at: Placement.At(0, 0, 0, rotationZDegrees: 45));
        var furo = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, 5, 10);

        Action act = () => BooleanFeature.CreateDifference("x", girada, furo);

        act.Should().Throw<NotSupportedException>().WithMessage("*PlacaComFillet_1*girada*fillet*");
    }

    [Fact]
    public async Task InstanciaGiradaComFilletNoScript_ErroApontaALinhaDaOperacao()
    {
        string code = """
            var p = Add(new Chapa(50), at: Placement.At(0, 0, 0, rotationZDegrees: 45));
            var c = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, 5, 10) { IsTransientTool = true };
            AddFeature(c);
            AddFeature(BooleanFeature.CreateDifference("x", p, c));

            public sealed record Chapa(double Lado) : Part
            {
                protected override IFeature Build()
                {
                    var box = new BoxFeature(Point3D.Origin, Lado, Lado, 5);
                    return new FilletFeature("filete", box, 1) { ApplyToAllEdges = true };
                }
            }
            """;

        var result = await new RoslynSceneEvaluator().EvaluateAsync(code);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cena.cs(4,").And.Contain("girada").And.Contain("fillet");
    }

    // ------------------------------------------------------------ Novo arquivo de peça

    [Fact]
    public async Task CreatePartFile_CriaPecaCompilavelEmPecas()
    {
        using var folder = new SceneTestFolder();
        var project = folder.CreateScene();

        string relative = SceneProjectTemplate.CreatePartFile(project, "Suporte");
        File.AppendAllText(folder.FullPath("Cena.cs"), "Add(new Suporte(), at: Placement.At(100, 0, 0));\n");

        relative.Should().Be("Pecas/Suporte.cs");
        File.ReadAllText(folder.FullPath(relative)).Should().Contain("public sealed record Suporte(");
        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());
        result.Success.Should().BeTrue(because: result.ErrorMessage);
        result.Features.Should().HaveCount(2);
    }

    [Fact]
    public void CreatePartFile_RecusaArquivoExistenteOuForaDaCena()
    {
        using var folder = new SceneTestFolder();
        var project = folder.CreateScene();

        Action existente = () => SceneProjectTemplate.CreatePartFile(project, "Medidas");
        Action fora = () => SceneProjectTemplate.CreatePartFile(project, "../Fora");
        Action vazio = () => SceneProjectTemplate.CreatePartFile(project, "  ");

        existente.Should().Throw<InvalidOperationException>().WithMessage("*já existe*");
        fora.Should().Throw<InvalidOperationException>().WithMessage("*fora da cena*");
        vazio.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("suporte-lateral", "SuporteLateral")]
    [InlineData("flange", "Flange")]
    [InlineData("3d", "Peca3d")]
    [InlineData("***", "NovaPeca")]
    public void ToTypeName_GeraIdentificadorValido(string input, string expected)
    {
        SceneProjectTemplate.ToTypeName(input).Should().Be(expected);
    }

    // ------------------------------------------------------------ Editor: erros e novo arquivo

    [Fact]
    public async Task ErroNaCena_FicaVisivelNaLinhaENoResumo_ESomeAoCorrigir()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        string original = vm.ScriptEditor.GeneratedCSharpCode;

        vm.ScriptEditor.GeneratedCSharpCode = "var ok = 1;\nvar quebrado = naoExiste;\n";
        await vm.ScriptEditor.RunCodeAsync();

        vm.ScriptEditor.HasErrors.Should().BeTrue();
        vm.ScriptEditor.ActiveFileErrorLines.Should().Equal(2);
        vm.ScriptEditor.ErrorSummary.Should().StartWith("Cena.cs(2): ");
        vm.Sketch.Features.Should().ContainSingle("o modelo exibido continua sendo o último válido");

        vm.ScriptEditor.ActiveFile = "Pecas/Medidas.cs";
        vm.ScriptEditor.ActiveFileErrorLines.Should().BeEmpty();
        vm.ScriptEditor.GoToFirstError();
        vm.ScriptEditor.ActiveFile.Should().Be("Cena.cs");

        vm.ScriptEditor.GeneratedCSharpCode = original;
        await vm.ScriptEditor.RunCodeAsync();
        vm.ScriptEditor.HasErrors.Should().BeFalse();
        vm.ScriptEditor.ErrorSummary.Should().BeNull();
    }

    [Fact]
    public async Task NovoArquivo_PeloEditor_CriaAbreEExecuta()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);

        vm.ScriptEditor.NewFileName = "Suporte";
        await vm.ScriptEditor.CreateFileAsync();

        vm.ScriptEditor.ProjectFiles.Should().Contain("Pecas/Suporte.cs");
        vm.ScriptEditor.ActiveFile.Should().Be("Pecas/Suporte.cs");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("record Suporte(");
        vm.ScriptEditor.NewFileName.Should().BeEmpty();
        vm.ScriptEditor.HasErrors.Should().BeFalse();
    }

    private static float MinX(TriangleMesh mesh) =>
        Enumerable.Range(0, mesh.VertexCount).Min(i => mesh.Positions[i * 3]);
}
