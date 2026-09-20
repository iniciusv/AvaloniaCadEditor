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

    public Point3D Center { get; set; }
    public double Radius { get; set; }
    public Vector3D Normal { get; set; }
    public bool IsSelected { get; set; } = false;

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
        IEnumerable<Guid>? sourceEntityIds = null)
    {
        Center = center;
        Radius = radius;
        Normal = normal ?? new Vector3D(0, 0, 1);
        SourceEntityIds = sourceEntityIds?.ToList() ?? new List<Guid>();
    }

    public IReadOnlyList<Point3D> GetVertices()
    {
        // Tessellated approximation for polyline visualization
        int segments = 32;
        var vertices = new List<Point3D>(segments);
        for (int i = 0; i < segments; i++)
        {
            double theta = 2.0 * Math.PI * i / segments;
            vertices.Add(new Point3D(
                Center.X + Radius * Math.Cos(theta),
                Center.Y + Radius * Math.Sin(theta),
                Center.Z
            ));
        }
        return vertices;
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
