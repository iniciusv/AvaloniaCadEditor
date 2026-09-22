using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Infrastructure.Engines;

public class MathGeometryEngine : IGeometryEngine
{
    public Entity? HitTest(IEnumerable<Entity> entities, Point3D point, double tolerance)
    {
        foreach (var entity in entities.Reverse())
        {
            if (entity is LineEntity line)
            {
                if (DistancePointToLine(point, line) <= tolerance)
                    return entity;
            }
            else if (entity is CircleEntity circle)
            {
                var d = Math.Sqrt(Math.Pow(point.X - circle.Center.X, 2) + Math.Pow(point.Y - circle.Center.Y, 2));
                if (Math.Abs(d - circle.Radius) <= tolerance)
                    return entity;
            }
        }
        return null;
    }

    public IEnumerable<(Point3D Point, SnapPointType Type)> GetSnapPoints(Entity entity, Point3D? referencePoint = null)
    {
        var snaps = new List<(Point3D Point, SnapPointType Type)>();
        if (entity is LineEntity line)
        {
            snaps.Add((line.StartPoint, SnapPointType.Endpoint));
            snaps.Add((line.EndPoint, SnapPointType.Endpoint));
            snaps.Add((new Point3D((line.StartPoint.X + line.EndPoint.X) / 2, (line.StartPoint.Y + line.EndPoint.Y) / 2, 0), SnapPointType.Midpoint));
        }
        else if (entity is CircleEntity circle)
        {
            snaps.Add((circle.Center, SnapPointType.Center));
            snaps.Add((new Point3D(circle.Center.X + circle.Radius, circle.Center.Y, 0), SnapPointType.Endpoint)); // Quadrants treated as endpoints for now
            snaps.Add((new Point3D(circle.Center.X - circle.Radius, circle.Center.Y, 0), SnapPointType.Endpoint));
            snaps.Add((new Point3D(circle.Center.X, circle.Center.Y + circle.Radius, 0), SnapPointType.Endpoint));
            snaps.Add((new Point3D(circle.Center.X, circle.Center.Y - circle.Radius, 0), SnapPointType.Endpoint));

            if (referencePoint.HasValue)
            {
                var p = referencePoint.Value;
                var c = circle.Center;
                var r = circle.Radius;
                var d = Math.Sqrt(Math.Pow(p.X - c.X, 2) + Math.Pow(p.Y - c.Y, 2));

                if (d > r)
                {
                    var phi = Math.Atan2(p.Y - c.Y, p.X - c.X);
                    var beta = Math.Acos(r / d);

                    var t1 = new Point3D(c.X + r * Math.Cos(phi + beta), c.Y + r * Math.Sin(phi + beta), 0);
                    var t2 = new Point3D(c.X + r * Math.Cos(phi - beta), c.Y + r * Math.Sin(phi - beta), 0);

                    snaps.Add((t1, SnapPointType.Tangent));
                    snaps.Add((t2, SnapPointType.Tangent));
                }
            }
        }
        return snaps;
    }

    public IEnumerable<Point3D> GetIntersections(Entity a, Entity b)
    {
        var intersections = new List<Point3D>();
        if (a is LineEntity line1 && b is LineEntity line2)
        {
            var p = IntersectLines(line1.StartPoint, line1.EndPoint, line2.StartPoint, line2.EndPoint);
            if (p != null)
                intersections.Add(p.Value);
        }
        return intersections;
    }

    public IEnumerable<Entity> TrimEntity(Entity entity, IEnumerable<Point3D> intersections, Point3D clickPoint)
    {
        var intersectionList = intersections.ToList();
        if (intersectionList.Count == 0)
            return new List<Entity> { entity };

        if (entity is LineEntity line)
        {
            var pointsOnLine = new List<Point3D> { line.StartPoint, line.EndPoint };
            pointsOnLine.AddRange(intersectionList);

            pointsOnLine = pointsOnLine
                .Distinct(new Point3DComparer())
                .OrderBy(p => Math.Pow(p.X - line.StartPoint.X, 2) + Math.Pow(p.Y - line.StartPoint.Y, 2))
                .ToList();

            var newSegments = new List<Entity>();
            for (int i = 0; i < pointsOnLine.Count - 1; i++)
            {
                var p1 = pointsOnLine[i];
                var p2 = pointsOnLine[i + 1];

                var midPoint = new Point3D((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2, 0);
                var distToClick = Math.Sqrt(Math.Pow(clickPoint.X - midPoint.X, 2) + Math.Pow(clickPoint.Y - midPoint.Y, 2));

                if (distToClick > 0.01 && !IsPointOnSegment(clickPoint, p1, p2))
                {
                    newSegments.Add(new LineEntity(p1, p2));
                }
            }
            return newSegments;
        }
        return new List<Entity> { entity };
    }

    public IEnumerable<Entity> FilletEntities(Entity e1, Point3D click1, Entity e2, Point3D click2, double radius)
    {
        return new List<Entity> { e1, e2 };
    }

    public object Extrude(IEnumerable<Entity> entities, double thickness)
    {
        return new { Type = "Extrusion3D", Entities = entities, Thickness = thickness };
    }

    private double DistancePointToLine(Point3D p, LineEntity line)
    {
        double l2 = Math.Pow(line.StartPoint.X - line.EndPoint.X, 2) + Math.Pow(line.StartPoint.Y - line.EndPoint.Y, 2);
        if (l2 == 0.0) return Math.Sqrt(Math.Pow(p.X - line.StartPoint.X, 2) + Math.Pow(p.Y - line.StartPoint.Y, 2));

        double t = Math.Max(0, Math.Min(1, ((p.X - line.StartPoint.X) * (line.EndPoint.X - line.StartPoint.X) + (p.Y - line.StartPoint.Y) * (line.EndPoint.Y - line.StartPoint.Y)) / l2));
        var projection = new Point3D(line.StartPoint.X + t * (line.EndPoint.X - line.StartPoint.X), line.StartPoint.Y + t * (line.EndPoint.Y - line.StartPoint.Y), 0);

        return Math.Sqrt(Math.Pow(p.X - projection.X, 2) + Math.Pow(p.Y - projection.Y, 2));
    }

    private Point3D? IntersectLines(Point3D p1, Point3D p2, Point3D p3, Point3D p4)
    {
        double denominator = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
        if (Math.Abs(denominator) < 1e-10) return null; // Paralelas

        double t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / denominator;
        double u = ((p1.X - p3.X) * (p1.Y - p2.Y) - (p1.Y - p3.Y) * (p1.X - p2.X)) / denominator;

        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
        {
            return new Point3D(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y), 0);
        }

        return null;
    }

    private bool IsPointOnSegment(Point3D p, Point3D a, Point3D b)
    {
        var distAP = Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));
        var distPB = Math.Sqrt(Math.Pow(b.X - p.X, 2) + Math.Pow(b.Y - p.Y, 2));
        var distAB = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

        return Math.Abs((distAP + distPB) - distAB) < 0.1;
    }

    private class Point3DComparer : IEqualityComparer<Point3D>
    {
        public bool Equals(Point3D x, Point3D y)
        {
            return Math.Abs(x.X - y.X) < 1e-5 && Math.Abs(x.Y - y.Y) < 1e-5;
        }

        public int GetHashCode(Point3D obj)
        {
            return Math.Round(obj.X, 4).GetHashCode() ^ Math.Round(obj.Y, 4).GetHashCode();
        }
    }
}
