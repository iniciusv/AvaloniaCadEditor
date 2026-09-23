using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class SetWorkPlaneCommand : ICommandTool
{
    public event EventHandler<string>? CodeGenerated;
    public string Name => "Set WorkPlane";
    public string GetStatusText() => "Click on a 3D face to set WorkPlane";
    public string GetMeasurementText() => "";

    public System.Collections.Generic.IEnumerable<Entity> GetPreviewEntities() => System.Array.Empty<Entity>();

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        // This is not used because SilkViewportControl intercepts the raycast for SetWorkPlaneCommand.
        // It directly sets ViewModel.ActiveWorkPlane and changes the tool back to SelectTool.
    }

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
    }

    public void OnMouseUp(Sketch sketch, Point3D point)
    {
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
    }

    public void OnValueEntered(Sketch sketch, double value)
    {
    }

    public void Cancel()
    {
    }
}


