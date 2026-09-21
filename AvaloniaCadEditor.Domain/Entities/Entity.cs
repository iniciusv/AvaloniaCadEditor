using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Entities;

public abstract class Entity
{
    public Guid Id { get; } = Guid.NewGuid();
    
    public bool IsSelected { get; set; } = false;
    public bool IsVisible { get; set; } = true;
    public WorkPlane Plane { get; set; } = WorkPlane.XY;

    public abstract void Translate(Vector3D translation);
    
    public abstract Entity Clone();
}
