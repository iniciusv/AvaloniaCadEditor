namespace AvaloniaCadEditor.Domain.Entities;

public class Sketch
{
    private readonly List<Entity> _entities = new();

    public IReadOnlyList<Entity> Entities => _entities.AsReadOnly();

    public void AddEntity(Entity entity)
    {
        _entities.Add(entity);
    }

    public void RemoveEntity(Entity entity)
    {
        _entities.Remove(entity);
    }
    
    public void Clear()
    {
        _entities.Clear();
    }
}
