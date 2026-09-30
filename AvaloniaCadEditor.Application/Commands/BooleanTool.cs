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
    /// Disparado, em vez de <see cref="CodeGenerated"/>, quando a operação tem uma base só e ela já é
    /// o resultado de uma booleana do mesmo tipo: as ferramentas são acrescentadas à feature existente
    /// (ver <see cref="AvaloniaCadEditor.Application.Services.RoslynBooleanOperandAppender"/>) em vez
    /// de encadear mais uma operação em cima dela. <c>FallbackCode</c> é o código equivalente ao
    /// caminho antigo (uma BooleanFeature nova encadeada); quem ouvir o evento deve usá-lo se o
    /// acréscimo ao script falhar (formato de código não reconhecido).
    /// </summary>
    public event EventHandler<(string BooleanVar, IReadOnlyList<string> ToolVars, string FallbackCode)>? OperandAppendRequested;

    private readonly BooleanOperationType _operationType;
    private readonly ISelectionService _selectionService;
    private Sketch? _sketch;

    public string Name => $"{_operationType}";

    private enum State { SelectingBase, SelectingTool }
    private State _currentState = State.SelectingBase;

    private readonly List<IFeature> _bases = new();
    private readonly List<IFeature> _tools = new();

    /// <param name="preselected">
    /// Sólidos já marcados no viewport/árvore antes de abrir a ferramenta (Ctrl+clique prévio):
    /// todos entram direto como bases, e a ferramenta já começa pedindo as ferramentas de corte/união.
    /// </param>
    public BooleanTool(BooleanOperationType operationType, ISelectionService selectionService, IEnumerable<IFeature>? preselected = null)
    {
        _operationType = operationType;
        _selectionService = selectionService;
        _selectionService.SelectionChanged += OnSelectionChanged;

        foreach (var feature in preselected ?? Enumerable.Empty<IFeature>())
        {
            if (feature == null || feature is WorkPlaneFeature) continue;
            if (_bases.Contains(feature)) continue;
            _bases.Add(feature);
            feature.IsSelected = true;
        }

        if (_bases.Count > 0)
        {
            _currentState = State.SelectingTool;
        }
    }

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        _sketch = sketch;
    }

    private void OnSelectionChanged()
    {
        if (_selectionService.CurrentSelectionType != SelectionType.Feature) return;
        if (_selectionService.SelectedObject is not IFeature feature) return;

        // Seleção feita pela árvore de modelo (clique simples, sem Ctrl): sempre substitui/avança.
        PickFeature(feature, additive: false);
    }

    /// <summary>
    /// Escolhe (ou, com <paramref name="additive"/>, alterna) um sólido como base ou ferramenta,
    /// dependendo do estado atual. <paramref name="additive"/> corresponde ao Ctrl+clique: acumula
    /// mais bases/ferramentas em vez de confirmar na hora. Um clique simples confirma imediatamente
    /// (avança de base para ferramenta, ou executa a operação se já estiver escolhendo ferramentas).
    /// </summary>
    public void PickFeature(IFeature? feature, bool additive)
    {
        if (feature == null || feature is WorkPlaneFeature) return;

        if (_currentState == State.SelectingBase)
        {
            if (_bases.Contains(feature))
            {
                if (additive)
                {
                    _bases.Remove(feature);
                    feature.IsSelected = false;
                }
                return;
            }

            _bases.Add(feature);
            feature.IsSelected = true;

            if (!additive)
            {
                _currentState = State.SelectingTool;
            }
        }
        else if (_currentState == State.SelectingTool)
        {
            if (_bases.Contains(feature)) return; // não pode ser ferramenta da própria base

            if (_tools.Contains(feature))
            {
                if (additive)
                {
                    _tools.Remove(feature);
                    feature.IsSelected = false;
                }
                return;
            }

            _tools.Add(feature);
            feature.IsSelected = true;

            if (!additive && _sketch != null)
            {
                Execute(_sketch);
            }
        }
    }

    private void Execute(Sketch sketch)
    {
        if (_bases.Count == 0) return;

        bool pureUnionOfBases = _operationType == BooleanOperationType.Union && _tools.Count == 0 && _bases.Count >= 2;
        if (_tools.Count == 0 && !pureUnionOfBases) return;

        var baseVars = _bases.Select(AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName).ToList();
        var toolVars = _tools.Select(AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName).ToList();

        string id = Guid.NewGuid().ToString("N").Substring(0, 4);
        string newBoolVar = $"boolOp_{id}";
        string toolsArg = $"new IFeature[] {{ {string.Join(", ", toolVars)} }}";

        string fallbackCode = baseVars.Count == 1
            ? $"var {newBoolVar} = new BooleanFeature(name: \"{newBoolVar}\", operation: BooleanOperationType.{_operationType}, baseFeature: {baseVars[0]}, toolFeatures: {toolsArg});\r\nAddFeature({newBoolVar});"
            : $"var {newBoolVar} = new BooleanFeature(name: \"{newBoolVar}\", operation: BooleanOperationType.{_operationType}, baseFeatures: new IFeature[] {{ {string.Join(", ", baseVars)} }}, toolFeatures: {toolsArg});\r\nAddFeature({newBoolVar});";

        // Uma base só, e ela já é uma booleana do mesmo tipo: acrescenta as ferramentas a ela em vez
        // de encadear outra BooleanFeature em cima do resultado (uma operação só, árvore plana).
        if (_bases.Count == 1 && toolVars.Count > 0 &&
            _bases[0] is BooleanFeature existingBoolean && existingBoolean.Operation == _operationType)
        {
            OperandAppendRequested?.Invoke(this, (baseVars[0], toolVars, fallbackCode));
            Cancel();
            return;
        }

        CodeGenerated?.Invoke(this, fallbackCode);
        Cancel();
    }

    public void OnMouseMove(Sketch sketch, Point3D point) { }

    public void Cancel()
    {
        foreach (var f in _bases) f.IsSelected = false;
        foreach (var f in _tools) f.IsSelected = false;
        _bases.Clear();
        _tools.Clear();
        _currentState = State.SelectingBase;
        _sketch = null;
    }

    public void Dispose()
    {
        _selectionService.SelectionChanged -= OnSelectionChanged;
    }

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape)
        {
            Cancel();
        }
        else if (key == CadKey.Enter)
        {
            if (_currentState == State.SelectingBase && _bases.Count > 0)
            {
                _currentState = State.SelectingTool;
            }
            else if (_currentState == State.SelectingTool)
            {
                _sketch ??= sketch;
                Execute(sketch);
            }
        }
    }

    public void OnValueEntered(Sketch sketch, double value) { }

    public string GetMeasurementText() => "";

    public string GetStatusText()
    {
        string opName = _operationType == BooleanOperationType.Subtract ? "Subtrair (Corte)" : "Unir";
        const string hint = "Ctrl+clique acumula/remove · Enter confirma · Esc cancela";
        return _currentState switch
        {
            State.SelectingBase => $"Operação {opName}: selecione a(s) BASE(s) ({_bases.Count} escolhida(s)) — {hint}",
            State.SelectingTool => $"Operação {opName}: selecione a(s) FERRAMENTA(s) ({_tools.Count} escolhida(s)) para {opName.ToLowerInvariant()} {_bases.Count} base(s) — {hint}",
            _ => ""
        };
    }
}



