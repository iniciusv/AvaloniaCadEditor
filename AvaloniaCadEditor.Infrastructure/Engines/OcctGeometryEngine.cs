using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;
using Occt;

namespace AvaloniaCadEditor.Infrastructure.Engines;

public class OcctGeometryEngine : IGeometryEngine
{
    public IEnumerable<Point3D> GetIntersections(Entity a, Entity b)
    {
        var curveA = CreateOcctCurve(a);
        var curveB = CreateOcctCurve(b);

        if (curveA == null || curveB == null) return Enumerable.Empty<Point3D>();

        var intersector = new Geom2dAPI_InterCurveCurve(curveA, curveB, 1e-6);
        var points = new List<Point3D>();

        for (int i = 1; i <= intersector.NbPoints; i++)
        {
            var pnt = intersector.Point(i);
            points.Add(new Point3D(pnt.X, pnt.Y, 0));
        }

        return points;
    }

    public IEnumerable<Point3D> GetSnapPoints(Entity entity)
    {
        if (entity is LineEntity line)
        {
            yield return line.StartPoint;
            yield return line.EndPoint;
            yield return new Point3D((line.StartPoint.X + line.EndPoint.X) / 2, (line.StartPoint.Y + line.EndPoint.Y) / 2);
        }
        else if (entity is CircleEntity circle)
        {
            yield return circle.Center;
        }
    }

    public Entity? HitTest(IEnumerable<Entity> entities, Point3D point, double tolerance)
    {
        // Simplification for now, we just measure distance
        foreach (var entity in entities)
        {
            if (entity is LineEntity line)
            {
                // distance from point to line segment
                var p1 = new gp_Pnt2d(line.StartPoint.X, line.StartPoint.Y);
                var p2 = new gp_Pnt2d(line.EndPoint.X, line.EndPoint.Y);
                var gpLine = new gp_Lin2d(p1, new gp_Dir2d(new gp_Vec2d(p1, p2)));
                
                var dist = gpLine.Distance(new gp_Pnt2d(point.X, point.Y));
                if (dist < tolerance) return line;
            }
            else if (entity is CircleEntity circle)
            {
                var dist = Math.Abs(circle.Center.DistanceTo(point) - circle.Radius);
                if (dist < tolerance) return circle;
            }
        }
        return null;
    }

    public IEnumerable<Entity> TrimEntity(Entity entity, IEnumerable<Point3D> intersections, Point3D clickPoint)
    {
        // Real implementation using OCCT BRep topology or algebraic splitting
        // For simplicity, we just split the line mathematically for now, as OCCT BRep is heavy
        if (entity is LineEntity line)
        {
            if (!intersections.Any()) yield break;
            var closest = intersections.OrderBy(i => i.DistanceTo(clickPoint)).First();

            // Split into two
            var distStart = line.StartPoint.DistanceTo(clickPoint);
            var distEnd = line.EndPoint.DistanceTo(clickPoint);

            // Return the half that was NOT clicked
            if (distStart < distEnd)
            {
                yield return new LineEntity(closest, line.EndPoint);
            }
            else
            {
                yield return new LineEntity(line.StartPoint, closest);
            }
        }
        else
        {
            yield return entity;
        }
    }

    public IEnumerable<Entity> FilletEntities(Entity e1, Point3D click1, Entity e2, Point3D click2, double radius)
    {
        // Real implementation uses ChFi2d_AnaFilletAlgo
        // Or BRepFilletAPI
        return Enumerable.Empty<Entity>();
    }

    public object Extrude(IEnumerable<Entity> entities, double thickness)
    {
        var wireBuilder = new BRepBuilderAPI_MakeWire();
        
        foreach (var entity in entities)
        {
            var curve = CreateOcctCurve(entity);
            if (curve == null) continue;

            // In OCCT, Geom2d curves need to be mapped to 3D space to create 3D Edges
            var plane = new gp_Pln(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1));
            var curve3d = GeomAPI.To3d(curve, plane);
            
            var edge = new BRepBuilderAPI_MakeEdge(curve3d).Edge;
            wireBuilder.Add(edge);
        }

        if (!wireBuilder.IsDone) return new object();

        var face = new BRepBuilderAPI_MakeFace(wireBuilder.Wire).Face;
        // Construct prism
        var prism = new BRepPrimAPI_MakePrism(face, new gp_Vec(0, 0, thickness)).Shape;

        return prism;
    }

    private Geom2d_Curve? CreateOcctCurve(Entity entity)
    {
        if (entity is LineEntity line)
        {
            var p1 = new gp_Pnt2d(line.StartPoint.X, line.StartPoint.Y);
            var p2 = new gp_Pnt2d(line.EndPoint.X, line.EndPoint.Y);
            
            // Avoid creating line with 0 length
            if (p1.Distance(p2) < 1e-6) return null;
            
            return new Geom2d_TrimmedCurve(new Geom2d_Line(p1, new gp_Dir2d(new gp_Vec2d(p1, p2))), 0, p1.Distance(p2));
        }
        else if (entity is CircleEntity circle)
        {
            var ax2d = new gp_Ax2d(new gp_Pnt2d(circle.Center.X, circle.Center.Y), new gp_Dir2d(1, 0));
            return new Geom2d_Circle(ax2d, circle.Radius);
        }

        return null;
    }
}
