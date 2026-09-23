using System;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public class WorkPlaneFeature : FeatureBase
{
    public bool IsBasePlane { get; set; } = false;

    public WorkPlane Plane { get; set; }
    
    public WorkPlaneFeature(WorkPlane plane, string name, bool isBasePlane = false)
    {
        Plane = plane;
        Name = name;
        IsBasePlane = isBasePlane;
    }
}
