using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Entities;

public abstract class Entity
{
    public Guid Id { get; } = Guid.NewGuid();
    
    public bool IsSelected { get; set; } = false;

    public abstract void Translate(Vector3D translation);
    
    public abstract Entity Clone();
}
