using Avalonia;
using System;

namespace AvaloniaCadEditor;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // `AvaloniaCadEditor.UI.exe check <pasta> [--geometry]`: verifica uma cena sem abrir a janela.
        if (AvaloniaCadEditor.UI.Cli.CheckCommand.IsCheckInvocation(args))
        {
            Environment.ExitCode = AvaloniaCadEditor.UI.Cli.CheckCommand.Run(args);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
