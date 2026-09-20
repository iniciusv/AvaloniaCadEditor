using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class SelectTool : ICommandTool
{
    public string Name => "Select";

    public void OnMouseDown(Sketch sketch, Point3D point) { }

    public void OnMouseMove(Sketch sketch, Point3D point) { }

    public void Cancel() { }

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();

    public void OnKeyDown(CadKey key) { }
}
