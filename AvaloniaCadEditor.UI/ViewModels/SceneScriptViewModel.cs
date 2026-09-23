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

namespace AvaloniaCadEditor.UI.ViewModels;

public class SceneScriptViewModel : INotifyPropertyChanged
{
    private readonly ISceneEvaluator? _sceneEvaluator;
    private readonly IFilePickerService? _filePickerService;
    private readonly CadViewModel _cadViewModel;

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
            var generator = new CsCodeGenerator();
            GeneratedCSharpCode = generator.GenerateCode(_cadViewModel.Sketch, _cadViewModel.ActiveWorkPlane);
        });

        RunCodeCommand = new RelayCommand(async () =>
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
                _cadViewModel.Sketch.Clear();
                foreach (var feature in result.Features)
                {
                    _cadViewModel.Sketch.AddFeature(feature);
                }
                _cadViewModel.RefreshProfiles();
                _cadViewModel.StatusText = $"✓ Código executado: {result.Features.Count} feature(s)";
            }
            else
            {
                _cadViewModel.StatusText = $"✗ Erro de Compilação";
                Console.WriteLine("Evaluation Error:\n" + result.ErrorMessage);
            }
        });

        LoadCodeFromFileCommand = new RelayCommand(async () =>
        {
            if (_filePickerService == null) return;
            var path = await _filePickerService.OpenFileAsync("Carregar Código C#", new[] { "*.cs" });
            if (!string.IsNullOrEmpty(path))
            {
                GeneratedCSharpCode = File.ReadAllText(path);
                RunCodeCommand.Execute(null);
                _cadViewModel.StatusText = $"✓ Modelo carregado de: {Path.GetFileName(path)}";
            }
        });

        SaveCodeToFileCommand = new RelayCommand(async () =>
        {
            if (_filePickerService == null) return;
            if (GenerateCodeCommand != null && GenerateCodeCommand.CanExecute(null))
            {
                GenerateCodeCommand.Execute(null);
            }
            var path = await _filePickerService.SaveFileAsync("Salvar Modelo como C#", ".cs", "Model.cs");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, GeneratedCSharpCode);
                _cadViewModel.StatusText = $"✓ Modelo salvo em: {Path.GetFileName(path)}";
            }
        });
    }

    public ICommand GenerateCodeCommand { get; }
    public ICommand RunCodeCommand { get; }
    public ICommand LoadCodeFromFileCommand { get; }
    public ICommand SaveCodeToFileCommand { get; }

    private string _generatedCSharpCode = "// Pressione 'Atualizar Código' para gerar o C#.";
    public string GeneratedCSharpCode
    {
        get => _generatedCSharpCode;
        set { _generatedCSharpCode = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
