using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>Fase 1: a cena é uma pasta com .csproj, lida por convenção e compilável sozinha.</summary>
public class SceneProjectTests
{
    [Fact]
    public void Create_GeraProjetoCenaExemploEProtocoloParaIA()
    {
        using var folder = new SceneTestFolder("MinhaCena");

        var project = folder.CreateScene();

        project.Name.Should().Be("MinhaCena");
        File.Exists(folder.FullPath("MinhaCena.csproj")).Should().BeTrue();
        File.Exists(folder.FullPath("Cena.cs")).Should().BeTrue();
        File.Exists(folder.FullPath("Pecas/Medidas.cs")).Should().BeTrue();
        File.ReadAllText(folder.FullPath("CLAUDE.md")).Should().Contain("@AGENTS.md");
        File.ReadAllText(folder.FullPath("AGENTS.md")).Should().Contain("check . --geometry").And.Contain("dotnet build");
        File.ReadAllText(folder.FullPath(".cad/api.md")).Should().Contain("BoxFeature");

        string csproj = File.ReadAllText(folder.FullPath("MinhaCena.csproj"));
        csproj.Should().Contain("AvaloniaCadEditor.Domain.dll");
        csproj.Should().Contain("Include=\"AvaloniaCadEditor.Domain.Scripting.Scene\" Static=\"true\"");
    }

    [Fact]
    public void Create_EmPastaNaoVazia_Falha()
    {
        using var folder = new SceneTestFolder();
        folder.Write("qualquer.txt", "x");

        Action act = () => folder.CreateScene();

        act.Should().Throw<InvalidOperationException>().WithMessage("*pasta vazia*");
    }

    [Fact]
    public void GetSourceFiles_IgnoraBinObjEPastaGerada()
    {
        using var folder = new SceneTestFolder();
        var project = folder.CreateScene();
        folder.Write("bin/Debug/Gerado.cs", "lixo");
        folder.Write("obj/Outro.cs", "lixo");
        folder.Write(".cad/Interno.cs", "lixo");

        project.GetSourceFiles().Should().Equal("Cena.cs", "Pecas/Medidas.cs");
        project.IsIgnoredPath(folder.FullPath("obj/Outro.cs")).Should().BeTrue();
        project.IsIgnoredPath(folder.FullPath("Pecas/Medidas.cs")).Should().BeFalse();
    }

    [Fact]
    public void Load_SemCsproj_FalhaComMensagemClara()
    {
        using var folder = new SceneTestFolder();
        folder.Write("Cena.cs", "AddFeature(null);");

        Action act = () => SceneProject.Load(folder.Root);

        act.Should().Throw<InvalidOperationException>().WithMessage("*.csproj*");
    }

    [Fact]
    public void FindEntryFile_EncontraOArquivoComTopLevelStatements()
    {
        var docs = new[]
        {
            new ScriptDocument("Pecas/A.cs", "public static class A { }"),
            new ScriptDocument("Montagem.cs", "var x = 1;"),
        };

        SceneProject.FindEntryFile(docs).Should().Be("Montagem.cs");
        SceneProject.FindEntryFile(docs.Take(1)).Should().Be(SceneProject.DefaultEntryFile);
    }

    [Fact]
    public async Task CenaGerada_ExecutaNoAvaliadorUsandoTipoDeOutroArquivo()
    {
        using var folder = new SceneTestFolder();
        var project = folder.CreateScene();

        var result = await new RoslynSceneEvaluator().EvaluateAsync(project.ReadDocuments());

        result.Success.Should().BeTrue(because: result.ErrorMessage);
        var box = result.Features.Should().ContainSingle().Which.Should().BeOfType<BoxFeature>().Subject;
        box.Height.Should().Be(10, "a altura vem de Medidas.Espessura, declarada em Pecas/Medidas.cs");
    }

    /// <summary>
    /// A promessa da cena autônoma: `dotnet build` na pasta gerada compila sem o app.
    /// Usa o SDK .NET instalado na máquina (o mesmo que compila esta solução).
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void CenaGerada_CompilaComDotnetBuild()
    {
        using var folder = new SceneTestFolder("CenaBuild");
        folder.CreateScene();

        var psi = new ProcessStartInfo("dotnet", "build --nologo -v q")
        {
            WorkingDirectory = folder.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // O runner de testes exporta variáveis do MSBuild que atrapalhariam um build aninhado.
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            psi.Environment.Remove(key);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(180_000);

        exited.Should().BeTrue("o build não deveria levar mais de 3 minutos");
        process.ExitCode.Should().Be(0, because: stdout.Result + stderr.Result);
    }
}
