using System;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public class ExtrudeFeature : IFeature
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public bool IsSelected { get; set; } = false;

    public bool HasError => Profile == null;
    public string? ErrorMessage => HasError ? "Referência perdida: perfil de origem foi removido" : null;

    public IProfile Profile { get; set; }
    public double Depth { get; set; }
    public Vector3D Direction { get; set; }

    public double BaseArea => Profile?.Area ?? 0.0;
    public double Volume => BaseArea * Math.Abs(Depth);

    public Point3D BaseCenter => Profile?.Center ?? Point3D.Origin;
    public Point3D TopCenter
    {
        get
        {
            var baseC = BaseCenter;
            var dir = Direction.Normalize() * Depth;
            return new Point3D(baseC.X + dir.X, baseC.Y + dir.Y, baseC.Z + dir.Z);
        }
    }

    public Point3D Centroid
    {
        get
        {
            var baseC = BaseCenter;
            var topC = TopCenter;
            return new Point3D((baseC.X + topC.X) / 2.0, (baseC.Y + topC.Y) / 2.0, (baseC.Z + topC.Z) / 2.0);
        }
    }

    public ExtrudeFeature(IProfile profile, double depth, Vector3D? direction = null, string? name = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Depth = depth;
        Direction = direction ?? profile.Normal * depth;
        
        string typeName = profile.Classification switch
        {
            ProfileClassification.Circle => "Cilindro",
            ProfileClassification.Rectangle => "Prisma Retangular",
            _ => "Extrusão 3D"
        };
        Name = name ?? $"{typeName} ({depth:F1}mm)";
    }

    public void UpdateDepth(double depth)
    {
        if (depth != 0)
        {
            Depth = depth;
            Direction = Profile.Normal * depth;
        }
    }
}
