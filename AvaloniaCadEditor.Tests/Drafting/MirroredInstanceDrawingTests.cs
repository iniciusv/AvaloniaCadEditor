using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Isolation;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Drafting;

/// <summary>
/// O bloco_ficacao_belt_vao_correia da cena é posicionado com <c>Placement.MirrorZ()</c>. Um espelho
/// não é "rotação só em Z", e o isolamento ficava sem referencial — sem referencial não há cotas.
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

        var bloco = result.Features.OfType<PartInstance>().First(f => f.PartTypeName == "BlocoFicacaoBeltVaoCorreia");
        bloco.Placement.IsMirrored.Should().BeTrue();
        var positions = new OpenCascadeGeometryKernel().BuildFeatureMesh(bloco).Positions;

        var frame = PartFrameResolver.Resolve(bloco, positions);
        frame.Should().NotBeNull();

        var sheet = DraftingAnalyzer.Analyze(DraftingInput.ForIsolated(bloco, frame!, positions, vista)!)!;
        sheet.Dimensions.Should().NotBeEmpty();
    }
}
