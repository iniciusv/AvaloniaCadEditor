using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

public class CircleProfile : IProfile
{
    public Guid Id { get; } = Guid.NewGuid();
    public ProfileClassification Classification => ProfileClassification.Circle;
    public IReadOnlyList<ProfileCandidate> AvailableCandidates { get; set; } = Array.Empty<ProfileCandidate>();
    public IReadOnlyList<Guid> SourceEntityIds { get; }
    public ValueObjects.WorkPlane Plane { get; set; } = ValueObjects.WorkPlane.XY;

    public Point3D Center { get; set; }
    public double Radius { get; set; }
    public Vector3D Normal => Plane.Normal;
    public bool IsSelected { get; set; } = false;
    public bool IsVisible { get; set; } = true;

    public double Area => Math.PI * Radius * Radius;

    public void UpdateRadius(double newRadius)
    {
        if (newRadius > 0)
        {
            Radius = newRadius;
        }
    }

    public void UpdateCenter(Point3D newCenter)
    {
        Center = newCenter;
    }

    public CircleProfile(
        Point3D center,
        double radius,
        Vector3D? normal = null,
        IEnumerable<Guid>? sourceEntityIds = null,
        ValueObjects.WorkPlane? plane = null)
    {
        Center = center;
        Radius = radius;
        SourceEntityIds = sourceEntityIds?.ToList() ?? new List<Guid>();
        Plane = plane ?? ValueObjects.WorkPlane.XY;
    }

    public IReadOnlyList<Point3D> GetVertices(int segments = 32)
    {
        var vertices = new List<Point3D>(segments);
        for (int i = 0; i < segments; i++)
        {
            double theta = 2.0 * Math.PI * i / segments;
            var localPt = new Point3D(
                Center.X + Radius * Math.Cos(theta),
                Center.Y + Radius * Math.Sin(theta),
                Center.Z
            );
            vertices.Add(Plane.ToWorldSpace(localPt));
        }
        return vertices;
    }

    public (IReadOnlyList<Point3D> Vertices, IReadOnlyList<uint> Indices) GetTessellatedFace(int segments = 36)
    {
        var verts = new List<Point3D>(segments + 1);
        var indices = new List<uint>(segments * 3);

        // Center vertex
        verts.Add(Plane.ToWorldSpace(Center));

        // Perimeter vertices
        for (int i = 0; i < segments; i++)
        {
            double theta = 2.0 * Math.PI * i / segments;
            var localPt = new Point3D(
                Center.X + Radius * Math.Cos(theta),
                Center.Y + Radius * Math.Sin(theta),
                Center.Z
            );
            verts.Add(Plane.ToWorldSpace(localPt));
        }

        // Fan triangles
        for (int i = 0; i < segments; i++)
        {
            uint current = (uint)(i + 1);
            uint next = (uint)((i + 1) % segments + 1);
            indices.Add(0);
            indices.Add(current);
            indices.Add(next);
        }

        return (verts, indices);
    }

    public bool ContainsPoint(Point3D point)
    {
        double dx = point.X - Center.X;
        double dy = point.Y - Center.Y;
        return (dx * dx + dy * dy) <= (Radius * Radius + 1e-4);
    }

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
}

