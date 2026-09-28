using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Infrastructure.Engines;

public class DummyGeometryEngine : IGeometryEngine
{
    public IEnumerable<Point3D> GetIntersections(Entity a, Entity b)
    {
        // Dummy: returning empty for now
        return Enumerable.Empty<Point3D>();
    }

    public IEnumerable<(Point3D Point, SnapPointType Type)> GetSnapPoints(Entity entity, Point3D? referencePoint = null)
    {
        if (entity is LineEntity line)
        {
            // Endpoints
            yield return (line.StartPoint, SnapPointType.Endpoint);
            yield return (line.EndPoint, SnapPointType.Endpoint);
            // Midpoint
            yield return (new Point3D((line.StartPoint.X + line.EndPoint.X) / 2, (line.StartPoint.Y + line.EndPoint.Y) / 2), SnapPointType.Midpoint);
        }
        else if (entity is CircleEntity circle)
        {
            // Center
            yield return (circle.Center, SnapPointType.Center);
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

    public object Extrude(IEnumerable<Entity> entities, double thickness)
    {
        // Dummy
        return new object();
    }

    public Entity? ExtendEntity(Entity entity, IEnumerable<Entity> boundaries, Point3D clickPoint)
    {
        // Dummy: não estende
        return null;
    }

    public Point3D? GetApparentIntersection(Entity a, Entity b)
    {
        // Dummy: sem interseção
        return null;
    }

    public IEnumerable<Entity> OffsetProfile(IProfile profile, double distance)
    {
        // Dummy: sem offset
        return Enumerable.Empty<Entity>();
    }
}
