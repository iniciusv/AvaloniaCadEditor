using System;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public class WorkPlaneFeature : IFeature
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "WorkPlane";
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public bool IsSelected { get; set; } = false;
    public bool HasError => false;
    public string? ErrorMessage => null;
    public bool IsBasePlane { get; set; } = false;

    public WorkPlane Plane { get; set; }

    public WorkPlaneFeature(WorkPlane plane, string name, bool isBasePlane = false)
    {
        Plane = plane;
        Name = name;
        IsBasePlane = isBasePlane;
    }
}
