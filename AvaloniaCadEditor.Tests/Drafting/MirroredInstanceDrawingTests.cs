using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Isolation;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Drafting;

/// <summary>
/// Instância posicionada com <c>Placement.MirrorZ()</c>: um espelho não é "rotação só em Z", e o
/// isolamento ficava sem referencial — sem referencial não há cotas. O caso veio do
/// bloco_ficacao_belt_vao_correia da cena; ela não o espelha mais, então o teste recria aquele
/// placement sobre a mesma peça.
/// </summary>
public class MirroredInstanceDrawingTests
{
    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile!)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, "AiCore");
    }

    [Theory]
    [InlineData(StandardView.Top)]
    [InlineData(StandardView.Front)]
    public async Task BlocoEspelhadoEmZ_TemReferencialECotas(StandardView vista)
    {
        var project = SceneProject.Load(AiCoreFolder());
        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());
        result.Success.Should().BeTrue(result.ErrorMessage);

        // Girado em Z e depois espelhado em Z, como a cena posicionava o bloco.
        var daCena = result.Features.OfType<PartInstance>().First(f => f.PartTypeName == "BlocoFicacaoBeltVaoCorreia");
        var espelho = Placement.At(5, 0, 26, rotationZDegrees: 90).Then(Placement.MirrorZ()).Then(Placement.Translate(new Vector3D(0, 0, 41)));
        var bloco = new PartInstance<Part>(daCena.Part, daCena.Definition, espelho) { Name = "bloco_espelhado" };
        bloco.Placement.IsMirrored.Should().BeTrue();
        var positions = new OpenCascadeGeometryKernel().BuildFeatureMesh(bloco).Positions;

        var frame = PartFrameResolver.Resolve(bloco, positions);
        frame.Should().NotBeNull();

        var sheet = DraftingAnalyzer.Analyze(DraftingInput.ForIsolated(bloco, frame!, positions, vista)!)!;
        sheet.Dimensions.Should().NotBeEmpty();
    }
}
