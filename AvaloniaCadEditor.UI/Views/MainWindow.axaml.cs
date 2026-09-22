using Avalonia.Controls;

namespace AvaloniaCadEditor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        this.DataContextChanged += (s, e) =>
        {
            if (DataContext is AvaloniaCadEditor.UI.ViewModels.CadViewModel vm)
            {
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(AvaloniaCadEditor.UI.ViewModels.CadViewModel.IsDrawingMode))
                    {
                        Cursor = vm.IsDrawingMode ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross) : Avalonia.Input.Cursor.Default;
                    }
                };
            }
        };
    }

    private void Viewport_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        ViewportControl.HandlePointerPressed(e);
    }

    private void Viewport_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        ViewportControl.HandlePointerMoved(e);
    }

    private void Viewport_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        ViewportControl.HandlePointerReleased(e);
    }

    private void Viewport_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        ViewportControl.HandlePointerWheelChanged(e);
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is AvaloniaCadEditor.UI.ViewModels.CadViewModel vm)
        {
            vm.OnKeyDown(e.Key.ToString());
            ViewportControl.RequestNextFrameRendering();
        }
    }
}