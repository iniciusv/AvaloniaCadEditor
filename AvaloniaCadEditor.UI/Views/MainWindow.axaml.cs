using Avalonia.Controls;

namespace AvaloniaCadEditor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        GuideLabelsOverlay.AttachTo(ViewportControl);
        DraftingLabelsOverlay.AttachTo(ViewportControl);
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
        // Modo "Cotar" do desenho: o clique escolhe pontos de cota em vez de selecionar.
        if (DataContext is AvaloniaCadEditor.UI.ViewModels.CadViewModel { Drafting: { IsPicking: true } drafting } &&
            e.GetCurrentPoint(ViewportControl).Properties.IsLeftButtonPressed &&
            drafting.HandleClick(e.GetPosition(ViewportControl), ViewportControl.Camera, ViewportControl.Bounds.Size))
        {
            e.Handled = true;
            ViewportControl.RequestNextFrameRendering();
            return;
        }

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
            // Com uma peça isolada (H), as vistas seguem os eixos dela.
            var vm = DataContext as AvaloniaCadEditor.UI.ViewModels.CadViewModel;
            vm?.Drafting?.SetView(view);
            ViewportControl.SetStandardView(view, (float)(vm?.DisplayFrame?.AzimuthOffset ?? 0.0));
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
            // ESC no modo "Cotar" desiste da cota em andamento antes da cascata normal do ESC.
            if (e.Key == Avalonia.Input.Key.Escape && vm.Drafting is { IsPicking: true } drafting && drafting.CancelPending())
            {
                ViewportControl.RequestNextFrameRendering();
                return;
            }

            bool isCtrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
            vm.OnKeyDown(e.Key.ToString(), isCtrl);
            if (e.Key == Avalonia.Input.Key.Tab) e.Handled = true;
            ViewportControl.RequestNextFrameRendering();
        }
    }

    protected override void OnKeyUp(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (DataContext is AvaloniaCadEditor.UI.ViewModels.CadViewModel vm)
        {
            vm.OnKeyUp(e.Key.ToString());
            ViewportControl.RequestNextFrameRendering();
        }
    }
}