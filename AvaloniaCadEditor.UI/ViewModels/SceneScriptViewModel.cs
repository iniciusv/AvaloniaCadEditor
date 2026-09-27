using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AvaloniaCadEditor.UI.Services;

namespace AvaloniaCadEditor.UI.ViewModels;

public class SceneScriptViewModel : INotifyPropertyChanged
{
    private readonly ISceneEvaluator? _sceneEvaluator;
    private readonly IFilePickerService? _filePickerService;
    private readonly CadViewModel _cadViewModel;
    private readonly CodeHistoryManager _history = new();

    public SceneScriptViewModel(
        CadViewModel cadViewModel,
        ISceneEvaluator? sceneEvaluator, 
        IFilePickerService? filePickerService)
    {
        _cadViewModel = cadViewModel;
        _sceneEvaluator = sceneEvaluator;
        _filePickerService = filePickerService;

        GenerateCodeCommand = new RelayCommand(() => 
        {
            // O gerador foi castrado. O código agora é a fonte da verdade.
        });
        RunCodeCommand = new RelayCommand(async () => await RunCodeAsync());
        
        LoadCodeFromFileCommand = new RelayCommand(async () =>
        {
            if (_filePickerService == null) return;
            var path = await _filePickerService.OpenFileAsync("Carregar Código C#", new[] { "*.cs" });
            if (!string.IsNullOrEmpty(path))
            {
                _history.SaveState(GeneratedCSharpCode);
                GeneratedCSharpCode = File.ReadAllText(path);
                RunCodeCommand.Execute(null);
                _cadViewModel.StatusText = $"✓ Modelo carregado de: {Path.GetFileName(path)}";
            }
        });

        SaveCodeToFileCommand = new RelayCommand(async () =>
        {
            if (_filePickerService == null) return;
            var path = await _filePickerService.SaveFileAsync("Salvar Modelo como C#", ".cs", "Model.cs");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, GeneratedCSharpCode);
                _cadViewModel.StatusText = $"✓ Modelo salvo em: {Path.GetFileName(path)}";
            }
        });
    }

    public async System.Threading.Tasks.Task RunCodeAsync()
    {
        if (_sceneEvaluator == null)
        {
            _cadViewModel.StatusText = "Aviso: Evaluator não configurado.";
            return;
        }
        _cadViewModel.StatusText = "Avaliando código...";
        var result = await _sceneEvaluator.EvaluateAsync(GeneratedCSharpCode);
        if (result.Success)
        {
            _cadViewModel.MeshManager.ClearCache();
            _cadViewModel.Sketch.ClearFeatures();
            foreach (var feature in result.Features)
            {
                _cadViewModel.Sketch.AddFeature(feature);
            }
            _cadViewModel.ModelTree.RebuildTree();
            _cadViewModel.RefreshProfiles();
            _cadViewModel.RefreshSolidMesh();
            _cadViewModel.StatusText = $"✓ Código executado: {result.Features.Count} feature(s)";
        }
        else
        {
            _cadViewModel.StatusText = $"✗ Erro de Compilação: {result.ErrorMessage.Replace("\r", "").Replace("\n", " ")}";
            Console.WriteLine("Evaluation Error:\n" + result.ErrorMessage);
        }
    }



    public ICommand GenerateCodeCommand { get; }
    public ICommand RunCodeCommand { get; }
    public ICommand LoadCodeFromFileCommand { get; }
    public ICommand SaveCodeToFileCommand { get; }

    private string _generatedCSharpCode = "// Pressione 'Atualizar Código' para gerar o C#.";
    public string GeneratedCSharpCode
    {
        get => _generatedCSharpCode;
        set 
        { 
            _generatedCSharpCode = value; 
            AvaloniaCadEditor.Application.Services.RoslynDependencyAnalyzer.CurrentCode = value;
            OnPropertyChanged(); 
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void AppendCode(string codeSnippet)
    {
        _history.SaveState(GeneratedCSharpCode);
        if (GeneratedCSharpCode.StartsWith("// Pressione"))
        {
            GeneratedCSharpCode = string.Empty;
        }

        GeneratedCSharpCode += codeSnippet;
    }

    public void RemoveCodeForFeature(string variableName)
    {
        _history.SaveState(GeneratedCSharpCode);
        if (string.IsNullOrWhiteSpace(variableName)) return;
        
        GeneratedCSharpCode = RoslynFeatureRemover.RemoveFeatureCode(GeneratedCSharpCode, variableName);
    }

    public void MutateCoordinates(System.Collections.Generic.IEnumerable<string> rootVariables, AvaloniaCadEditor.Domain.ValueObjects.Vector3D translation)
    {
        _history.SaveState(GeneratedCSharpCode);
        GeneratedCSharpCode = RoslynCoordinateMutator.MutateCoordinates(GeneratedCSharpCode, rootVariables, translation);
    }

    /// <summary>
    /// Aplica um código gerado por fonte externa (ex.: o chat de IA) como o script vigente,
    /// preservando o histórico de Undo/Redo, e o executa imediatamente.
    /// </summary>
    public async Task ApplyExternalCode(string code)
    {
        _history.SaveState(GeneratedCSharpCode);
        GeneratedCSharpCode = code;
        await RunCodeAsync();
    }

    public void Undo()
    {
        if (_history.TryUndo(GeneratedCSharpCode, out var previousCode))
        {
            GeneratedCSharpCode = previousCode;
            RunCodeCommand.Execute(null);
        }
    }

    public void Redo()
    {
        if (_history.TryRedo(GeneratedCSharpCode, out var nextCode))
        {
            GeneratedCSharpCode = nextCode;
            RunCodeCommand.Execute(null);
        }
    }
}
