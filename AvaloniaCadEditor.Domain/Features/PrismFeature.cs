using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public class PrismFeature : FeatureBase
{
    public IReadOnlyList<Point3D> Vertices { get; }
    public double Height { get; set; }
    public WorkPlane Plane { get; set; }

    public PrismFeature(IEnumerable<Point3D> vertices, double height, WorkPlane? plane = null, string? name = null)
    {
        Vertices = vertices.ToList().AsReadOnly();
        Height = height;
        Plane = plane ?? WorkPlane.XY;
        Name = name ?? $"Prism ({Height:F1}mm)";
    }

    public override IReadOnlyList<Point3D> GetVertices()
    {
        var result = new List<Point3D>();
        var up = Plane.Normal * Height;

        // Base vertices in world space
        foreach (var v in Vertices)
        {
            result.Add(Plane.ToWorldSpace(v));
        }

        // Top vertices in world space
        foreach (var v in Vertices)
        {
            result.Add(Plane.ToWorldSpace(v) + up);
        }

        return result.AsReadOnly();
    }
}
