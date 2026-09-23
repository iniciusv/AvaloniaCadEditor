using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public class ExtrudeFeature : FeatureBase
{

    public IProfile Profile { get; set; }
    public double Height { get; set; }
    public Vector3D Direction { get; set; }


    public double BaseArea => Profile?.Area ?? 0.0;
    public double Volume => BaseArea * Math.Abs(Height);

    public Point3D BaseCenter => Profile?.Center ?? Point3D.Origin;
    public Point3D TopCenter
    {
        get
        {
            var baseC = BaseCenter;
            var dir = Direction.Normalize() * Height;
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

    public ExtrudeFeature(IProfile profile, double height, Vector3D? direction = null, string? name = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Height = height;
        Direction = direction ?? profile.Normal * height;
        
        string typeName = profile.Classification switch
        {
            ProfileClassification.Circle => "Cilindro",
            ProfileClassification.Rectangle => "Prisma Retangular",
            _ => "Extrusão 3D"
        };
        Name = name ?? $"{typeName} ({height:F1}mm)";
    }

    public void UpdateHeight(double height)
    {
        if (height != 0)
        {
            Height = height;
            Direction = Profile.Normal * height;
        }
    }
}
