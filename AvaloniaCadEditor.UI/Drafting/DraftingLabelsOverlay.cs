using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.UI.Controls;
using AvaloniaCadEditor.UI.ReferenceGuides;
using AvaloniaCadEditor.UI.Reports;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Drafting;

/// <summary>
/// Textos das cotas sobre a viewport (como o <see cref="GuideLabelsOverlay"/>). Os textos são
/// clicáveis — o canvas não tem fundo, então só eles recebem o clique e o resto passa para a
/// viewport: no modo "Cotar" o clique esconde/remove a cota; fora dele, mostra o sólido no código.
/// Alt+clique, em qualquer modo, copia como texto a cadeia (fileira) da cota.
/// </summary>
public sealed class DraftingLabelsOverlay : Canvas
{
    private SilkViewportControl? _viewport;
    private DraftingViewModel? _drafting;
    private readonly List<(TextBlock Block, DraftDimension Dimension, LabelAnchor Anchor)> _labels = new();

    public DraftingLabelsOverlay()
    {
        Background = null;
        DataContextChanged += (_, _) => Rebind();
    }

    public void AttachTo(SilkViewportControl viewport)
    {
        if (_viewport != null) _viewport.FrameRendered -= Reposition;
        _viewport = viewport;
        _viewport.FrameRendered += Reposition;
    }

    private void Rebind()
    {
        if (_drafting != null) _drafting.Changed -= OnChanged;
        _drafting = (DataContext as CadViewModel)?.Drafting;
        if (_drafting != null) _drafting.Changed += OnChanged;
        Rebuild();
    }

    private void OnChanged() => Dispatcher.UIThread.Post(() =>
    {
        Rebuild();
        _viewport?.RequestNextFrameRendering();
    });

    private void Rebuild()
    {
        Children.Clear();
        _labels.Clear();

        if (_drafting is not { IsVisible: true, Sheet: not null } drafting) return;

        double arrow = drafting.ArrowSize;
        foreach (var dimension in drafting.Dimensions)
        {
            var graphics = DimensionGraphics.Build(dimension, arrow);
            string text = drafting.ParameterHints.TryGetValue(dimension.Key, out var hint)
                ? $"{dimension.Text} · {hint}"
                : dimension.Text;

            var block = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Padding = new Thickness(2, 0),
                Foreground = dimension.IsManual ? Brushes.Gold : new SolidColorBrush(Color.FromRgb(140, 215, 255)),
                Background = new SolidColorBrush(Color.FromArgb(190, 20, 20, 20)),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            if (graphics.Placement == LabelPlacement.LeftRotated)
            {
                block.RenderTransform = new RotateTransform(-90);
                block.RenderTransformOrigin = RelativePoint.Center;
            }
            var sources = dimension.SourceNames.Count > 0 ? string.Join(", ", dimension.SourceNames) : "peça";
            ToolTip.SetTip(block, $"{sources}\nAlt+clique: copiar a cadeia");

            var captured = dimension;
            block.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(block).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    _ = ReportClipboard.CopyAsync(this, _drafting?.BuildChainReport(captured), DataContext as CadViewModel, "cadeia de cotas");
                    return;
                }
                _drafting?.OnLabelClicked(captured);
            };

            block.Measure(Size.Infinity);
            Children.Add(block);
            _labels.Add((block, dimension, new LabelAnchor(graphics.LabelAnchor, graphics.Placement)));
        }

        Reposition();
    }

    private void Reposition()
    {
        if (_viewport == null || _drafting == null) return;

        bool showing = _drafting.IsShowing(_viewport.Camera) && _drafting.Sheet != null;
        var size = _viewport.Bounds.Size;
        foreach (var (block, _, anchor) in _labels)
        {
            var screen = showing ? ScreenProjection.WorldToScreen(_viewport.Camera, _drafting.ToWorld(anchor.Point), size) : null;
            if (screen == null)
            {
                block.IsVisible = false;
                continue;
            }

            block.IsVisible = true;
            var w = block.DesiredSize.Width;
            var h = block.DesiredSize.Height;
            var (x, y) = (screen.Value.X, screen.Value.Y);
            switch (anchor.Placement)
            {
                case LabelPlacement.Above:
                    SetLeft(block, x - w / 2);
                    SetTop(block, y - h - 1);
                    break;
                case LabelPlacement.LeftRotated:
                    // Girado 90° em torno do centro: o centro visual fica à esquerda da linha.
                    SetLeft(block, x - h / 2 - 1 - w / 2);
                    SetTop(block, y - h / 2);
                    break;
                default:
                    SetLeft(block, x + 2);
                    SetTop(block, y - h / 2);
                    break;
            }
        }
    }

    private readonly record struct LabelAnchor(P2 Point, LabelPlacement Placement);
}
