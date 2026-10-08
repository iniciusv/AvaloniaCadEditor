using Avalonia.Controls;

namespace AvaloniaCadEditor.UI.Drafting;

/// <summary>Legenda flutuante das cotas: por nível, quantas aparecem, quantas a densidade escondeu e
/// o toggle para mostrá-las mesmo assim (ver <see cref="DraftingViewModel.Levels"/>).</summary>
public partial class DraftingLegend : UserControl
{
    public DraftingLegend()
    {
        InitializeComponent();
    }
}
