using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Infrastructure.Engines;
using AvaloniaCadEditor.UI.ViewModels;
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
			var geometryEngine = new MathGeometryEngine();
			var osnapService = new OSnapService(geometryEngine);
			var profileRecognitionService = new ProfileRecognitionService();
			var geometryKernel = new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel();
			var settingsProvider = new AvaloniaCadEditor.Infrastructure.Settings.JsonTessellationSettingsProvider();
			var viewModel = new CadViewModel(geometryEngine, osnapService, profileRecognitionService, geometryKernel, null, settingsProvider);

			desktop.MainWindow = new MainWindow
			{
				DataContext = viewModel
			};
		}

		base.OnFrameworkInitializationCompleted();
	}
}