using System.Collections.Generic;
using Avalonia.Controls;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.Resources;
using AvaloniaCadEditor.UI.ViewModels;
using static AvaloniaCadEditor.UI.Controls.Input.ContextMenus.ViewportMenuItems;

namespace AvaloniaCadEditor.UI.Controls.Input.ContextMenus;

/// <summary>
/// Menu do botão direito no esboço: projetar no plano a face ou o sólido sob o cursor (ou tirar a projeção),
/// limpar as projeções do plano e desligar por enquanto os chanfros/fillets.
/// </summary>
internal static class SketchContextMenuBuilder
{
    public static List<Control> Build(CadViewModel vm, RaycastHit? hit, System.Action requestRender)
    {
        var items = new List<Control>();

        if (hit is { } target)
        {
            items.Add(Submenu(Strings.ProjectMenu, new Control[]
            {
                Action(Strings.ProjectFace, () => vm.Project(target, ProjectionTarget.Face), requestRender),
                Action(Strings.ProjectBody, () => vm.Project(target, ProjectionTarget.Body), requestRender),
            }));
            if (vm.IsProjected(target, ProjectionTarget.Face))
                items.Add(Action(Strings.RemoveProjectedFace, () => vm.RemoveProjection(target, ProjectionTarget.Face), requestRender));
            if (vm.IsProjected(target, ProjectionTarget.Body))
                items.Add(Action(Strings.RemoveProjectedBody, () => vm.RemoveProjection(target, ProjectionTarget.Body), requestRender));
        }

        if (vm.HasProjections)
            items.Add(Action(Strings.ClearProjections, vm.ClearProjections, requestRender));

        if (items.Count > 0) items.Add(new Separator());
        items.Add(Toggle(Strings.SuppressFilletsTemporarily, vm.IsFilletSuppressed, on => vm.IsFilletSuppressed = on, requestRender));
        return items;
    }
}
