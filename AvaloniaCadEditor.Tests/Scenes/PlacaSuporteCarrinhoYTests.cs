using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// A PlacaSuporteCarrinhoY monta suas 9 fixações com porca como instâncias de FuroPorca (peça usada
/// como ferramenta dentro do Build). O volume é o da versão com furos e alojamentos declarados
/// soltos — se uma instância deixasse de cortar, ele subiria.
/// </summary>
public class PlacaSuporteCarrinhoYTests
{
    /// <summary>Volume medido antes da troca por FuroPorca (mesma tesselagem padrão do kernel).</summary>
    private const double VolumeComTodosOsCortes = 9894.71;

    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile!)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("este teste lê a cena AiCore do repo, então precisa achá-la");
        return Path.Combine(dir!.FullName, "AiCore");
    }

    private static async Task<TriangleMesh> MalhaLocal(string instancia)
    {
        var project = SceneProject.Load(AiCoreFolder());
        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());
        result.Success.Should().BeTrue(result.ErrorMessage);

        var placa = result.Features.OfType<PartInstance>().Single(i => i.Name == instancia);
        return new OpenCascadeGeometryKernel().BuildFeatureMesh(placa.Definition);
    }

    [Theory]
    [InlineData("placa_suporte_carrinho_y_a")]
    [InlineData("placa_suporte_carrinho_y_b")]
    public async Task TodasAsFixacoesComPorcaSaoCortadas(string instancia)
    {
        var malha = await MalhaLocal(instancia);

        MeshVolumeCalculator.ComputeVolume(malha).Should().BeApproximately(VolumeComTodosOsCortes, 0.05);
    }

    /// <summary>
    /// porca_inferior_3 é uma instância só (furo + alojamento): o hexágono fica centrado no furo em
    /// y = 12,5. Antes, o alojamento estava em y = 12 e o furo em 12,5. O vértice +Y do hexágono
    /// ampliado (raio 3,597) aparece na face inferior (z = -5,5).
    /// </summary>
    [Theory]
    [InlineData("placa_suporte_carrinho_y_a", 5.5)]
    [InlineData("placa_suporte_carrinho_y_b", -5.5)]
    public async Task AlojamentoInferiorFicaCentradoNoFuro(string instancia, double x)
    {
        var malha = await MalhaLocal(instancia);
        double raioVertice = (5.83 / 2 + 0.2) / (Math.Sqrt(3) / 2);

        var vertices = Enumerable.Range(0, malha.VertexCount)
            .Select(i => (X: (double)malha.Positions[i * 3], Y: (double)malha.Positions[i * 3 + 1], Z: (double)malha.Positions[i * 3 + 2]))
            .Where(v => Math.Abs(v.Z + 5.5) < 1e-3 && Math.Abs(v.X - x) < 1e-3)
            .Select(v => v.Y)
            .ToList();

        vertices.Should().Contain(y => Math.Abs(y - (12.5 + raioVertice)) < 1e-3);
        vertices.Should().Contain(y => Math.Abs(y - (12.5 - raioVertice)) < 1e-3);
    }
}
