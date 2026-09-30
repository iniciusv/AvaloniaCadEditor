using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class BooleanTool : ICommandTool, IDisposable
{
    public event EventHandler<string>? CodeGenerated;

    /// <summary>
    /// Disparado, em vez de <see cref="CodeGenerated"/>, quando a base escolhida já é o resultado de
    /// uma booleana do mesmo tipo: a ferramenta é acrescentada à feature existente (ver
    /// <see cref="AvaloniaCadEditor.Application.Services.RoslynBooleanOperandAppender"/>) em vez de
    /// encadear mais uma operação em cima dela. <c>FallbackCode</c> é o código equivalente ao caminho
    /// antigo (uma BooleanFeature nova encadeada); quem ouvir o evento deve usá-lo se o acréscimo ao
    /// script falhar (formato de código não reconhecido).
    /// </summary>
    public event EventHandler<(string BooleanVar, string ToolVar, string FallbackCode)>? OperandAppendRequested;

    private readonly BooleanOperationType _operationType;
    private readonly ISelectionService _selectionService;
    private Sketch? _sketch;

    public string Name => $"{_operationType}";

    private enum State { SelectingBase, SelectingTool }
    private State _currentState = State.SelectingBase;

    private IFeature? _baseFeature;
    private IFeature? _toolFeature;

    public BooleanTool(BooleanOperationType operationType, ISelectionService selectionService)
    {
        _operationType = operationType;
        _selectionService = selectionService;
        _selectionService.SelectionChanged += OnSelectionChanged;
    }

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        _sketch = sketch;
        if (_currentState == State.SelectingTool && _baseFeature != null && _toolFeature != null)
        {
            Execute(_sketch);
        }
    }

    private void OnSelectionChanged()
    {
        if (_selectionService.CurrentSelectionType != SelectionType.Feature) return;
        if (_selectionService.SelectedObject is not IFeature feature) return;
        if (feature is WorkPlaneFeature) return;

        if (_currentState == State.SelectingBase)
        {
            _baseFeature = feature;
            _currentState = State.SelectingTool;
        }
        else if (_currentState == State.SelectingTool && feature != _baseFeature)
        {
            _toolFeature = feature;
            if (_sketch != null)
            {
                Execute(_sketch);
            }
        }
    }

    private void Execute(Sketch sketch)
    {
        if (_baseFeature != null && _toolFeature != null)
        {
            string toolVar = AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName(_toolFeature);
            string baseVar = AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName(_baseFeature);

            string id = Guid.NewGuid().ToString("N").Substring(0, 4);
            string newBoolVar = $"boolOp_{id}";
            string fallbackCode = $"var {newBoolVar} = new BooleanFeature(name: \"{newBoolVar}\", operation: BooleanOperationType.{_operationType}, baseFeature: {baseVar}, toolFeatures: new IFeature[] {{ {toolVar} }});\r\nAddFeature({newBoolVar});";

            // A base já é uma booleana do mesmo tipo: acrescenta a ferramenta a ela em vez de
            // encadear outra BooleanFeature em cima do resultado (uma operação só, árvore plana).
            if (_baseFeature is BooleanFeature existingBoolean && existingBoolean.Operation == _operationType)
            {
                OperandAppendRequested?.Invoke(this, (baseVar, toolVar, fallbackCode));
                Cancel();
                return;
            }

            CodeGenerated?.Invoke(this, fallbackCode);
            Cancel();
        }
    }

    public void OnMouseMove(Sketch sketch, Point3D point) { }

    public void Cancel()
    {
        _currentState = State.SelectingBase;
        _baseFeature = null;
        _toolFeature = null;
        _sketch = null;
    }

    public void Dispose()
    {
        _selectionService.SelectionChanged -= OnSelectionChanged;
    }

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape) Cancel();
    }

    public void OnValueEntered(Sketch sketch, double value) { }

    public string GetMeasurementText() => "";

    public string GetStatusText()
    {
        string opName = _operationType == BooleanOperationType.Subtract ? "Subtrair (Corte)" : "Unir";
        return _currentState switch
        {
            State.SelectingBase => $"Operação {opName}: 1/2 Selecione o Volume BASE",
            State.SelectingTool => $"Operação {opName}: 2/2 Selecione o Volume FERRAMENTA para {_operationType} de {_baseFeature?.Name}",
            _ => ""
        };
    }
}



