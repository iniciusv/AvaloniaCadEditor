using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Domain.Entities;

public class Sketch
{
    private readonly List<WorkPlaneFeature> _workPlanes = new();
    private readonly List<IFeature> _features = new();

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
    
    // Compatibility wrappers for the active sketch (assuming first WorkPlane for now)
    public IReadOnlyList<Entity> Entities => _workPlanes.Count > 0 ? _workPlanes[0].Sketch.Entities : System.Array.Empty<Entity>();
    public IReadOnlyList<IProfile> Profiles => _workPlanes.Count > 0 ? _workPlanes[0].Sketch.Profiles : System.Array.Empty<IProfile>();

    public void AddEntity(Entity entity)
    {
        if (_workPlanes.Count > 0) _workPlanes[0].Sketch.AddEntity(entity);
    }

    public void RemoveEntity(Entity entity)
    {
        if (_workPlanes.Count > 0) _workPlanes[0].Sketch.RemoveEntity(entity);
    }

    public void SetProfiles(IEnumerable<IProfile> profiles)
    {
        if (_workPlanes.Count > 0) _workPlanes[0].Sketch.SetProfiles(profiles);
    }

    public void AddProfile(IProfile profile)
    {
        if (_workPlanes.Count > 0) _workPlanes[0].Sketch.AddProfile(profile);
    }

    public void RemoveProfile(IProfile profile)
    {
        if (_workPlanes.Count > 0) _workPlanes[0].Sketch.RemoveProfile(profile);
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
        if (_workPlanes.Count > 0) _workPlanes[0].Sketch.Clear();
        _workPlanes.Clear();
        _features.Clear();
        _workPlanes.Add(new WorkPlaneFeature(Plane, "Plano Base (XY)", true));
    }
}
