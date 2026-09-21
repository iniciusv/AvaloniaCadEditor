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

    public ValueObjects.WorkPlane Plane { get; set; } = ValueObjects.WorkPlane.XY;

    public Point3D Center { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double RotationAngle { get; set; } // in radians, relative to reference axis
    public Vector3D Normal => Plane.Normal;
    public bool IsSelected { get; set; } = false;
    public bool IsVisible { get; set; } = true;

    public double Area => Width * Height;

    public RectangleProfile(
        Point3D center,
        double width,
        double height,
        double rotationAngle = 0.0,
        Vector3D? normal = null,
        IEnumerable<Guid>? sourceEntityIds = null,
        ValueObjects.WorkPlane? plane = null)
    {
        Center = center;
        Width = width;
        Height = height;
        RotationAngle = rotationAngle;
        SourceEntityIds = sourceEntityIds?.ToList() ?? new List<Guid>();
        Plane = plane ?? ValueObjects.WorkPlane.XY;
    }

    public IReadOnlyList<Point3D> GetVertices(int segments = 32)
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
            var localPt = new Point3D(Center.X + rotX, Center.Y + rotY, Center.Z);
            vertices.Add(Plane.ToWorldSpace(localPt));
        }

        return vertices;
    }

    public (IReadOnlyList<Point3D> Vertices, IReadOnlyList<uint> Indices) GetTessellatedFace(int segments = 36)
    {
        var verts = GetVertices();
        var indices = new uint[] { 0, 1, 2, 0, 2, 3 };
        return (verts, indices);
    }

    public bool ContainsPoint(Point3D point)
    {
        double dx = point.X - Center.X;
        double dy = point.Y - Center.Y;

        double cos = Math.Cos(RotationAngle);
        double sin = Math.Sin(RotationAngle);

        double lx = dx * cos + dy * sin;
        double ly = -dx * sin + dy * cos;

        double halfW = Width / 2.0;
        double halfH = Height / 2.0;

        return Math.Abs(lx) <= halfW + 1e-4 && Math.Abs(ly) <= halfH + 1e-4;
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

