using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

public class SceneCheckServiceTests
{
    private static SceneCheckService CreateService() =>
        new(new RoslynSceneEvaluator(), new OpenCascadeGeometryKernel());

    [Fact]
    public async Task CenaValida_ComGeometria_PassaEResumeOResultado()
    {
        using var folder = new SceneTestFolder("Valida");
        var project = folder.CreateScene();

        var report = await CreateService().CheckAsync(project, checkGeometry: true);

        report.Success.Should().BeTrue(because: report.Format());
        report.GeometryChecked.Should().BeTrue();
        report.FeatureCount.Should().Be(1);
        report.Format().Should().Contain("OK: 1 feature(s), geometria validada");
    }

    [Fact]
    public async Task ErroEmArquivoDePeca_FalhaApontandoArquivoELinha()
    {
        using var folder = new SceneTestFolder();
        var project = folder.CreateScene();
        folder.Write("Pecas/Medidas.cs", "public static class Medidas\n{\n    public const double Espessura = \"dez\";\n}\n");

        var report = await CreateService().CheckAsync(project, checkGeometry: true);

        report.Success.Should().BeFalse();
        report.Errors.Should().Contain(d => d.FilePath == "Pecas/Medidas.cs" && d.Line == 3);
        report.Format().Should().Contain("Pecas/Medidas.cs(3,").And.Contain("FALHOU");
    }

    [Fact]
    public async Task SemGeometria_NaoUsaOKernel()
    {
        using var folder = new SceneTestFolder();
        var project = folder.CreateScene();

        var report = await new SceneCheckService(new RoslynSceneEvaluator()).CheckAsync(project, checkGeometry: true);

        report.Success.Should().BeTrue(because: report.Format());
        report.GeometryChecked.Should().BeFalse();
    }
}

/// <summary>A origem de cada feature vem dos atributos Caller* de AddFeature.</summary>
public class FeatureOriginTests
{
    [Fact]
    public async Task AddFeature_RegistraArquivoLinhaEVariavel()
    {
        string code =
            "var caixa = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 10, length: 10, height: 10) { Name = \"Caixa Bonita\" };\n" +
            "AddFeature(caixa);";

        var result = await new RoslynSceneEvaluator().EvaluateAsync(code);

        result.Success.Should().BeTrue(because: result.ErrorMessage);
        var feature = result.Features.Single();
        var origin = FeatureOrigin.Of(feature);
        origin.Should().NotBeNull();
        origin!.FilePath.Should().Be(ScriptDocument.DefaultSceneFile);
        origin.Line.Should().Be(2);
        origin.Variable.Should().Be("caixa");
        FeatureOrigin.VariableName(feature).Should().Be("caixa", "o código se refere à variável, não ao Name");
    }

    [Fact]
    public async Task AddFeatureComExpressao_UsaNameComoNome()
    {
        var docs = new[]
        {
            new ScriptDocument("Pecas/Fabrica.cs",
                "public static class Fabrica\n{\n" +
                "    public static void Cubo(string nome) =>\n" +
                "        AddFeature(new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 5, length: 5, height: 5) { Name = nome });\n" +
                "}\n"),
            new ScriptDocument("Cena.cs", "Fabrica.Cubo(\"cubo_1\");"),
        };

        var result = await new RoslynSceneEvaluator().EvaluateAsync(docs);

        result.Success.Should().BeTrue(because: result.ErrorMessage);
        var feature = result.Features.Single();
        var origin = FeatureOrigin.Of(feature)!;
        origin.FilePath.Should().Be("Pecas/Fabrica.cs", "AddFeature foi chamado dentro do arquivo da peça");
        origin.Line.Should().Be(4);
        origin.Variable.Should().BeNull();
        FeatureOrigin.VariableName(feature).Should().Be("cubo_1");
    }

    [Fact]
    public void FeatureCriadaForaDeScript_NaoTemOrigem()
    {
        var box = new BoxFeature(Point3DOrigin(), 1, 1, 1) { Name = "manual" };

        FeatureOrigin.Of(box).Should().BeNull();
        FeatureOrigin.VariableName(box).Should().Be("manual");
    }

    private static AvaloniaCadEditor.Domain.ValueObjects.Point3D Point3DOrigin() => new(0, 0, 0);
}
