using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace AvaloniaCadEditor.UI.Controls;

/// <summary>
/// Canvas transparente e sem hit-test sobre o <see cref="SilkViewportControl"/> que desenha o
/// retângulo da seleção por arraste: azul com borda contínua para janela (esquerda → direita) e
/// verde com borda tracejada para crossing (direita → esquerda), como no AutoCAD.
/// </summary>
public sealed class SelectionBoxOverlay : Canvas
{
    private static readonly IBrush WindowFill = new SolidColorBrush(Color.FromArgb(40, 60, 130, 255));
    private static readonly IBrush WindowStroke = new SolidColorBrush(Color.FromRgb(90, 150, 255));
    private static readonly IBrush CrossingFill = new SolidColorBrush(Color.FromArgb(40, 60, 200, 90));
    private static readonly IBrush CrossingStroke = new SolidColorBrush(Color.FromRgb(90, 220, 110));

    private SilkViewportControl? _viewport;
    private readonly Rectangle _box = new() { StrokeThickness = 1, IsVisible = false };

    public SelectionBoxOverlay()
    {
        IsHitTestVisible = false;
        Children.Add(_box);
    }

    public void AttachTo(SilkViewportControl viewport)
    {
        if (_viewport != null) _viewport.SelectionBoxChanged -= OnSelectionBoxChanged;
        _viewport = viewport;
        _viewport.SelectionBoxChanged += OnSelectionBoxChanged;
    }

    private void OnSelectionBoxChanged(Rect? rect, bool window)
    {
        if (rect is not { } r)
        {
            _box.IsVisible = false;
            return;
        }

        _box.Fill = window ? WindowFill : CrossingFill;
        _box.Stroke = window ? WindowStroke : CrossingStroke;
        _box.StrokeDashArray = window ? null : new AvaloniaList<double> { 4, 3 };
        _box.Width = r.Width;
        _box.Height = r.Height;
        SetLeft(_box, r.X);
        SetTop(_box, r.Y);
        _box.IsVisible = true;
    }
}
