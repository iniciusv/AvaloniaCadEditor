using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaCadEditor.UI.Reports;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Drafting;

/// <summary>Legenda flutuante das cotas: por nível, quantas aparecem, quantas a densidade escondeu e
/// o toggle para mostrá-las mesmo assim (ver <see cref="DraftingViewModel.Levels"/>).</summary>
public partial class DraftingLegend : UserControl
{
    public DraftingLegend()
    {
        InitializeComponent();
    }

    private void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DraftingViewModel drafting) return;
        var cad = TopLevel.GetTopLevel(this)?.DataContext as CadViewModel;
        _ = ReportClipboard.CopyAsync(this, drafting.BuildReport(), cad, "desenho de cotas");
    }
}
