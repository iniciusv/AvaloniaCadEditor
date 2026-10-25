using System.Collections.Generic;
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
/// Invariante central do referencial para qualquer rotação: uma peça girada fora de Z (como
/// <c>carrinho_hgh15_z_b</c>, deitada em X e depois girada em Z) tem que dar, em cada vista, as
/// mesmas cotas — mesma chave e mesmo valor — que a mesma peça (mesmo <c>Part</c>, mesma
/// <c>Definition</c>) parada na identidade. O referencial é só uma lente; o desenho não pode mudar
/// com a orientação da peça no mundo.
/// </summary>
public class RotatedInstanceDrawingTests
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

    private static Dictionary<string, double> DimensionsByKey(IFeature feature)
    {
        var kernel = new OpenCascadeGeometryKernel();
        var positions = kernel.BuildFeatureMesh(feature).Positions;
        var frame = PartFrameResolver.Resolve(feature, positions);
        frame.Should().NotBeNull();

        var dimensions = new Dictionary<string, double>();
        foreach (var view in new[] { StandardView.Front, StandardView.Back, StandardView.Left, StandardView.Right, StandardView.Top, StandardView.Bottom })
        {
            var input = DraftingInput.ForIsolated(feature, frame!, positions, view);
            var sheet = input == null ? null : DraftingAnalyzer.Analyze(input);
            foreach (var dimension in sheet?.Dimensions ?? System.Array.Empty<DraftDimension>())
            {
                dimensions[$"{view}:{dimension.Key}"] = dimension.Value;
            }
        }

        return dimensions;
    }

    [Fact]
    public async Task CarrinhoGiradoForaDeZ_DaAsMesmasCotasQueNaIdentidade()
    {
        var project = SceneProject.Load(AiCoreFolder());
        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());
        result.Success.Should().BeTrue(result.ErrorMessage);

        // Como a cena posiciona carrinho_hgh15_z_b: deitado em X e depois girado em Z.
        var daCena = result.Features.OfType<PartInstance>().First(f => f.PartTypeName == "CarrinhoHgh15");
        daCena.Placement.IsRotationAboutZOnly.Should().BeFalse("o caso interessante é fora de Z");

        var girado = new PartInstance<Part>(daCena.Part, daCena.Definition,
            AvaloniaCadEditor.Domain.ValueObjects.Placement.Rotation(AvaloniaCadEditor.Domain.ValueObjects.Vector3D.UnitX, 90)
                .Then(AvaloniaCadEditor.Domain.ValueObjects.Placement.At(50, 30, 10, rotationZDegrees: -90)))
        { Name = "carrinho_girado" };
        var identidade = new PartInstance<Part>(daCena.Part, daCena.Definition,
            AvaloniaCadEditor.Domain.ValueObjects.Placement.Identity)
        { Name = "carrinho_identidade" };

        var cotasGirado = DimensionsByKey(girado);
        var cotasIdentidade = DimensionsByKey(identidade);

        cotasGirado.Keys.Should().BeEquivalentTo(cotasIdentidade.Keys);
        foreach (var (key, value) in cotasIdentidade)
        {
            cotasGirado[key].Should().BeApproximately(value, 1e-6, $"a cota '{key}' não pode depender da orientação da peça no mundo");
        }
    }
}
