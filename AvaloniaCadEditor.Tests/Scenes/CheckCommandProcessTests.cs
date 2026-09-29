using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Roda o executável real (<c>AvaloniaCadEditor.UI.exe check</c>) como processo separado, do mesmo
/// jeito que uma IA externa ou um terminal faria, e confere saída e código de saída.
/// </summary>
[Trait("Category", "Integration")]
public class CheckCommandProcessTests
{
    [Fact]
    public void CenaValida_ComGeometria_Retorna0()
    {
        using var folder = new SceneTestFolder("CenaCheck");
        folder.CreateScene();

        var (exitCode, output) = RunCheck($"check \"{folder.Root}\" --geometry");

        exitCode.Should().Be(0, because: output);
        output.Should().Contain("AvaloniaCad check: CenaCheck (2 arquivo(s))");
        output.Should().Contain("OK: 1 feature(s), geometria validada.");
    }

    [Fact]
    public void CenaComErro_Retorna1EApontaArquivoELinha()
    {
        using var folder = new SceneTestFolder("CenaQuebrada");
        folder.CreateScene();
        string medidas = folder.FullPath("Pecas/Medidas.cs");
        File.WriteAllText(medidas, File.ReadAllText(medidas).Replace("= 10;", "= \"dez\";"));

        var (exitCode, output) = RunCheck($"check \"{folder.Root}\" --geometry");

        exitCode.Should().Be(1, because: output);
        output.Should().Contain("Pecas/Medidas.cs(5,");
        output.Should().Contain("CS0029");
        output.Should().Contain("FALHOU: 1 erro(s).");
    }

    [Fact]
    public void PastaSemCena_Retorna2()
    {
        using var folder = new SceneTestFolder();
        folder.Write("leia-me.txt", "não é uma cena");

        var (exitCode, output) = RunCheck($"check \"{folder.Root}\"");

        exitCode.Should().Be(2, because: output);
        output.Should().Contain(".csproj");
    }

    private static (int ExitCode, string Output) RunCheck(string arguments)
    {
        var psi = new ProcessStartInfo(FindUiExecutable(), arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(120_000).Should().BeTrue("o check não deveria levar mais de 2 minutos");

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>
    /// O executável do app na mesma configuração dos testes
    /// (ex.: AvaloniaCadEditor.UI/bin/Debug/net10.0-windows/AvaloniaCadEditor.UI.exe).
    /// </summary>
    private static string FindUiExecutable()
    {
        var testBin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string framework = testBin.Name;                 // net10.0-windows
        string configuration = testBin.Parent!.Name;     // Debug / Release

        var dir = testBin;
        while (dir != null && !dir.GetFiles("*.slnx").Any() && !dir.GetFiles("*.sln").Any())
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("os testes rodam dentro da pasta da solução");

        string exe = Path.Combine(dir!.FullName, "AvaloniaCadEditor.UI", "bin", configuration, framework, "AvaloniaCadEditor.UI.exe");
        File.Exists(exe).Should().BeTrue($"o app precisa estar compilado em {exe}");
        return exe;
    }
}
