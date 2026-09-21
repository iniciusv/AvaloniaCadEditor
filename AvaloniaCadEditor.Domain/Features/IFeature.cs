using System;
using System.Collections.Generic;

namespace AvaloniaCadEditor.Domain.Features;

public interface IFeature
{
    Guid Id { get; }
    string Name { get; set; }
    bool IsEnabled { get; set; }
    bool IsVisible { get; set; }
    bool IsSelected { get; set; }

    /// <summary>
    /// Gets the features that this feature depends on.
    /// This forms the Directed Acyclic Graph (DAG) for the CSG.
    /// </summary>
    IEnumerable<IFeature> Dependencies { get; }

    /// <summary>
    /// Marks this feature and its dependents as dirty, requiring a rebuild of the geometry.
    /// </summary>
    void MarkDirty();
}
