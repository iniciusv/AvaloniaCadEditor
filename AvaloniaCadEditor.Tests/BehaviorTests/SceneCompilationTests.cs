using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Fase 0 do plano multi-arquivo: a cena é compilada como um programa C# comum
/// (CSharpCompilation + AssemblyLoadContext colecionável) em vez de C# Scripting.
/// </summary>
public class SceneCompilationTests
{
    private static ScriptDocument Doc(string path, string text) => new(path, text);

    private static string BoxLine(string name, double x = 0) =>
        $"var {name} = new BoxFeature(baseCorner: new Point3D({x}, 0, 0), width: 10, length: 10, height: 10) {{ Name = \"{name}\" }};";

    [Fact]
    public async Task CodigoSoComComentarios_RetornaCenaVaziaComSucesso()
    {
        var result = await new RoslynSceneEvaluator().EvaluateAsync("// Pressione 'Atualizar Código' para gerar o C#.");

        result.Success.Should().BeTrue(because: result.ErrorMessage);
        result.Features.Should().BeEmpty();
    }

    [Fact]
    public async Task ScriptDeArquivoUnico_ContinuaFuncionandoComAddFeatureGlobal()
    {
        string code = BoxLine("box_1") + "\nAddFeature(box_1);";

        var result = await new RoslynSceneEvaluator().EvaluateAsync(code);

        result.Success.Should().BeTrue(because: result.ErrorMessage);
        result.Features.Should().ContainSingle().Which.Name.Should().Be("box_1");
    }

    [Fact]
    public async Task UsingsImplicitos_IncluemLinq()
    {
        string code =
            "var caixas = Enumerable.Range(0, 3)\n" +
            "    .Select(i => new BoxFeature(baseCorner: new Point3D(i * 20, 0, 0), width: 10, length: 10, height: 10) { Name = $\"box_{i}\" })\n" +
            "    .ToList();\n" +
            "foreach (var c in caixas) AddFeature(c);";

        var result = await new RoslynSceneEvaluator().EvaluateAsync(code);

        result.Success.Should().BeTrue(because: result.ErrorMessage);
        result.Features.Should().HaveCount(3);
    }

    [Fact]
    public async Task VariosArquivos_CenaUsaTipoDeclaradoEmOutroArquivo()
    {
        var docs = new[]
        {
            Doc("Pecas/Medidas.cs",
                "public static class Medidas\n" +
                "{\n" +
                "    public const double Lado = 25;\n" +
                "    public static BoxFeature Cubo(string nome, double x) =>\n" +
                "        new BoxFeature(baseCorner: new Point3D(x, 0, 0), width: Lado, length: Lado, height: Lado) { Name = nome };\n" +
                "}\n"),
            Doc("Cena.cs",
                "AddFeature(Medidas.Cubo(\"cubo_1\", 0));\n" +
                "AddFeature(Medidas.Cubo(\"cubo_2\", 50));\n")
        };

        var result = await new RoslynSceneEvaluator().EvaluateAsync(docs);

        result.Success.Should().BeTrue(because: result.ErrorMessage);
        result.Features.Select(f => f.Name).Should().Equal("cubo_1", "cubo_2");
    }

    [Fact]
    public async Task ErroDeCompilacao_InformaArquivoELinha()
    {
        string code = BoxLine("box_1") + "\nAddFeature(caixa_inexistente);";

        var result = await new RoslynSceneEvaluator().EvaluateAsync(code);

        result.Success.Should().BeFalse();
        var error = result.Diagnostics.Should().ContainSingle(d => d.Severity == ScriptDiagnosticSeverity.Error).Subject;
        error.FilePath.Should().Be(ScriptDocument.DefaultSceneFile);
        error.Line.Should().Be(2);
        error.Id.Should().Be("CS0103");
        result.ErrorMessage.Should().Contain("Cena.cs(2,");
    }

    [Fact]
    public async Task ErroDeCompilacaoEmOutroArquivo_ApontaParaEsseArquivo()
    {
        var docs = new[]
        {
            Doc("Pecas/Quebrada.cs", "public static class Quebrada { public static int X => \"texto\"; }"),
            Doc("Cena.cs", BoxLine("box_1") + "\nAddFeature(box_1);")
        };

        var result = await new RoslynSceneEvaluator().EvaluateAsync(docs);

        result.Success.Should().BeFalse();
        result.Diagnostics
            .Where(d => d.Severity == ScriptDiagnosticSeverity.Error)
            .Should().OnlyContain(d => d.FilePath == "Pecas/Quebrada.cs" && d.Line == 1);
    }

    [Fact]
    public async Task DoisArquivosComTopLevelStatements_EhErroDeCompilacao()
    {
        var docs = new[]
        {
            Doc("Cena.cs", BoxLine("box_1") + "\nAddFeature(box_1);"),
            Doc("Outra.cs", BoxLine("box_2") + "\nAddFeature(box_2);")
        };

        var result = await new RoslynSceneEvaluator().EvaluateAsync(docs);

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Id == "CS8802");
    }

    [Fact]
    public async Task ErroEmExecucao_InformaArquivoELinha()
    {
        string code = BoxLine("box_1") + "\nthrow new InvalidOperationException(\"boom\");";

        var result = await new RoslynSceneEvaluator().EvaluateAsync(code);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("boom");
        result.ErrorMessage.Should().Contain("Cena.cs(2,");
    }

    [Fact]
    public async Task AvaliacoesConcorrentes_NaoMisturamFeatures()
    {
        var evaluator = new RoslynSceneEvaluator();

        var tasks = Enumerable.Range(1, 8).Select(n =>
        {
            string code = string.Join("\n", Enumerable.Range(0, n).Select(i => BoxLine($"b{i}", i * 20) + $" AddFeature(b{i});"));
            return evaluator.EvaluateAsync(code).ContinueWith(t => (Expected: n, Result: t.Result));
        });

        foreach (var (expected, result) in await Task.WhenAll(tasks))
        {
            result.Success.Should().BeTrue(because: result.ErrorMessage);
            result.Features.Should().HaveCount(expected);
        }
    }

    [Fact]
    public async Task AvaliacoesRepetidas_DescarregamOsAssembliesGerados()
    {
        const int runs = 30;
        await RunManyAsync(runs);

        for (int i = 0; i < 10 && CountSceneAssemblies() > 0; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(50);
        }

        // Outros testes podem estar avaliando em paralelo; o que importa é não acumular 1 assembly por execução.
        CountSceneAssemblies().Should().BeLessThan(runs / 3);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task RunManyAsync(int runs)
    {
        var evaluator = new RoslynSceneEvaluator();
        for (int i = 0; i < runs; i++)
        {
            var result = await evaluator.EvaluateAsync(BoxLine("box_1") + "\nAddFeature(box_1);");
            result.Success.Should().BeTrue(because: result.ErrorMessage);
        }
    }

    private static int CountSceneAssemblies() =>
        AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name?.StartsWith("CadScene_") == true);
}
