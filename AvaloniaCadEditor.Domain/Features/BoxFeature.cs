using System;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public class BoxFeature : FeatureBase
{
    // Defined in global coordinates
    public Point3D BaseCorner { get; set; }
    public double Width { get; set; }  // X size
    public double Length { get; set; } // Y size
    public double Height { get; set; } // Z size
    
    // Optional: Orientation matrix or Quaternions for rotation
    public Vector3D DirectionX { get; set; } = new Vector3D(1, 0, 0);
    public Vector3D DirectionY { get; set; } = new Vector3D(0, 1, 0);
    public Vector3D DirectionZ { get; set; } = new Vector3D(0, 0, 1);

    public BoxFeature(Point3D baseCorner, double width, double length, double height, string? name = null)
    {
        BaseCorner = baseCorner;
        Width = width;
        Length = length;
        Height = height;
        Name = name ?? $"Box ({width}x{length}x{height})";
    }

    public Point3D GetFaceCenter(FaceType faceType)
    {
        Point3D bottomCenter = new Point3D(
            BaseCorner.X + DirectionX.X * (Width / 2.0) + DirectionY.X * (Length / 2.0),
            BaseCorner.Y + DirectionX.Y * (Width / 2.0) + DirectionY.Y * (Length / 2.0),
            BaseCorner.Z + DirectionX.Z * (Width / 2.0) + DirectionY.Z * (Length / 2.0)
        );

        return faceType switch
        {
            FaceType.Bottom => bottomCenter,
            FaceType.Top => new Point3D(bottomCenter.X + DirectionZ.X * Height, bottomCenter.Y + DirectionZ.Y * Height, bottomCenter.Z + DirectionZ.Z * Height),
            FaceType.Front => new Point3D(bottomCenter.X - DirectionY.X * (Length / 2.0) + DirectionZ.X * (Height / 2.0), bottomCenter.Y - DirectionY.Y * (Length / 2.0) + DirectionZ.Y * (Height / 2.0), bottomCenter.Z - DirectionY.Z * (Length / 2.0) + DirectionZ.Z * (Height / 2.0)),
            FaceType.Back => new Point3D(bottomCenter.X + DirectionY.X * (Length / 2.0) + DirectionZ.X * (Height / 2.0), bottomCenter.Y + DirectionY.Y * (Length / 2.0) + DirectionZ.Y * (Height / 2.0), bottomCenter.Z + DirectionY.Z * (Length / 2.0) + DirectionZ.Z * (Height / 2.0)),
            FaceType.Right => new Point3D(bottomCenter.X + DirectionX.X * (Width / 2.0) + DirectionZ.X * (Height / 2.0), bottomCenter.Y + DirectionX.Y * (Width / 2.0) + DirectionZ.Y * (Height / 2.0), bottomCenter.Z + DirectionX.Z * (Width / 2.0) + DirectionZ.Z * (Height / 2.0)),
            FaceType.Left => new Point3D(bottomCenter.X - DirectionX.X * (Width / 2.0) + DirectionZ.X * (Height / 2.0), bottomCenter.Y - DirectionX.Y * (Width / 2.0) + DirectionZ.Y * (Height / 2.0), bottomCenter.Z - DirectionX.Z * (Width / 2.0) + DirectionZ.Z * (Height / 2.0)),
            _ => bottomCenter
        };
    }

    public Point3D GetFaceCorner(FaceType faceType)
    {
        Point3D faceCenter = GetFaceCenter(faceType);
        Vector3D dirX = GetFaceDirectionX(faceType);
        Vector3D dirY = GetFaceDirectionY(faceType);
        double w = GetFaceWidth(faceType);
        double l = GetFaceLength(faceType);
        return new Point3D(
            faceCenter.X - dirX.X * (w / 2.0) - dirY.X * (l / 2.0),
            faceCenter.Y - dirX.Y * (w / 2.0) - dirY.Y * (l / 2.0),
            faceCenter.Z - dirX.Z * (w / 2.0) - dirY.Z * (l / 2.0)
        );
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

    public BoxFeature CreateExtrusion(FaceType faceType, double extrusionHeight, string? name = null)
    {
        var extrudedBox = new BoxFeature(
            GetFaceCorner(faceType),
            GetFaceWidth(faceType),
            GetFaceLength(faceType),
            extrusionHeight,
            name);
            
        extrudedBox.AlignToFace(this, faceType);
        return extrudedBox;
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

    public override IReadOnlyList<Point3D> GetVertices()
    {
        var v0 = BaseCorner;
        var v1 = BaseCorner + DirectionX * Width;
        var v2 = BaseCorner + DirectionX * Width + DirectionY * Length;
        var v3 = BaseCorner + DirectionY * Length;
        var up = DirectionZ * Height;

        return new List<Point3D>
        {
            v0,
            v1,
            v2,
            v3,
            v0 + up,
            v1 + up,
            v2 + up,
            v3 + up
        }.AsReadOnly();
    }

    public Point3D GetVertex(BoxCorner corner) => GetVertex((int)corner);
}
