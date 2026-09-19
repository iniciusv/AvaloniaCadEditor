using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaCadEditor.ViewModels;
using AvaloniaCadEditor.Views;

namespace AvaloniaCadEditor;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var geometryEngine = new AvaloniaCadEditor.Infrastructure.Engines.DummyGeometryEngine();
            var osnapService = new AvaloniaCadEditor.Application.Services.OSnapService(geometryEngine);
            var viewModel = new AvaloniaCadEditor.UI.ViewModels.CadViewModel(geometryEngine, osnapService);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}