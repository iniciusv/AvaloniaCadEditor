using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Domain.Entities;

public class Sketch
{
    private readonly List<Entity> _entities = new();
    private readonly List<IProfile> _profiles = new();
    private readonly List<IFeature> _features = new();

    public ValueObjects.WorkPlane Plane { get; set; }

    public Sketch()
    {
        Plane = ValueObjects.WorkPlane.XY;
        _features.Add(new WorkPlaneFeature(Plane, "Plano Base (XY)", true));
    }

    public Sketch(ValueObjects.WorkPlane plane)
    {
        Plane = plane;
    }

    public IReadOnlyList<Entity> Entities => _entities.AsReadOnly();
    public IReadOnlyList<IProfile> Profiles => _profiles.AsReadOnly();
    public IReadOnlyList<IFeature> Features => _features.AsReadOnly();

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
        _features.Clear();
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

    public event Action? FeaturesChanged;

    public void AddFeature(IFeature feature)
    {
        _features.Add(feature);
        FeaturesChanged?.Invoke();
    }

    public void RemoveFeature(IFeature feature)
    {
        _features.Remove(feature);
        FeaturesChanged?.Invoke();
    }

    public void InsertFeatureAt(int index, IFeature feature)
    {
        _features.Insert(index, feature);
        FeaturesChanged?.Invoke();
    }

    public void ReorderFeature(IFeature feature, int newIndex)
    {
        _features.Remove(feature);
        _features.Insert(Math.Min(newIndex, _features.Count), feature);
        FeaturesChanged?.Invoke();
    }
}
