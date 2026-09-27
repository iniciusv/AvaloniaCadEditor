using Avalonia.Media;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Document;
using Microsoft.CodeAnalysis.Text;
using System;

using System.Collections.Generic;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public class FeatureHighlightColorizer : DocumentColorizingTransformer
{
    private List<TextSpan> _highlightSpans = new();

    public List<TextSpan> HighlightSpans 
    { 
        get => _highlightSpans; 
        set => _highlightSpans = value ?? new List<TextSpan>(); 
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (HighlightSpans.Count == 0) return;

        int lineStart = line.Offset;
        int lineEnd = line.Offset + line.Length;

        foreach (var span in HighlightSpans)
        {
            int spanStart = span.Start;
            int spanEnd = span.End;

            if (spanStart < lineEnd && spanEnd > lineStart)
            {
                int start = Math.Max(spanStart, lineStart);
                int end = Math.Min(spanEnd, lineEnd);

                ChangeLinePart(start, end, element => 
                {
                    // Melhora a cor: Um tom de azul-esverdeado suave para nÃ£o ofuscar a sintaxe. (Teal translÃºcido)
                    element.TextRunProperties.SetBackgroundBrush(new SolidColorBrush(Color.Parse("#4400B4D8"))); 
                });
            }
        }
    }
}
