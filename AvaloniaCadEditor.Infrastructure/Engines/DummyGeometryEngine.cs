using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Infrastructure.Engines;

public class DummyGeometryEngine : IGeometryEngine
{
    public IEnumerable<Point3D> GetIntersections(Entity a, Entity b)
    {
        // Dummy: returning empty for now
        return Enumerable.Empty<Point3D>();
    }

    public IEnumerable<Point3D> GetSnapPoints(Entity entity)
    {
        if (entity is LineEntity line)
        {
            // Endpoints
            yield return line.StartPoint;
            yield return line.EndPoint;
            // Midpoint
            yield return new Point3D((line.StartPoint.X + line.EndPoint.X) / 2, (line.StartPoint.Y + line.EndPoint.Y) / 2);
        }
        else if (entity is CircleEntity circle)
        {
            // Center
            yield return circle.Center;
        }
    }

    public Entity? HitTest(IEnumerable<Entity> entities, Point3D point, double tolerance)
    {
        // Simple bounding box or distance check
        foreach (var entity in entities)
        {
            if (entity is LineEntity line)
            {
                // Dummy hit test
                if (line.StartPoint.DistanceTo(point) < tolerance || line.EndPoint.DistanceTo(point) < tolerance)
                    return line;
            }
        }
        return null;
    }

    public IEnumerable<Entity> TrimEntity(Entity entity, IEnumerable<Point3D> intersections, Point3D clickPoint)
    {
        // Dummy: doesn't actually trim
        yield return entity;
    }

    public IEnumerable<Entity> FilletEntities(Entity e1, Point3D click1, Entity e2, Point3D click2, double radius)
    {
        // Dummy
        return Enumerable.Empty<Entity>();
    }
}
