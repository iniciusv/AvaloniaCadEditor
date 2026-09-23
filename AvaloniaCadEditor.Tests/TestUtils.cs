using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Infrastructure.Settings;
using AvaloniaCadEditor.UI.ViewModels;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Tests;

public static class TestUtils
{
    public static CadViewModel CreateCadViewModel(
        IGeometryEngine geometryEngine,
        IOSnapService osnapService,
        IProfileRecognitionService? profileRecognitionService = null,
        IGeometryKernel? geometryKernel = null,
        ISelectionService? selectionService = null)
    {
        var kernel = geometryKernel ?? new AvaloniaCadEditor.Infrastructure.OpenCascade.PureMathGeometryKernel();
        var selService = selectionService ?? new SelectionService();
        var settingsProvider = new JsonTessellationSettingsProvider();
        
        var meshManager = new SceneMeshManager(kernel, settingsProvider);
        var vcbService = new VcbInputService();
        var deletionService = new SceneDeletionService(selService);
        
        var vm = new CadViewModel(
            geometryEngine,
            osnapService,
            meshManager,
            vcbService,
            deletionService,
            null!, // ScriptEditor vai ser injetado abaixo
            profileRecognitionService,
            selService,
            settingsProvider);
            
        vm.ScriptEditor = new SceneScriptViewModel(vm, new AvaloniaCadEditor.Infrastructure.Scripting.RoslynSceneEvaluator(), null);
        return vm;
    }
}
