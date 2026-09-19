namespace AvaloniaCadEditor.Domain.Entities;

public abstract class Entity
{
    public Guid Id { get; } = Guid.NewGuid();
    
    // Color, Layer, etc could go here.
}
