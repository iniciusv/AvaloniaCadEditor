using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.UI.Controls.Rendering;
using Microsoft.CodeAnalysis.Text;
using System;

namespace AvaloniaCadEditor.UI.Controls;

public static class CodeEditorBehavior
{
    private static readonly RoslynFeatureHighlighter _highlighter = new();

    public static readonly AttachedProperty<IFeature?> SelectedFeatureProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, IFeature?>("SelectedFeature", typeof(CodeEditorBehavior));


    public static IFeature? GetSelectedFeature(AvaloniaObject element) => element.GetValue(SelectedFeatureProperty);
    public static void SetSelectedFeature(AvaloniaObject element, IFeature? value) => element.SetValue(SelectedFeatureProperty, value);

    private static void HandleSelectedFeatureChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs e)
    {
        var colorizer = GetOrCreateColorizer(editor);
        var newFeature = e.NewValue as IFeature;
        
        System.Diagnostics.Debug.WriteLine($"[DEBUG-HIGHLIGHT] HandleSelectedFeatureChanged. newFeature: {newFeature?.Name}");

        if (newFeature != null && editor.Document != null)
        {
            var code = editor.Document.Text;
            var span = _highlighter.FindFeatureSpan(code, newFeature.Name);
            System.Diagnostics.Debug.WriteLine($"[DEBUG-HIGHLIGHT] Feature {newFeature.Name} found span: {span}");

            colorizer.HighlightSpan = span;
            
            if (span.HasValue)
            {
                try 
                {
                    var line = editor.Document.GetLineByOffset(span.Value.Start);
                    System.Diagnostics.Debug.WriteLine($"[DEBUG-HIGHLIGHT] Scrolling to line {line.LineNumber}");
                    editor.ScrollToLine(line.LineNumber);
                } 
                catch (ArgumentOutOfRangeException) { /* Ignorar problemas de scroll */ }
            }
        }
        else
        {
            colorizer.HighlightSpan = null;
        }

        editor.TextArea.TextView.Redraw();
    }

    private static FeatureHighlightColorizer GetOrCreateColorizer(TextEditor editor)
    {
        foreach (var transformer in editor.TextArea.TextView.LineTransformers)
        {
            if (transformer is FeatureHighlightColorizer existing)
                return existing;
        }

        var newColorizer = new FeatureHighlightColorizer();
        editor.TextArea.TextView.LineTransformers.Add(newColorizer);
        return newColorizer;
    }

    public static readonly AttachedProperty<string> DocumentTextProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, string>("DocumentText", typeof(CodeEditorBehavior));

    static CodeEditorBehavior()
    {
        SelectedFeatureProperty.Changed.AddClassHandler<TextEditor>((editor, e) => HandleSelectedFeatureChanged(editor, e));
        DocumentTextProperty.Changed.AddClassHandler<TextEditor>((editor, e) => HandleDocumentTextChanged(editor, e));
    }

    public static string GetDocumentText(AvaloniaObject element) => element.GetValue(DocumentTextProperty);
    public static void SetDocumentText(AvaloniaObject element, string value) => element.SetValue(DocumentTextProperty, value);

    private static void HandleDocumentTextChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs e)
    {
        if (editor.Document == null)
        {
            editor.Document = new AvaloniaEdit.Document.TextDocument();
            editor.TextChanged += (s, ev) =>
            {
                SetDocumentText(editor, editor.Document.Text);
            };
        }

        var newText = e.NewValue as string ?? string.Empty;
        if (editor.Document.Text != newText)
        {
            editor.Document.Text = newText;
        }
    }
}
