using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Entities;

public class CircleEntity : Entity
{
    public Point3D Center { get; set; }
    public double Radius { get; set; }

    public CircleEntity(Point3D center, double radius)
    {
        Center = center;
        Radius = radius;
    }

    public override void Translate(Vector3D translation)
    {
        Center = Center + translation;
    }

    public override Entity Clone()
    {
        return new CircleEntity(Center, Radius)
        {
            IsSelected = this.IsSelected
        };
    }
}
