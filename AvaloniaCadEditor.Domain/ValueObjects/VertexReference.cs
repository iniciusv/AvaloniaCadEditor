using System;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Domain.ValueObjects;

public class VertexReference
{
    public IFeature ParentFeature { get; }
    public int VertexIndex { get; }
    public string Name { get; set; }

    public VertexReference(IFeature parent, int vertexIndex, string name)
    {
        ParentFeature = parent ?? throw new ArgumentNullException(nameof(parent));
        VertexIndex = vertexIndex;
        Name = name;
    }

    public Point3D GetPosition()
    {
        return ParentFeature.GetVertex(VertexIndex);
    }
}
