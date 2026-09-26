using Avalonia.Media;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Document;
using Microsoft.CodeAnalysis.Text;
using System;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public class FeatureHighlightColorizer : DocumentColorizingTransformer
{
    private TextSpan? _highlightSpan;

    public TextSpan? HighlightSpan 
    { 
        get => _highlightSpan; 
        set => _highlightSpan = value; 
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!HighlightSpan.HasValue) return;

        int spanStart = HighlightSpan.Value.Start;
        int spanEnd = HighlightSpan.Value.End;
        int lineStart = line.Offset;
        int lineEnd = line.Offset + line.Length;

        if (spanStart < lineEnd && spanEnd > lineStart)
        {
            int start = Math.Max(spanStart, lineStart);
            int end = Math.Min(spanEnd, lineEnd);

            ChangeLinePart(start, end, element => 
            {
                element.TextRunProperties.SetBackgroundBrush(new SolidColorBrush(Color.Parse("#8827AE60"))); 
            });
        }
    }
}
