using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Domain.Entities;

public class Sketch
{
    private readonly List<WorkPlaneFeature> _workPlanes = new();
    private readonly List<IFeature> _features = new();
    private readonly List<Entity> _entities = new();
    private readonly List<IProfile> _profiles = new();

    public ValueObjects.WorkPlane Plane { get; set; }

    public Sketch()
    {
        Plane = ValueObjects.WorkPlane.XY;
        _workPlanes.Add(new WorkPlaneFeature(Plane, "Plano Base (XY)", true));
    }

    public Sketch(ValueObjects.WorkPlane plane)
    {
        Plane = plane;
        _workPlanes.Add(new WorkPlaneFeature(Plane, "Plano Base", true));
    }

    public IReadOnlyList<WorkPlaneFeature> WorkPlanes => _workPlanes.AsReadOnly();
    public IReadOnlyList<IFeature> Features => _features.AsReadOnly();
    
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

    public void AddWorkPlane(WorkPlaneFeature wp)
    {
        _workPlanes.Add(wp);
    }

    public void RemoveWorkPlane(WorkPlaneFeature wp)
    {
        _workPlanes.Remove(wp);
    }

    public void AddFeature(IFeature feature)
    {
        if (feature is WorkPlaneFeature wpf)
        {
            AddWorkPlane(wpf);
            return;
        }
        _features.Add(feature);
    }

    public void RemoveFeature(IFeature feature)
    {
        if (feature is WorkPlaneFeature wpf)
        {
            RemoveWorkPlane(wpf);
            return;
        }
        _features.Remove(feature);
    }

    public void ClearFeatures()
    {
        _features.Clear();
    }

    public void Clear()
    {
        _entities.Clear();
        _profiles.Clear();
        _workPlanes.Clear();
        _features.Clear();
        _workPlanes.Add(new WorkPlaneFeature(Plane, "Plano Base (XY)", true));
    }
}
