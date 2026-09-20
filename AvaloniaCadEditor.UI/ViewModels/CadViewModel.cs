using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.UI.ViewModels;

public class CadViewModel : INotifyPropertyChanged
{
    private readonly IGeometryEngine _geometryEngine;
    private readonly IOSnapService _osnapService;
    
    public Sketch Sketch { get; } = new Sketch();
    
    private ICommandTool? _currentTool;
    public ICommandTool? CurrentTool
    {
        get => _currentTool;
        set
        {
            _currentTool?.Cancel();
            _currentTool = value;
            OnPropertyChanged();
        }
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }
    
    private Point3D _currentSnappedPoint;
    public Point3D CurrentSnappedPoint
    {
        get => _currentSnappedPoint;
        set { _currentSnappedPoint = value; OnPropertyChanged(); }
    }

    public ICommand SelectLineToolCommand { get; }
    public ICommand SelectCircleToolCommand { get; }
    public ICommand SelectTrimToolCommand { get; }

    public ICommand SelectSelectToolCommand { get; }
    public ICommand SelectMoveToolCommand { get; }

    public bool OsnapEnabled
    {
        get => _osnapService.IsEnabled;
        set
        {
            _osnapService.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    public CadViewModel(IGeometryEngine geometryEngine, IOSnapService osnapService)
    {
        _geometryEngine = geometryEngine;
        _osnapService = osnapService;
        
        SelectLineToolCommand = new RelayCommand(() => CurrentTool = new DrawLineCommand());
        SelectCircleToolCommand = new RelayCommand(() => CurrentTool = new DrawCircleCommand());
        SelectTrimToolCommand = new RelayCommand(() => CurrentTool = new TrimCommand(_geometryEngine));

        SelectSelectToolCommand = new RelayCommand(() => CurrentTool = new SelectTool(_geometryEngine));
        SelectMoveToolCommand = new RelayCommand(() => CurrentTool = new MoveTool());

        CurrentTool = new DrawLineCommand();
    }

    public void OnPointerPressed(Point3D worldPoint)
    {
        var snapped = _osnapService.GetSnappedPoint(Sketch, worldPoint, 5.0); // 5.0 world units tolerance
        CurrentTool?.OnMouseDown(Sketch, snapped);
    }

    public void OnPointerMoved(Point3D worldPoint)
    {
        var snapped = _osnapService.GetSnappedPoint(Sketch, worldPoint, 5.0);
        CurrentSnappedPoint = snapped;
        CurrentTool?.OnMouseMove(Sketch, snapped);
        
        StatusText = $"X: {snapped.X:F2}, Y: {snapped.Y:F2}";
    }

    public void OnKeyDown(string keyName)
    {
        switch (keyName.ToUpperInvariant())
        {
            case "L":
                SelectLineToolCommand.Execute(null);
                break;
            case "C":
                SelectCircleToolCommand.Execute(null);
                break;
            case "T":
                SelectTrimToolCommand.Execute(null);
                break;
            case "O": // Placeholder for Orbit
                // SelectOrbitToolCommand.Execute(null);
                break;
            case "M":
                SelectMoveToolCommand.Execute(null);
                break;
            case "SPACE":
                SelectSelectToolCommand.Execute(null);
                break;
            case "ESCAPE":
                CurrentTool?.OnKeyDown(CadKey.Escape);
                CurrentTool?.Cancel();
                break;
            case "UP":
                CurrentTool?.OnKeyDown(CadKey.ArrowUp);
                break;
            case "DOWN":
                CurrentTool?.OnKeyDown(CadKey.ArrowDown);
                break;
            case "LEFT":
                CurrentTool?.OnKeyDown(CadKey.ArrowLeft);
                break;
            case "RIGHT":
                CurrentTool?.OnKeyDown(CadKey.ArrowRight);
                break;
            case "LEFTCTRL":
            case "RIGHTCTRL":
                CurrentTool?.OnKeyDown(CadKey.Control);
                break;
        }
    }

    public void SetTool(ICommandTool tool)
    {
        CurrentTool = tool;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
