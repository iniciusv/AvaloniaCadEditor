using System.Collections.Generic;
using Avalonia.Controls;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.Resources;
using AvaloniaCadEditor.UI.ViewModels;
using static AvaloniaCadEditor.UI.Controls.Input.ContextMenus.ViewportMenuItems;

namespace AvaloniaCadEditor.UI.Controls.Input.ContextMenus;

/// <summary>
/// Menu do botão direito sobre a seleção: espelhar (ou copiar espelhado) pelo centro da seleção em X/Y/Z, ou
/// escolher o plano com o mouse (<see cref="AvaloniaCadEditor.Application.Commands.MirrorTool"/>). Sem seleção
/// espelhável, nenhum item.
/// </summary>
internal static class MirrorContextMenuBuilder
{
    public static List<Control> Build(CadViewModel vm, System.Action requestRender)
    {
        if (!vm.CanMirrorSelection) return new List<Control>();

        Control Mirror(string header, bool copy) => Submenu(header, new Control[]
        {
            Action(Strings.MirrorInX, () => vm.MirrorSelectionAtCenter(Vector3D.UnitX, copy), requestRender),
            Action(Strings.MirrorInY, () => vm.MirrorSelectionAtCenter(Vector3D.UnitY, copy), requestRender),
            Action(Strings.MirrorInZ, () => vm.MirrorSelectionAtCenter(Vector3D.UnitZ, copy), requestRender),
            new Separator(),
            Action(Strings.MirrorPickPlane, () => vm.StartMirrorTool(copy), requestRender),
        });

        return new List<Control>
        {
            Mirror(Strings.MirrorMenu, copy: false),
            Mirror(Strings.MirrorCopyMenu, copy: true),
        };
    }
}
