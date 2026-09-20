using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

public class PolygonProfile : IProfile
{
    public Guid Id { get; } = Guid.NewGuid();
    public ProfileClassification Classification => ProfileClassification.Polygon;
    public IReadOnlyList<ProfileCandidate> AvailableCandidates { get; set; } = Array.Empty<ProfileCandidate>();
    public IReadOnlyList<Guid> SourceEntityIds { get; }

    public List<Point3D> Vertices { get; set; }
    public Vector3D Normal { get; set; }

    public Point3D Center
    {
        get
        {
            if (Vertices == null || Vertices.Count == 0) return Point3D.Origin;
            double x = Vertices.Average(v => v.X);
            double y = Vertices.Average(v => v.Y);
            double z = Vertices.Average(v => v.Z);
            return new Point3D(x, y, z);
        }
    }

    public double Area
    {
        get
        {
            if (Vertices == null || Vertices.Count < 3) return 0;
            // Generalized planar polygon area via cross products
            Vector3D totalCross = new Vector3D(0, 0, 0);
            for (int i = 0; i < Vertices.Count; i++)
            {
                var current = Vertices[i];
                var next = Vertices[(i + 1) % Vertices.Count];
                var v1 = new Vector3D(current.X, current.Y, current.Z);
                var v2 = new Vector3D(next.X, next.Y, next.Z);
                totalCross = totalCross + CrossProduct(v1, v2);
            }
            return 0.5 * totalCross.Length;
        }
    }

    public PolygonProfile(IEnumerable<Point3D> vertices, IEnumerable<Guid>? sourceEntityIds = null, Vector3D? normal = null)
    {
        Vertices = vertices.ToList();
        SourceEntityIds = sourceEntityIds?.ToList() ?? new List<Guid>();
        Normal = normal ?? CalculateNormal(Vertices);
    }

    public IReadOnlyList<Point3D> GetVertices() => Vertices.AsReadOnly();

    public IProfile SwitchClassification(ProfileClassification targetClassification)
    {
        var candidate = AvailableCandidates.FirstOrDefault(c => c.Classification == targetClassification);
        if (candidate != null)
        {
            var newProfile = candidate.Factory();
            newProfile.AvailableCandidates = this.AvailableCandidates;
            return newProfile;
        }
        return this;
    }

    private static Vector3D CrossProduct(Vector3D a, Vector3D b)
    {
        return new Vector3D(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );
    }

    private static Vector3D CalculateNormal(IReadOnlyList<Point3D> points)
    {
        if (points.Count < 3) return new Vector3D(0, 0, 1);
        var v1 = points[1] - points[0];
        var v2 = points[2] - points[0];
        var cross = CrossProduct(v1, v2);
        return cross.Length > 1e-6 ? cross.Normalize() : new Vector3D(0, 0, 1);
    }
}
