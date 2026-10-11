using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Mover uma instância pelo viewport (clique-clique no plano XY) reescreve o <c>at:</c> do Add e
/// reexecuta a cena. O caso é o da cena AiCore: a posição sai de outra instância (<c>ToWorld</c>) e
/// tem rotação. Quando a reescrita falha, o motivo tem que ficar no status — antes a coordenada do
/// cursor o apagava logo em seguida e a peça só "voltava" para o lugar, sem explicação.
/// </summary>
public class MoverInstanciaPeloViewportTests
{
    private const string Peca =
        "public sealed record Bloco(double Lado = 10) : Part\n" +
        "{\n" +
        "    protected override IFeature Build() => new BoxFeature(new Point3D(0, 0, 0), Lado, Lado, Lado) { Name = \"corpo\" };\n" +
        "}\n";

    private const string Cena =
        "var peca = new Bloco();\n" +
        "var base1 = Add(peca, at: Placement.At(0, 0, 0, rotationZDegrees: 180), name: \"base\");\n" +
        "var canto = base1.ToWorld(new Point3D(5, 5, 10));\n" +
        "var apoiado = Add(peca, at: Placement.At(canto.X + 2, canto.Y, canto.Z, rotationZDegrees: 180), name: \"apoiado\");";

    private static async Task<CadViewModel> AbrirCena(SceneTestFolder folder)
    {
        folder.CreateScene();
        folder.Write("Cena.cs", Cena);
        folder.Write("Pecas/Bloco.cs", Peca);

        // Snap desligado: o ponto clicado é o ponto usado.
        var osnap = new Mock<IOSnapService>();
        osnap.Setup(o => o.GetSnappedPoint(It.IsAny<Sketch>(), It.IsAny<Point3D>(), It.IsAny<Point3D?>(), It.IsAny<TriangleMesh?>(), It.IsAny<bool>()))
             .Returns((Sketch _, Point3D cursor, Point3D? _, TriangleMesh? _, bool _) => cursor);

        var vm = TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            osnap.Object,
            null,
            new PureMathGeometryKernel());
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        return vm;
    }

    private static void MoverPeloViewport(CadViewModel vm, string nome, double dx)
    {
        var instance = vm.Sketch.Features.Single(f => f.Name == nome);
        vm.SelectionService.SelectById(vm.Sketch, instance.Id);
        vm.CurrentTool = new MoveTool();
        vm.OnPointerPressed(new Point3D(0, 0, 0));
        vm.OnPointerPressed(new Point3D(dx, 0, 0));
    }

    /// <summary>O handler do app é async void: espera a cena ser refeita (features novas).</summary>
    private static async Task<PartInstance> EsperarReexecucao(CadViewModel vm, IFeature antes)
    {
        for (int i = 0; i < 200; i++)
        {
            var atual = vm.Sketch.Features.OfType<PartInstance>().Single(f => f.Name == antes.Name);
            if (!ReferenceEquals(atual, antes)) return atual;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException($"a cena não foi refeita (status: {vm.StatusText})");
    }

    [Fact]
    public async Task InstanciaPosicionadaPorOutra_MoveEFicaNoLugarNovo()
    {
        using var folder = new SceneTestFolder();
        var vm = await AbrirCena(folder);
        var antes = vm.Sketch.Features.OfType<PartInstance>().Single(f => f.Name == "apoiado");
        double x0 = antes.Placement.Translation.X;

        MoverPeloViewport(vm, "apoiado", 10);
        var depois = await EsperarReexecucao(vm, antes);

        depois.Placement.Translation.X.Should().BeApproximately(x0 + 10, 1e-6);
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("Placement.At(canto.X + 12, canto.Y, canto.Z, rotationZDegrees: 180)");
    }

    [Fact]
    public async Task ReescritaQueFalha_DeixaOMotivoNoStatus_EmVezDaCoordenadaDoCursor()
    {
        using var folder = new SceneTestFolder();
        var vm = await AbrirCena(folder);

        // O código mudou depois da última execução: a linha de onde "apoiado" veio (4) já não tem Add.
        vm.ScriptEditor.GeneratedCSharpCode = string.Concat(Enumerable.Repeat("// comentário\n", 10)) + Cena;

        MoverPeloViewport(vm, "apoiado", 10);

        vm.StatusText.Should().StartWith("✗").And.Contain("linha 4");
    }
}
