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
			var evaluator = new AvaloniaCadEditor.Infrastructure.Scripting.RoslynSceneEvaluator();
			var selectionService = new SelectionService();
			
			var meshManager = new SceneMeshManager(geometryKernel, settingsProvider);
			var vcbService = new VcbInputService();
			var deletionService = new SceneDeletionService(selectionService);
			
			desktop.MainWindow = new MainWindow();
			var filePicker = new AvaloniaCadEditor.UI.Services.AvaloniaFilePickerService(desktop.MainWindow);
			
			var algorithms = new System.Collections.Generic.List<AvaloniaCadEditor.Application.Interfaces.IReconstructionAlgorithm>
			{
			    new AvaloniaCadEditor.Infrastructure.Engines.BaseFaceExtrusionAlgorithm(),
			    new AvaloniaCadEditor.Infrastructure.Engines.BaseProfileCutsAlgorithm(),
			    new AvaloniaCadEditor.Infrastructure.Engines.RansacReconstructionAlgorithm(),
			    new AvaloniaCadEditor.Infrastructure.Engines.BoundingBoxHeuristicAlgorithm()
			};
			var reconstructionService = new AvaloniaCadEditor.Application.Services.ShapeReconstructionService(algorithms, geometryKernel);
			
			var viewModel = new CadViewModel(geometryEngine, osnapService, meshManager, vcbService, deletionService, null!, profileRecognitionService, selectionService, settingsProvider, null, filePicker, reconstructionService);
			
			var scriptEditor = new SceneScriptViewModel(viewModel, evaluator, filePicker);
			viewModel.ScriptEditor = scriptEditor;

			var aiSettingsProvider = new AvaloniaCadEditor.Infrastructure.Settings.JsonAiSettingsProvider();
			viewModel.AiChat = new AvaloniaCadEditor.UI.ViewModels.AiChatViewModel(viewModel, scriptEditor, evaluator, aiSettingsProvider);

			var referenceGuideService = new AvaloniaCadEditor.Application.ReferenceGuides.ReferenceGuideService();
			viewModel.ReferenceGuides = new AvaloniaCadEditor.UI.ReferenceGuides.ReferenceGuidesViewModel(referenceGuideService, () => viewModel.Sketch.Features);

			desktop.MainWindow.DataContext = viewModel;
		}

		base.OnFrameworkInitializationCompleted();
	}
}