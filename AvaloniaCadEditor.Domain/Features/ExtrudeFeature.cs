using System;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public class ExtrudeFeature : IFeature
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public bool IsEnabled { get; set; } = true;

    public IProfile Profile { get; set; }
    public double Depth { get; set; }
    public Vector3D Direction { get; set; }

    public ExtrudeFeature(IProfile profile, double depth, Vector3D? direction = null, string? name = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Depth = depth;
        Direction = direction ?? profile.Normal * depth;
        Name = name ?? $"Extrusão ({profile.Classification})";
    }
}
