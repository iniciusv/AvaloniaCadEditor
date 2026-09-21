using System;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public class BoxFeature : FeatureBase
{
    // Defined in global coordinates
    public Point3D GlobalCenter { get; set; }
    public double Width { get; set; }  // X size
    public double Length { get; set; } // Y size
    public double Height { get; set; } // Z size
    
    // Optional: Orientation matrix or Quaternions for rotation
    public Vector3D DirectionX { get; set; } = new Vector3D(1, 0, 0);
    public Vector3D DirectionY { get; set; } = new Vector3D(0, 1, 0);
    public Vector3D DirectionZ { get; set; } = new Vector3D(0, 0, 1);

    public BoxFeature(Point3D globalCenter, double width, double length, double height, string? name = null)
    {
        GlobalCenter = globalCenter;
        Width = width;
        Length = length;
        Height = height;
        Name = name ?? $"Box ({width}x{length}x{height})";
    }
}
