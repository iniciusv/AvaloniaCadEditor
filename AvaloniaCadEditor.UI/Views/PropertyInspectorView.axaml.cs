using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Views;

public partial class PropertyInspectorView : UserControl
{
    public PropertyInspectorView()
    {
        InitializeComponent();
    }

    private async void OnCopyNameClick(object? sender, RoutedEventArgs e)
    {
        // Copia o nome atual da feature, não o texto ainda não aplicado da caixa.
        if (DataContext is PropertyInspectorViewModel { SelectedFeatureName: { } name } &&
            TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(name);
        }
    }
}
