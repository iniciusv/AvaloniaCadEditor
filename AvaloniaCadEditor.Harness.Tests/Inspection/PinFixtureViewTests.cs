using System.Runtime.CompilerServices;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Harness.Inspection;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit.Abstractions;

namespace AvaloniaCadEditor.Harness.Tests.Inspection;

/// <summary>
/// A vista da placa_b do caso pino-no-furo do PromptEval (espelhada, girada 180°, deslocada): o
/// furo_2 tem de sair com o eixo certo no mundo e no código da peça — é o que o modelo usa para
/// acertar o caso. O furo_2 fica no código em (−12,5; 9) e no mundo em (147,5; 11), z 42,5..47,5.
/// </summary>
public class PinFixtureViewTests(ITestOutputHelper output)
{
    private static string FixtureFiles([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile!)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AvaloniaCadEditor.PromptEval"))) dir = dir.Parent;
        dir.Should().NotBeNull("este teste lê a fixture do PromptEval");
        return Path.Combine(dir!.FullName, "AvaloniaCadEditor.PromptEval", "Cases", "fixtures", "pino-no-furo", "files");
    }

    [Fact]
    public async Task Furo2DaPlacaB_SaiComOEixoNoMundoENoCodigo()
    {
        string root = FixtureFiles();
        var documents = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => new ScriptDocument(Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToList();
        var scene = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        scene.Success.Should().BeTrue(scene.ErrorMessage);

        var service = new SceneInspectionService(new OpenCascadeGeometryKernel());
        var target = service.Prepare(TargetResolver.Resolve(scene.Features, "placa_b").Feature!);
        string text = service.DescribeView(target, StandardView.Top);
        output.WriteLine(text);

        text.Should().Contain("[furo_2] · mundo: entrada (147.5, 11, 47.5) → fundo (147.5, 11, 42.5) · código: entrada (-12.5, 9, 5) → fundo (-12.5, 9, 0)");
        text.Should().Contain("[furo_1] · mundo: entrada (122.5, 27.5, 47.5) → fundo (122.5, 27.5, 42.5) · código: entrada (12.5, -7.5, 5) → fundo (12.5, -7.5, 0)");
    }
}
