using Avalonia.Controls;

namespace AvaloniaCadEditor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        GuideLabelsOverlay.AttachTo(ViewportControl);
        SelectionBoxOverlay.AttachTo(ViewportControl);

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

    private void StandardView_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } &&
            System.Enum.TryParse<AvaloniaCadEditor.Domain.Entities.StandardView>(tag, out var view))
        {
            ViewportControl.SetStandardView(view);
        }
    }

    private void Projection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Primitives.ToggleButton toggle)
        {
            ViewportControl.SetOrthographic(toggle.IsChecked == true);
        }
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager?.GetFocusedElement() as Avalonia.Visual;
        for (var v = focused; v != null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v) as Avalonia.Visual)
        {
            if (v is TextBox || v is AvaloniaEdit.TextEditor || v is AvaloniaEdit.Editing.TextArea)
                return true;
        }
        return false;
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsTextInputFocused()) return;
        if (DataContext is AvaloniaCadEditor.UI.ViewModels.CadViewModel vm)
        {
            bool isCtrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
            vm.OnKeyDown(e.Key.ToString(), isCtrl);
            if (e.Key == Avalonia.Input.Key.Tab) e.Handled = true;
            ViewportControl.RequestNextFrameRendering();
        }
    }
}