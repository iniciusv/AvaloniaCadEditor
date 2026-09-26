using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Domain.ValueObjects;

public class EdgeReference
{
    public IFeature ParentFeature { get; }
    public string Name { get; set; }
    public EdgeType EdgeId { get; }

    public EdgeReference(IFeature parent, EdgeType edgeId, string name = "")
    {
        ParentFeature = parent;
        EdgeId = edgeId;
        Name = name;
    }

    public override bool Equals(object? obj)
    {
        if (obj is EdgeReference other)
            return ReferenceEquals(ParentFeature, other.ParentFeature) && EdgeId == other.EdgeId;
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ParentFeature, EdgeId);
    }
}
