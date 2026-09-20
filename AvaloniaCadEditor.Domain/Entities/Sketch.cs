using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Domain.Entities;

public class Sketch
{
    private readonly List<Entity> _entities = new();
    private readonly List<IProfile> _profiles = new();

    public IReadOnlyList<Entity> Entities => _entities.AsReadOnly();
    public IReadOnlyList<IProfile> Profiles => _profiles.AsReadOnly();

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
        _profiles.Clear();
    }

    public void SetProfiles(IEnumerable<IProfile> profiles)
    {
        _profiles.Clear();
        _profiles.AddRange(profiles);
    }

    public void AddProfile(IProfile profile)
    {
        _profiles.Add(profile);
    }

    public void RemoveProfile(IProfile profile)
    {
        _profiles.Remove(profile);
    }
}
