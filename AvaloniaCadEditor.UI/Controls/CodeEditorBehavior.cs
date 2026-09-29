using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.UI.Controls.Rendering;
using AvaloniaCadEditor.UI.Resources;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Linq;

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

            // Instâncias de peça (e features sem variável com o mesmo nome) não aparecem pelo
            // nome no código: destaca a instrução da linha onde foram registradas.
            if (spans.Count == 0 && AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.Of(newFeature) is { Line: > 0 } origin)
            {
                var statement = AvaloniaCadEditor.Application.Scenes.PartSourceEditor.FindStatementSpan(code, origin.Line);
                if (statement.HasValue) spans.Add(statement.Value);
            }

            colorizer.HighlightSpans = spans;
            
            // Scroll to the main target node specifically
            var targetSpan = _highlighter.GetFeatureSpans(code, newFeature.Name).FirstOrDefault();
            if (targetSpan != default)
            {
                try 
                {
                    var line = editor.Document.GetLineByOffset(targetSpan.Start);
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
        ErrorLinesProperty.Changed.AddClassHandler<TextEditor>((editor, e) => HandleErrorLinesChanged(editor, e));
        DocumentTextProperty.Changed.AddClassHandler<TextEditor>((editor, e) => HandleDocumentTextChanged(editor, e));
        RevealLineProperty.Changed.AddClassHandler<TextEditor>((editor, e) => HandleRevealLineChanged(editor, e));
        CodeCommandsProperty.Changed.AddClassHandler<TextEditor>((editor, e) => HandleCodeCommandsChanged(editor));
        FocusWhenProperty.Changed.AddClassHandler<TextBox>((box, e) => HandleFocusWhenChanged(box, e));
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

    /// <summary>Linhas (1-based) com erro na última execução; pintadas de vermelho no editor.</summary>
    public static readonly AttachedProperty<System.Collections.Generic.IReadOnlyList<int>?> ErrorLinesProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, System.Collections.Generic.IReadOnlyList<int>?>("ErrorLines", typeof(CodeEditorBehavior));

    public static System.Collections.Generic.IReadOnlyList<int>? GetErrorLines(AvaloniaObject element) => element.GetValue(ErrorLinesProperty);
    public static void SetErrorLines(AvaloniaObject element, System.Collections.Generic.IReadOnlyList<int>? value) => element.SetValue(ErrorLinesProperty, value);

    private static void HandleErrorLinesChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs e)
    {
        ErrorLineColorizer? colorizer = null;
        foreach (var transformer in editor.TextArea.TextView.LineTransformers)
        {
            if (transformer is ErrorLineColorizer existing)
            {
                colorizer = existing;
                break;
            }
        }
        if (colorizer == null)
        {
            colorizer = new ErrorLineColorizer();
            editor.TextArea.TextView.LineTransformers.Add(colorizer);
        }

        colorizer.Lines.Clear();
        if (e.NewValue is System.Collections.Generic.IReadOnlyList<int> lines)
        {
            foreach (var line in lines) colorizer.Lines.Add(line);
            if (lines.Count > 0 && editor.Document != null && lines[0] <= editor.Document.LineCount)
            {
                editor.ScrollToLine(lines[0]);
            }
        }
        editor.TextArea.TextView.Redraw();
    }

    /// <summary>Linha que o editor deve mostrar (rola até ela e põe o cursor no início).</summary>
    public static readonly AttachedProperty<AvaloniaCadEditor.UI.ViewModels.EditorLineRequest?> RevealLineProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, AvaloniaCadEditor.UI.ViewModels.EditorLineRequest?>("RevealLine", typeof(CodeEditorBehavior));

    public static AvaloniaCadEditor.UI.ViewModels.EditorLineRequest? GetRevealLine(AvaloniaObject element) => element.GetValue(RevealLineProperty);
    public static void SetRevealLine(AvaloniaObject element, AvaloniaCadEditor.UI.ViewModels.EditorLineRequest? value) => element.SetValue(RevealLineProperty, value);

    private static void HandleRevealLineChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not AvaloniaCadEditor.UI.ViewModels.EditorLineRequest request) return;

        // Adiado: o pedido costuma chegar junto com a troca de arquivo ou de aba, antes do layout.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (editor.Document == null || request.Line < 1 || request.Line > editor.Document.LineCount) return;
            try
            {
                editor.TextArea.Caret.Line = request.Line;
                editor.TextArea.Caret.Column = Math.Max(1, request.Column);
                if (request.SelectionLength > 0)
                {
                    int start = editor.Document.GetOffset(request.Line, Math.Max(1, request.Column));
                    int length = Math.Min(request.SelectionLength, editor.Document.TextLength - start);
                    if (length > 0) editor.Select(start, length);
                }
                editor.ScrollToLine(request.Line);
                // Só navegação explícita (F12, lista de referências) leva o foco ao editor;
                // selecionar uma peça na árvore não deve tirar o foco dela.
                if (request.SelectionLength > 0) editor.TextArea.Focus();
            }
            catch (ArgumentOutOfRangeException) { /* documento trocou no meio do caminho */ }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Comandos de navegação do editor: F12 (ir para a definição), Shift+F12 (referências) e
    /// F2 (renomear), sempre sobre o símbolo sob o cursor.
    /// </summary>
    public static readonly AttachedProperty<AvaloniaCadEditor.UI.ViewModels.ICodeEditorCommands?> CodeCommandsProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, AvaloniaCadEditor.UI.ViewModels.ICodeEditorCommands?>("CodeCommands", typeof(CodeEditorBehavior));

    public static AvaloniaCadEditor.UI.ViewModels.ICodeEditorCommands? GetCodeCommands(AvaloniaObject element) => element.GetValue(CodeCommandsProperty);
    public static void SetCodeCommands(AvaloniaObject element, AvaloniaCadEditor.UI.ViewModels.ICodeEditorCommands? value) => element.SetValue(CodeCommandsProperty, value);

    private static readonly AttachedProperty<bool> IsKeyHandlerSubscribedProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, bool>("IsKeyHandlerSubscribed", typeof(CodeEditorBehavior));

    private static void HandleCodeCommandsChanged(TextEditor editor)
    {
        if (editor.GetValue(IsKeyHandlerSubscribedProperty)) return;
        editor.SetValue(IsKeyHandlerSubscribedProperty, true);

        // Tunnel: o editor consumiria F2/F12 antes de um handler normal.
        editor.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            var commands = GetCodeCommands(editor);
            if (commands == null) return;

            int offset = editor.CaretOffset;
            if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.Shift)
            {
                commands.FindReferencesAt(offset);
                e.Handled = true;
            }
            else if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
            {
                commands.GoToDefinitionAt(offset);
                e.Handled = true;
            }
            else if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None)
            {
                commands.BeginRenameAt(offset);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        // Botão direito: leva o cursor até onde se clicou (o menu age sobre o nome clicado),
        // a menos que o clique caia dentro de uma seleção que o usuário quer copiar.
        editor.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(editor).Properties.IsRightButtonPressed || editor.Document == null) return;
            var position = editor.GetPositionFromPoint(e.GetPosition(editor));
            if (position == null) return;

            int offset = editor.Document.GetOffset(position.Value.Location);
            bool insideSelection = editor.SelectionLength > 0 &&
                                   offset >= editor.SelectionStart && offset <= editor.SelectionStart + editor.SelectionLength;
            if (!insideSelection)
            {
                editor.TextArea.ClearSelection();
                editor.TextArea.Caret.Position = position.Value;
            }
        }, RoutingStrategies.Tunnel);

        editor.ContextMenu = BuildContextMenu(editor);
    }

    /// <summary>Menu do editor: navegação no código (com os atalhos visíveis) e área de transferência.</summary>
    private static ContextMenu BuildContextMenu(TextEditor editor)
    {
        MenuItem Item(string header, KeyGesture? gesture, Action action) => new MenuItem
        {
            Header = header,
            InputGesture = gesture,
            Command = new AvaloniaCadEditor.UI.ViewModels.RelayCommand(action)
        };

        void WithCommands(Action<AvaloniaCadEditor.UI.ViewModels.ICodeEditorCommands> action)
        {
            var commands = GetCodeCommands(editor);
            if (commands != null) action(commands);
        }

        return new ContextMenu
        {
            ItemsSource = new Control[]
            {
                Item(Strings.GoToDefinition, new KeyGesture(Key.F12), () => WithCommands(c => c.GoToDefinitionAt(editor.CaretOffset))),
                Item(Strings.FindReferences, new KeyGesture(Key.F12, KeyModifiers.Shift), () => WithCommands(c => c.FindReferencesAt(editor.CaretOffset))),
                Item(Strings.RenameSymbol, new KeyGesture(Key.F2), () => WithCommands(c => c.BeginRenameAt(editor.CaretOffset))),
                new Separator(),
                Item(Strings.CutText, new KeyGesture(Key.X, KeyModifiers.Control), () => editor.Cut()),
                Item(Strings.CopyText, new KeyGesture(Key.C, KeyModifiers.Control), () => editor.Copy()),
                Item(Strings.PasteText, new KeyGesture(Key.V, KeyModifiers.Control), () => editor.Paste()),
            }
        };
    }

    /// <summary>Dá foco (e seleciona o texto) a uma caixa quando o valor vira true, ex.: a caixa de renomear.</summary>
    public static readonly AttachedProperty<bool> FocusWhenProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("FocusWhen", typeof(CodeEditorBehavior));

    public static bool GetFocusWhen(AvaloniaObject element) => element.GetValue(FocusWhenProperty);
    public static void SetFocusWhen(AvaloniaObject element, bool value) => element.SetValue(FocusWhenProperty, value);

    private static void HandleFocusWhenChanged(TextBox box, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
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
