using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public abstract class FeatureBase : IFeature
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "Feature";
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public bool IsSelected { get; set; } = false;
    public bool IsTransientTool { get; set; } = false;

    // By default, features might not have dependencies unless overridden
    public virtual IEnumerable<IFeature> Dependencies => Array.Empty<IFeature>();

    public virtual void MarkDirty()
    {
        // In a complete implementation, this would trigger an event or 
        // notify the SceneGraph/Document that this node needs recompilation.
    }

    public virtual FaceReference FindFace(int faceId, Vector3D normalHint, string? name = null)
    {
        return new FaceReference(this, faceId, normalHint, name ?? $"face_{faceId}");
    }

    public virtual IReadOnlyList<Point3D> GetVertices()
    {
        return Array.Empty<Point3D>();
    }

    public virtual Point3D GetVertex(int index)
    {
        var verts = GetVertices();
        if (index >= 0 && index < verts.Count)
            return verts[index];
        return Point3D.Origin;
    }

    public virtual VertexReference FindVertex(int vertexIndex, string? name = null)
    {
        return new VertexReference(this, vertexIndex, name ?? $"vert_{vertexIndex}");
    }
}
