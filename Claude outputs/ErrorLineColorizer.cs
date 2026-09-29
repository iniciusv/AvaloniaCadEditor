using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

/// <summary>Pinta de vermelho as linhas que tiveram erro na última execução da cena.</summary>
public class ErrorLineColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush ErrorBackground = new SolidColorBrush(Color.Parse("#66B00020"));

    public HashSet<int> Lines { get; } = new();

    protected override void ColorizeLine(DocumentLine line)
    {
        if (Lines.Count == 0 || !Lines.Contains(line.LineNumber) || line.Length == 0) return;

        ChangeLinePart(line.Offset, line.EndOffset, element =>
        {
            element.TextRunProperties.SetBackgroundBrush(ErrorBackground);
        });
    }
}
