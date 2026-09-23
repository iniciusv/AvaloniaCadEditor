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

    public Point3D GetFaceCenter(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Bottom => GlobalCenter,
            FaceType.Top => new Point3D(GlobalCenter.X + DirectionZ.X * Height, GlobalCenter.Y + DirectionZ.Y * Height, GlobalCenter.Z + DirectionZ.Z * Height),
            FaceType.Front => new Point3D(GlobalCenter.X - DirectionY.X * (Length / 2.0) + DirectionZ.X * (Height / 2.0), GlobalCenter.Y - DirectionY.Y * (Length / 2.0) + DirectionZ.Y * (Height / 2.0), GlobalCenter.Z - DirectionY.Z * (Length / 2.0) + DirectionZ.Z * (Height / 2.0)),
            FaceType.Back => new Point3D(GlobalCenter.X + DirectionY.X * (Length / 2.0) + DirectionZ.X * (Height / 2.0), GlobalCenter.Y + DirectionY.Y * (Length / 2.0) + DirectionZ.Y * (Height / 2.0), GlobalCenter.Z + DirectionY.Z * (Length / 2.0) + DirectionZ.Z * (Height / 2.0)),
            FaceType.Right => new Point3D(GlobalCenter.X + DirectionX.X * (Width / 2.0) + DirectionZ.X * (Height / 2.0), GlobalCenter.Y + DirectionX.Y * (Width / 2.0) + DirectionZ.Y * (Height / 2.0), GlobalCenter.Z + DirectionX.Z * (Width / 2.0) + DirectionZ.Z * (Height / 2.0)),
            FaceType.Left => new Point3D(GlobalCenter.X - DirectionX.X * (Width / 2.0) + DirectionZ.X * (Height / 2.0), GlobalCenter.Y - DirectionX.Y * (Width / 2.0) + DirectionZ.Y * (Height / 2.0), GlobalCenter.Z - DirectionX.Z * (Width / 2.0) + DirectionZ.Z * (Height / 2.0)),
            _ => GlobalCenter
        };
    }

    public Vector3D GetFaceNormal(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Top => DirectionZ,
            FaceType.Bottom => new Vector3D(-DirectionZ.X, -DirectionZ.Y, -DirectionZ.Z),
            FaceType.Front => new Vector3D(-DirectionY.X, -DirectionY.Y, -DirectionY.Z),
            FaceType.Back => DirectionY,
            FaceType.Right => DirectionX,
            FaceType.Left => new Vector3D(-DirectionX.X, -DirectionX.Y, -DirectionX.Z),
            _ => DirectionZ
        };
    }

    public double GetFaceWidth(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Top or FaceType.Bottom => Width,
            FaceType.Front or FaceType.Back => Width,
            FaceType.Right or FaceType.Left => Length,
            _ => Width
        };
    }

    public double GetFaceLength(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Top or FaceType.Bottom => Length,
            FaceType.Front or FaceType.Back => Height,
            FaceType.Right or FaceType.Left => Height,
            _ => Length
        };
    }

    public FaceType? GetFaceTypeFromNormal(Vector3D normal)
    {
        double tol = 0.99;
        if (normal.DotProduct(DirectionZ) > tol) return FaceType.Top;
        if (normal.DotProduct(new Vector3D(-DirectionZ.X, -DirectionZ.Y, -DirectionZ.Z)) > tol) return FaceType.Bottom;
        if (normal.DotProduct(new Vector3D(-DirectionY.X, -DirectionY.Y, -DirectionY.Z)) > tol) return FaceType.Front;
        if (normal.DotProduct(DirectionY) > tol) return FaceType.Back;
        if (normal.DotProduct(DirectionX) > tol) return FaceType.Right;
        if (normal.DotProduct(new Vector3D(-DirectionX.X, -DirectionX.Y, -DirectionX.Z)) > tol) return FaceType.Left;
        return null;
    }

    public void AlignToFace(BoxFeature parentBox, FaceType faceType)
    {
        DirectionZ = parentBox.GetFaceNormal(faceType);
        DirectionX = parentBox.GetFaceDirectionX(faceType);
        DirectionY = parentBox.GetFaceDirectionY(faceType);
    }

    public Vector3D GetFaceDirectionX(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Top or FaceType.Bottom => DirectionX,
            FaceType.Front or FaceType.Back => DirectionX,
            FaceType.Right => DirectionY,
            FaceType.Left => new Vector3D(-DirectionY.X, -DirectionY.Y, -DirectionY.Z),
            _ => DirectionX
        };
    }

    public Vector3D GetFaceDirectionY(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Top => DirectionY,
            FaceType.Bottom => new Vector3D(-DirectionY.X, -DirectionY.Y, -DirectionY.Z),
            FaceType.Front => DirectionZ,
            FaceType.Back => DirectionZ,
            FaceType.Right or FaceType.Left => DirectionZ,
            _ => DirectionY
        };
    }
}
