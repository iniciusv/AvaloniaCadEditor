using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;

namespace AvaloniaCadEditor.UI.Cli;

/// <summary>
/// <c>AvaloniaCadEditor.UI.exe check [pasta] [--geometry]</c>: verifica uma cena sem abrir a janela.
/// Códigos de saída: 0 = cena válida, 1 = erros na cena, 2 = uso incorreto ou pasta inválida.
/// </summary>
public static class CheckCommand
{
    public const string Name = "check";

    public static bool IsCheckInvocation(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        AttachToParentConsole();

        var options = args.Skip(1).ToList();
        bool geometry = options.RemoveAll(a => a is "--geometry" or "-g") > 0;
        if (options.Any(a => a.StartsWith('-')) || options.Count > 1)
        {
            Console.Error.WriteLine("Uso: AvaloniaCadEditor.UI.exe check [pasta-da-cena] [--geometry]");
            return 2;
        }

        string folder = options.Count == 1 ? options[0] : Directory.GetCurrentDirectory();

        SceneProject project;
        try
        {
            project = SceneProject.Load(folder);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var service = new SceneCheckService(
            new RoslynSceneEvaluator(),
            geometry ? new OpenCascadeGeometryKernel() : null);

        var report = service.CheckAsync(project, geometry).GetAwaiter().GetResult();
        Console.Out.Write(report.Format());
        Console.Out.Flush();
        return report.Success ? 0 : 1;
    }

    /// <summary>
    /// O app é WinExe (sem console próprio). Quando chamado de um terminal sem redirecionamento,
    /// anexa ao console de quem chamou para a saída aparecer; com redirecionamento (IAs, scripts),
    /// a saída já vai para o pipe.
    /// </summary>
    private static void AttachToParentConsole()
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
        {
            return;
        }

        const int AttachParentProcess = -1;
        AttachConsole(AttachParentProcess);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);
}
