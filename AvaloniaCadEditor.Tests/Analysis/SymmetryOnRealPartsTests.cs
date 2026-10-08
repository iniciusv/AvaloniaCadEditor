using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Application.ReferenceGuides;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ReferenceGuides;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AvaloniaCadEditor.Tests.Analysis;

/// <summary>
/// Simetrias e eixos nas peças reais onde a análise errava: o trilho MGN9 (12 furos com rebaixo, passo
/// 20) ganhava um "plano de simetria" em cada furo e em cada meio de vão, e uma linha de fileira
/// atravessada em cada furo; a placa do carrinho Y ganhava planos entre pares soltos de FuroPorca.
/// O suporte do NEMA 17, que já estava certo, não pode mudar.
/// </summary>
public class SymmetryOnRealPartsTests(ITestOutputHelper output)
{
    private static readonly Lazy<Task<IReadOnlyList<IFeature>>> Scene = new(LoadScene);

    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile!)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore"))) dir = dir.Parent;
        dir.Should().NotBeNull("este teste lê a cena AiCore do repo");
        return Path.Combine(dir!.FullName, "AiCore");
    }

    private static async Task<IReadOnlyList<IFeature>> LoadScene()
    {
        var project = SceneProject.Load(AiCoreFolder());
        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());
        result.Success.Should().BeTrue(result.ErrorMessage);
        return result.Features;
    }

    private static async Task<PartInstance> Instance(string name) =>
        (await Scene.Value).OfType<PartInstance>().Single(i => i.Name == name);

    /// <summary>A peça no próprio sistema do código (origem e eixos do Build).</summary>
    private static ModelRelations Local(PartInstance instance) =>
        RelationAnalyzer.Analyze(FactExtractor.Extract([new AnalysisSubject(instance.Definition, Placement.Identity, instance.Name)]));

    private void Print(ModelRelations r)
    {
        foreach (var p in r.SymmetryPlanes)
        {
            output.WriteLine($"{p.Normal} = {p.Coordinate:0.###} · global={p.IsGlobal} · unidades={p.UnitPairs} · " +
                             string.Join(" | ", p.Pairs.Select(x => $"{x.A.Name}~{x.B.Name}")));
        }
    }

    [Fact]
    public async Task Trilho_SoOMeioDaFileiraESimetria()
    {
        var r = Local(await Instance("trilho_mgn9_x"));
        Print(r);

        r.SymmetryPlanes.Where(p => p.UnitPairs >= 2 || p.IsGlobal).Should().HaveCount(2, "o meio do comprimento e o meio da largura");
        r.SymmetryPlanes.Should().OnlyContain(p => p.IsGlobal);
    }

    [Fact]
    public async Task Trilho_GuiasSemLinhaAtravessadaPorFuroESimetriaDeitada()
    {
        var trilho = await Instance("trilho_mgn9_x");
        var set = new ReferenceGuideService().Analyze([trilho]);
        foreach (var line in set.Lines.Where(l => l.Kind is GuideKind.HoleRow or GuideKind.Symmetry))
        {
            output.WriteLine($"{line.Kind} {line.Axis}={line.Coordinate:0.###} {line.Start} -> {line.End} · {string.Join(", ", line.MemberNames)}");
        }

        // Cada posição é furo passante + rebaixo (coaxiais): uma unidade, não uma "fileira" de dois —
        // então nenhuma linha atravessada (em X) por furo, e uma só ao longo deles, com os 12.
        var rows = set.Lines.Where(l => l.Kind == GuideKind.HoleRow).ToList();
        rows.Should().NotContain(l => l.Axis == "X");
        var row = rows.Should().ContainSingle(l => l.Axis == "Y").Subject;
        row.MemberNames.Should().HaveCount(12).And.Contain("trilho_mgn9_x/furo_passante_3+rebaixo_3");

        var symmetry = set.Lines.Where(l => l.Kind == GuideKind.Symmetry).ToList();
        symmetry.Should().HaveCount(2);
        symmetry.Should().OnlyContain(l => Math.Abs(l.Start.Z - l.End.Z) < 1e-9, "as duas ficam no plano de cima, deitadas");
        symmetry.Should().Contain(l => (l.End - l.Start).Length > 200, "a do meio da largura atravessa o trilho no comprimento");
    }

    [Fact]
    public async Task PlacaB_SemPlanosEntreParesSoltosDeFuroPorca()
    {
        var r = Local(await Instance("placa_suporte_carrinho_y_b"));
        Print(r);

        // Os planos falsos do relatório (folha = código + (16, 24)): entre x_5/x_6 (y = −8), x_5/x_7
        // (0,5), porca_inferior_1/3 (2,25), x_6/x_7 (10,5), x_2/x_1 (11,5) e x_7/x_1 (x = 1).
        var locals = r.SymmetryPlanes.Where(p => !p.IsGlobal && p.UnitPairs >= 2).ToList();
        foreach (var y in new[] { -8, 0.5, 2.25, 10.5, 11.5 })
        {
            locals.Should().NotContain(p => p.Normal == FrameAxis.Y && Math.Abs(p.Coordinate - y) < 1e-3, $"y = {y} é um par solto");
        }
        locals.Should().NotContain(p => p.Normal == FrameAxis.X && Math.Abs(p.Coordinate - 1) < 1e-3, "x_7 e x_1 são um par só");
    }

    [Fact]
    public async Task SuporteNema17_SimetriasContinuam()
    {
        var r = Local(await Instance("suporte_motor_nema17"));
        Print(r);

        r.GlobalSymmetry(FrameAxis.X).Should().NotBeNull();
        r.SymmetryPlanes.Should().ContainSingle(p => p.Normal == FrameAxis.Y && Math.Abs(p.Coordinate) < 1e-6 && p.UnitPairs >= 2,
            "os 4 furos do motor e os 2 da extrusão se espelham em y = 0");
    }
}
