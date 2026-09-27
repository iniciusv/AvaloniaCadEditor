using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
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
            var spans = _highlighter.FindFeatureSpans(code, newFeature.Name);
            System.Diagnostics.Debug.WriteLine($"[DEBUG-HIGHLIGHT] Feature {newFeature.Name} found {spans.Count} spans");

            colorizer.HighlightSpans = spans;
            
            // Scroll to the main target node specifically
            var targetSpan = _highlighter.FindFeatureSpan(code, newFeature.Name);
            if (targetSpan.HasValue)
            {
                try 
                {
                    var line = editor.Document.GetLineByOffset(targetSpan.Value.Start);
                    System.Diagnostics.Debug.WriteLine($"[DEBUG-HIGHLIGHT] Scrolling to line {line.LineNumber}");
                    editor.ScrollToLine(line.LineNumber);
                } 
                catch (ArgumentOutOfRangeException) { /* Ignorar problemas de scroll */ }
            }
        }
        else
        {
            colorizer.HighlightSpans.Clear();
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
        ApplyVSDarkThemeToCSharp();
        SelectedFeatureProperty.Changed.AddClassHandler<TextEditor>((editor, e) => HandleSelectedFeatureChanged(editor, e));
        DocumentTextProperty.Changed.AddClassHandler<TextEditor>((editor, e) => HandleDocumentTextChanged(editor, e));
    }

    private static void ApplyVSDarkThemeToCSharp()
    {
        var def = HighlightingManager.Instance.GetDefinition("C#");
        if (def == null) return;

        void SetColor(string name, string hex)
        {
            var color = def.GetNamedColor(name);
            if (color != null && Color.TryParse(hex, out var parsedColor))
            {
                color.Foreground = new SimpleHighlightingBrush(parsedColor);
            }
        }

        SetColor("Comment", "#57A64A");
        SetColor("String", "#D69D85");
        SetColor("StringInterpolation", "#D69D85");
        SetColor("Char", "#D69D85");
        SetColor("Preprocessor", "#9B9B9B");
        SetColor("Punctuation", "#DCDCDC");
        SetColor("NumberLiteral", "#B5CEA8");
        SetColor("MethodCall", "#DCDCAA");
        
        SetColor("TypeKeywords", "#4EC9B0");
        SetColor("SemanticKeywords", "#4EC9B0");
        
        string[] blueKeywords = {
            "ValueTypeKeywords", "ReferenceTypeKeywords", "ThisOrBaseReference", "NullOrValueKeywords",
            "Keywords", "GotoKeywords", "ContextKeywords", "ExceptionKeywords", "CheckedKeyword",
            "UnsafeKeywords", "OperatorKeywords", "ParameterModifiers", "Modifiers", "Visibility",
            "NamespaceKeywords", "GetSetAddRemove", "TrueFalse"
        };

        foreach (var kw in blueKeywords)
        {
            SetColor(kw, "#569CD6");
        }
    }

    public static string GetDocumentText(AvaloniaObject element) => element.GetValue(DocumentTextProperty);
    public static void SetDocumentText(AvaloniaObject element, string value) => element.SetValue(DocumentTextProperty, value);

    private static void HandleDocumentTextChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs e)
    {
        if (editor.Document == null)
        {
            editor.Document = new AvaloniaEdit.Document.TextDocument();
        }

        // Hook up the TextChanged event only once using a WeakReference or a flag.
        // But Avalonia doesn't provide an easy attached property bag for this without creating another property.
        // We can just use an attached property to store whether we've hooked it up.
        if (!GetIsTextChangedSubscribed(editor))
        {
            editor.TextChanged += (s, ev) =>
            {
                var currentText = editor.Document?.Text ?? string.Empty;
                var propText = GetDocumentText(editor);
                if (currentText != propText)
                {
                    SetDocumentText(editor, currentText);
                }
            };
            SetIsTextChangedSubscribed(editor, true);
        }

        var newText = e.NewValue as string ?? string.Empty;
        if (editor.Document.Text != newText)
        {
            editor.Document.Text = newText;
        }
    }

    private static readonly AttachedProperty<bool> IsTextChangedSubscribedProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, bool>("IsTextChangedSubscribed", typeof(CodeEditorBehavior));

    private static bool GetIsTextChangedSubscribed(AvaloniaObject element) => element.GetValue(IsTextChangedSubscribedProperty);
    private static void SetIsTextChangedSubscribed(AvaloniaObject element, bool value) => element.SetValue(IsTextChangedSubscribedProperty, value);
}
