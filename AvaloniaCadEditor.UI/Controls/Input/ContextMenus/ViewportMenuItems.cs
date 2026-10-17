using System.Collections.Generic;
using Avalonia.Controls;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Controls.Input.ContextMenus;

/// <summary>
/// Itens do menu do botão direito da viewport. Toda ação pede um quadro novo ao terminar, porque o que ela muda
/// (seleção, referências, a cena) só aparece no próximo render.
/// </summary>
internal static class ViewportMenuItems
{
    public static MenuItem Action(string header, System.Action action, System.Action requestRender) => new()
    {
        Header = header,
        Command = new RelayCommand(() => { action(); requestRender(); })
    };

    public static MenuItem Toggle(string header, bool isChecked, System.Action<bool> apply, System.Action requestRender) => new()
    {
        Header = header,
        ToggleType = MenuItemToggleType.CheckBox,
        IsChecked = isChecked,
        Command = new RelayCommand(() => { apply(!isChecked); requestRender(); })
    };

    public static MenuItem Submenu(string header, IReadOnlyList<Control> items) => new()
    {
        Header = header,
        ItemsSource = items
    };
}
