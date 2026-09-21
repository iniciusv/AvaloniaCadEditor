using System;
using System.Collections.Generic;

namespace AvaloniaCadEditor.Domain.Features;

public abstract class FeatureBase : IFeature
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "Feature";
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public bool IsSelected { get; set; } = false;

    // By default, features might not have dependencies unless overridden
    public virtual IEnumerable<IFeature> Dependencies => Array.Empty<IFeature>();

    public virtual void MarkDirty()
    {
        // In a complete implementation, this would trigger an event or 
        // notify the SceneGraph/Document that this node needs recompilation.
    }
}
