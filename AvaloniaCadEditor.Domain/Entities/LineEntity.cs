using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Entities;

public class LineEntity : Entity
{
    public Point3D StartPoint { get; set; }
    public Point3D EndPoint { get; set; }

    public LineEntity(Point3D startPoint, Point3D endPoint)
    {
        StartPoint = startPoint;
        EndPoint = endPoint;
    }

    public double Length => StartPoint.DistanceTo(EndPoint);
}
