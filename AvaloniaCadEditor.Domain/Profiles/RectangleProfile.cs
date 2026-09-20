using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

public class RectangleProfile : IProfile
{
    public Guid Id { get; } = Guid.NewGuid();
    public ProfileClassification Classification => ProfileClassification.Rectangle;
    public IReadOnlyList<ProfileCandidate> AvailableCandidates { get; set; } = Array.Empty<ProfileCandidate>();
    public IReadOnlyList<Guid> SourceEntityIds { get; }

    public Point3D Center { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double RotationAngle { get; set; } // in radians, relative to reference axis
    public Vector3D Normal { get; set; }

    public double Area => Width * Height;

    public RectangleProfile(
        Point3D center,
        double width,
        double height,
        double rotationAngle = 0.0,
        Vector3D? normal = null,
        IEnumerable<Guid>? sourceEntityIds = null)
    {
        Center = center;
        Width = width;
        Height = height;
        RotationAngle = rotationAngle;
        Normal = normal ?? new Vector3D(0, 0, 1);
        SourceEntityIds = sourceEntityIds?.ToList() ?? new List<Guid>();
    }

    public IReadOnlyList<Point3D> GetVertices()
    {
        double halfW = Width / 2.0;
        double halfH = Height / 2.0;

        double cos = Math.Cos(RotationAngle);
        double sin = Math.Sin(RotationAngle);

        // Local corners on XY plane relative to center
        (double dx, double dy)[] corners =
        {
            (-halfW, -halfH),
            ( halfW, -halfH),
            ( halfW,  halfH),
            (-halfW,  halfH)
        };

        var vertices = new List<Point3D>();
        foreach (var (dx, dy) in corners)
        {
            double rotX = dx * cos - dy * sin;
            double rotY = dx * sin + dy * cos;
            vertices.Add(new Point3D(Center.X + rotX, Center.Y + rotY, Center.Z));
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
