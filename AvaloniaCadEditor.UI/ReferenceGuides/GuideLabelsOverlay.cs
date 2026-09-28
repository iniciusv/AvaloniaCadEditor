using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaCadEditor.Domain.ReferenceGuides;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.Controls;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.ReferenceGuides;

/// <summary>
/// Transparent, non-hit-testable canvas stacked over <see cref="SilkViewportControl"/> that
/// shows the reference-guide dimension labels (the GL layer has no text rendering). Labels are
/// only rebuilt when the analysis result or visibility changes; on every camera-driven redraw
/// they are just repositioned, which is cheap even for dozens of labels.
///
/// Wiring is a single call from the window's code-behind: <see cref="AttachTo"/>.
/// </summary>
public sealed class GuideLabelsOverlay : Canvas
{
    private SilkViewportControl? _viewport;
    private ReferenceGuidesViewModel? _guidesVm;
    private readonly List<(TextBlock Block, Point3D World)> _labels = new();

    public GuideLabelsOverlay()
    {
        IsHitTestVisible = false;
        DataContextChanged += (_, _) => Rebind();
    }

    /// <summary>Connects this overlay to the viewport it sits on top of, for camera access and
    /// per-frame repositioning.</summary>
    public void AttachTo(SilkViewportControl viewport)
    {
        if (_viewport != null) _viewport.FrameRendered -= RepositionLabels;
        _viewport = viewport;
        _viewport.FrameRendered += RepositionLabels;
    }

    private void Rebind()
    {
        if (_guidesVm != null) _guidesVm.PropertyChanged -= OnGuidesPropertyChanged;
        _guidesVm = (DataContext as CadViewModel)?.ReferenceGuides;
        if (_guidesVm != null) _guidesVm.PropertyChanged += OnGuidesPropertyChanged;
        Rebuild();
    }

    private void OnGuidesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReferenceGuidesViewModel.Result) or nameof(ReferenceGuidesViewModel.IsVisible))
        {
            Dispatcher.UIThread.Post(Rebuild);
        }
    }

    private void Rebuild()
    {
        Children.Clear();
        _labels.Clear();

        if (_guidesVm is not { IsVisible: true, Result: { } result })
        {
            return;
        }

        foreach (var dimension in result.Dimensions)
        {
            var midpoint = new Point3D(
                (dimension.From.X + dimension.To.X) / 2.0,
                (dimension.From.Y + dimension.To.Y) / 2.0,
                (dimension.From.Z + dimension.To.Z) / 2.0);

            var block = new TextBlock
            {
                Text = dimension.Label,
                FontSize = 11,
                Padding = new Thickness(3, 1),
                Foreground = BrushFor(dimension.Kind),
                Background = new SolidColorBrush(Color.FromArgb(170, 20, 20, 20))
            };
            Children.Add(block);
            _labels.Add((block, midpoint));
        }

        RepositionLabels();
    }

    private void RepositionLabels()
    {
        if (_viewport == null || _labels.Count == 0) return;

        var size = _viewport.Bounds.Size;
        foreach (var (block, world) in _labels)
        {
            var screen = ScreenProjection.WorldToScreen(_viewport.Camera, world, size);
            if (screen == null)
            {
                block.IsVisible = false;
                continue;
            }

            block.IsVisible = true;
            SetLeft(block, screen.Value.X + 4);
            SetTop(block, screen.Value.Y - 8);
        }
    }

    private static IBrush BrushFor(GuideKind kind) => kind switch
    {
        GuideKind.HoleRow => Brushes.Cyan,
        GuideKind.Wall => Brushes.Orange,
        GuideKind.ArcCenter => Brushes.Yellow,
        GuideKind.Symmetry => Brushes.LightGreen,
        GuideKind.NearMiss => Brushes.OrangeRed,
        _ => Brushes.White
    };
}
