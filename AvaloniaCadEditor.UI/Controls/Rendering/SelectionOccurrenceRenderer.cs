using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

/// <summary>
/// Realça as outras ocorrências do texto selecionado no editor. É um renderer de fundo (e não
/// um colorizer) para não disputar o fundo do texto com o realce de feature e o de erro.
/// </summary>
public class SelectionOccurrenceRenderer : IBackgroundRenderer
{
    private static readonly IBrush OccurrenceBackground = new SolidColorBrush(Color.Parse("#90365F8C"));
    private static readonly IPen OccurrenceBorder = new Pen(new SolidColorBrush(Color.Parse("#FF6A9BD0")), 1);

    public List<TextSegment> Occurrences { get; } = new();

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Occurrences.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;

        int viewStart = textView.VisualLines.First().FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines.Last().LastDocumentLine.EndOffset;

        var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
        foreach (var segment in Occurrences)
        {
            if (segment.EndOffset < viewStart || segment.StartOffset > viewEnd) continue;
            builder.AddSegment(textView, segment);
            builder.CloseFigure();
        }

        var geometry = builder.CreateGeometry();
        if (geometry != null) drawingContext.DrawGeometry(OccurrenceBackground, OccurrenceBorder, geometry);
    }
}
