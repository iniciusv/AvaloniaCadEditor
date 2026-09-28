using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Profiles;
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

    public Entity? ExtendEntity(Entity entity, IEnumerable<Entity> boundaries, Point3D clickPoint)
    {
        if (entity is not LineEntity line) return null;

        // Escolhe a ponta mais próxima do clique como a extremidade "móvel".
        bool extendingStart = clickPoint.DistanceTo(line.StartPoint) < clickPoint.DistanceTo(line.EndPoint);
        var origin = extendingStart ? line.StartPoint : line.EndPoint;
        var anchor = extendingStart ? line.EndPoint : line.StartPoint;

        var direction = (origin - anchor);
        if (direction.Length < 1e-9) return null;
        direction = direction.Normalize();

        double bestT = double.PositiveInfinity;
        Point3D? bestPoint = null;

        foreach (var boundary in boundaries)
        {
            if (boundary == entity) continue;

            if (boundary is LineEntity otherLine)
            {
                var hit = RayLineIntersection(origin, direction, otherLine.StartPoint, otherLine.EndPoint);
                if (hit == null) continue;

                var (rayT, u) = hit.Value;
                if (rayT > 1e-6 && u >= -1e-6 && u <= 1 + 1e-6 && rayT < bestT)
                {
                    bestT = rayT;
                    bestPoint = new Point3D(origin.X + direction.X * rayT, origin.Y + direction.Y * rayT, 0);
                }
            }
            else if (boundary is CircleEntity circle)
            {
                foreach (var t in RayCircleIntersections(origin, direction, circle))
                {
                    if (t > 1e-6 && t < bestT)
                    {
                        bestT = t;
                        bestPoint = new Point3D(origin.X + direction.X * t, origin.Y + direction.Y * t, 0);
                    }
                }
            }
        }

        if (bestPoint == null) return null;

        var newLine = extendingStart
            ? new LineEntity(bestPoint.Value, anchor)
            : new LineEntity(anchor, bestPoint.Value);
        newLine.Plane = line.Plane;
        return newLine;
    }

    public Point3D? GetApparentIntersection(Entity a, Entity b)
    {
        if (a is LineEntity lineA && b is LineEntity lineB)
        {
            var (point, _, _) = IntersectInfiniteLines(lineA.StartPoint, lineA.EndPoint, lineB.StartPoint, lineB.EndPoint);
            return point;
        }
        return null;
    }

    public IEnumerable<Entity> OffsetProfile(IProfile profile, double distance)
    {
        if (profile is CircleProfile circleProfile)
        {
            double newRadius = circleProfile.Radius + distance;
            if (newRadius <= 1e-6) return Enumerable.Empty<Entity>();

            var circle = new CircleEntity(circleProfile.Center, newRadius) { Plane = circleProfile.Plane };
            return new List<Entity> { circle };
        }

        // Polígono genérico (Polygon, Rectangle, etc.): usa os vértices locais na ordem do perfil.
        var localVerts = profile.GetVertices().Select(profile.Plane.ToLocalSpace).ToList();
        if (localVerts.Count < 3) return Enumerable.Empty<Entity>();

        double signedArea = SignedArea(localVerts);
        if (Math.Abs(signedArea) < 1e-9) return Enumerable.Empty<Entity>();

        // Sentido anti-horário (área com sinal positiva) => normal externa de uma aresta (p1->p2) aponta para a direita da aresta.
        double orientation = signedArea > 0 ? 1.0 : -1.0;
        int n = localVerts.Count;

        var offsetLinesStart = new Point3D[n];
        var offsetLinesEnd = new Point3D[n];

        for (int i = 0; i < n; i++)
        {
            var p1 = localVerts[i];
            var p2 = localVerts[(i + 1) % n];
            var edge = (p2 - p1);
            if (edge.Length < 1e-9) return Enumerable.Empty<Entity>();
            edge = edge.Normalize();

            // Normal externa da aresta (rotaciona -90° para CCW): (edge.Y, -edge.X)
            var normal = new Vector3D(edge.Y * orientation, -edge.X * orientation, 0);

            offsetLinesStart[i] = p1 + normal * distance;
            offsetLinesEnd[i] = p2 + normal * distance;
        }

        var newVertices = new List<Point3D>(n);
        for (int i = 0; i < n; i++)
        {
            int prev = (i - 1 + n) % n;
            var (point, _, _) = IntersectInfiniteLines(offsetLinesStart[prev], offsetLinesEnd[prev], offsetLinesStart[i], offsetLinesEnd[i]);
            newVertices.Add(point ?? offsetLinesStart[i]);
        }

        // Se alguma aresta "virou do avesso" (o offset interno ultrapassou o centro da região),
        // a direção da aresta resultante fica oposta à da aresta original. Checar isso aresta a
        // aresta é mais robusto do que comparar apenas o sinal da área total: num retângulo, por
        // exemplo, uma contração maior que a metade de AMBAS as dimensões inverte os dois eixos
        // simultaneamente e a área com sinal "se corrige" sozinha, escondendo o problema.
        for (int i = 0; i < n; i++)
        {
            var originalEdge = localVerts[(i + 1) % n] - localVerts[i];
            var newEdge = newVertices[(i + 1) % n] - newVertices[i];
            if (originalEdge.DotProduct(newEdge) <= 0)
            {
                return Enumerable.Empty<Entity>();
            }
        }

        // LineEntity armazena pontos em espaço local do plano do sketch (mesma convenção usada
        // ao construir entidades a partir de cliques do mouse), então newVertices (já locais) é usado direto.
        var result = new List<Entity>(n);
        for (int i = 0; i < n; i++)
        {
            var p1 = newVertices[i];
            var p2 = newVertices[(i + 1) % n];
            result.Add(new LineEntity(p1, p2) { Plane = profile.Plane });
        }
        return result;
    }

    private static double SignedArea(IReadOnlyList<Point3D> verts)
    {
        double area = 0;
        for (int i = 0; i < verts.Count; i++)
        {
            var p1 = verts[i];
            var p2 = verts[(i + 1) % verts.Count];
            area += p1.X * p2.Y - p2.X * p1.Y;
        }
        return area / 2.0;
    }

    /// <summary>
    /// Resolve origin + t*direction = segStart + u*(segEnd-segStart) para t (ao longo do raio)
    /// e u (ao longo do segmento). Retorna null se raio e segmento forem paralelos.
    /// </summary>
    private (double t, double u)? RayLineIntersection(Point3D origin, Vector3D direction, Point3D segStart, Point3D segEnd)
    {
        double segDx = segEnd.X - segStart.X;
        double segDy = segEnd.Y - segStart.Y;

        double det = segDx * direction.Y - segDy * direction.X;
        if (Math.Abs(det) < 1e-10) return null; // Paralelas

        double dx0 = segStart.X - origin.X;
        double dy0 = segStart.Y - origin.Y;

        double t = (segDx * dy0 - segDy * dx0) / det;
        double u = (direction.X * dy0 - direction.Y * dx0) / det;

        return (t, u);
    }

    private IEnumerable<double> RayCircleIntersections(Point3D origin, Vector3D direction, CircleEntity circle)
    {
        double ox = origin.X - circle.Center.X;
        double oy = origin.Y - circle.Center.Y;

        double a = direction.X * direction.X + direction.Y * direction.Y;
        double b = 2 * (ox * direction.X + oy * direction.Y);
        double c = ox * ox + oy * oy - circle.Radius * circle.Radius;

        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0 || a < 1e-12) yield break;

        double sqrtDisc = Math.Sqrt(discriminant);
        double t1 = (-b - sqrtDisc) / (2 * a);
        double t2 = (-b + sqrtDisc) / (2 * a);

        yield return t1;
        yield return t2;
    }

    private double DistancePointToLine(Point3D p, LineEntity line)
    {
        var v = line.EndPoint - line.StartPoint;
        var w = p - line.StartPoint;
        
        double l2 = v.X * v.X + v.Y * v.Y;
        if (l2 < 1e-10) return Math.Sqrt(w.X * w.X + w.Y * w.Y); // Ponto até o início

        double t = (w.X * v.X + w.Y * v.Y) / l2;
        t = Math.Max(0, Math.Min(1, t));
        
        var proj = new Point3D(line.StartPoint.X + t * v.X, line.StartPoint.Y + t * v.Y, 0);
        var distVec = p - proj;
        
        return Math.Sqrt(distVec.X * distVec.X + distVec.Y * distVec.Y);
    }

    private Point3D? IntersectLines(Point3D p1, Point3D p2, Point3D p3, Point3D p4)
    {
        var (point, t, u) = IntersectInfiniteLines(p1, p2, p3, p4);
        if (point == null) return null;

        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
        {
            return point;
        }

        return null;
    }

    /// <summary>
    /// Interseção das retas infinitas suportadas por p1-p2 e p3-p4, junto com os parâmetros
    /// t (ao longo de p1-p2) e u (ao longo de p3-p4). point é null se as retas forem paralelas.
    /// </summary>
    private (Point3D? point, double t, double u) IntersectInfiniteLines(Point3D p1, Point3D p2, Point3D p3, Point3D p4)
    {
        double denominator = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
        if (Math.Abs(denominator) < 1e-10) return (null, 0, 0); // Paralelas

        double t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / denominator;
        double u = ((p1.X - p3.X) * (p1.Y - p2.Y) - (p1.Y - p3.Y) * (p1.X - p2.X)) / denominator;

        var point = new Point3D(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y), 0);
        return (point, t, u);
    }

    private bool IsPointOnSegment(Point3D p, Point3D a, Point3D b)
    {
        var v = b - a;
        var w = p - a;
        double l2 = v.X * v.X + v.Y * v.Y;
        if (l2 < 1e-10) return Math.Abs(p.X - a.X) < 1e-5 && Math.Abs(p.Y - a.Y) < 1e-5;

        double t = (w.X * v.X + w.Y * v.Y) / l2;
        if (t < -0.01 || t > 1.01) return false;

        var proj = new Point3D(a.X + t * v.X, a.Y + t * v.Y, 0);
        var dist2 = Math.Pow(p.X - proj.X, 2) + Math.Pow(p.Y - proj.Y, 2);
        
        return dist2 < 0.01; // Tolerância de 0.1 ao quadrado
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
