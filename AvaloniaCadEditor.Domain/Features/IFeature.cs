using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public interface IFeature
{
    Guid Id { get; }
    string Name { get; set; }
    bool IsEnabled { get; set; }
    bool IsVisible { get; set; }
    bool IsSelected { get; set; }
    bool IsTransientTool { get; set; }

    /// <summary>
    /// Gets the features that this feature depends on.
    /// This forms the Directed Acyclic Graph (DAG) for the CSG.
    /// </summary>
    IEnumerable<IFeature> Dependencies { get; }

    /// <summary>
    /// Marks this feature and its dependents as dirty, requiring a rebuild of the geometry.
    /// </summary>
    void MarkDirty();

    /// <summary>
    /// Finds a face reference on this feature.
    /// </summary>
    FaceReference FindFace(int faceId, Vector3D normalHint, string? name = null);

    /// <summary>
    /// Gets all key geometric vertices of this feature in world space.
    /// </summary>
    IReadOnlyList<Point3D> GetVertices();

    /// <summary>
    /// Gets a specific vertex in world space by its index.
    /// </summary>
    Point3D GetVertex(int index);

    /// <summary>
    /// Finds a vertex reference on this feature.
    /// </summary>
    VertexReference FindVertex(int vertexIndex, string? name = null);
}
