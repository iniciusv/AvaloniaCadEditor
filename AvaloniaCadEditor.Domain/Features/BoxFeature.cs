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

    public EdgeType[] GetEdgesForFace(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Top => new[] { EdgeType.TopFront, EdgeType.TopBack, EdgeType.TopLeft, EdgeType.TopRight },
            FaceType.Bottom => new[] { EdgeType.BottomFront, EdgeType.BottomBack, EdgeType.BottomLeft, EdgeType.BottomRight },
            FaceType.Front => new[] { EdgeType.TopFront, EdgeType.BottomFront, EdgeType.FrontLeft, EdgeType.FrontRight },
            FaceType.Back => new[] { EdgeType.TopBack, EdgeType.BottomBack, EdgeType.BackLeft, EdgeType.BackRight },
            FaceType.Left => new[] { EdgeType.TopLeft, EdgeType.BottomLeft, EdgeType.FrontLeft, EdgeType.BackLeft },
            FaceType.Right => new[] { EdgeType.TopRight, EdgeType.BottomRight, EdgeType.FrontRight, EdgeType.BackRight },
            _ => Array.Empty<EdgeType>()
        };
    }

    public EdgeType[] GetAllEdges()
    {
        return new[] {
            EdgeType.TopFront, EdgeType.TopBack, EdgeType.TopLeft, EdgeType.TopRight,
            EdgeType.BottomFront, EdgeType.BottomBack, EdgeType.BottomLeft, EdgeType.BottomRight,
            EdgeType.FrontLeft, EdgeType.FrontRight,
            EdgeType.BackLeft, EdgeType.BackRight
        };
    }

    public (Point3D Start, Point3D End, Vector3D Normal1, Vector3D Normal2) GetEdgeGeometry(EdgeType edgeType)
    {
        var verts = GetVertices();
        Point3D start, end;
        Vector3D n1, n2;

        switch (edgeType)
        {
            case EdgeType.TopFront: start = verts[4]; end = verts[5]; n1 = GetFaceNormal(FaceType.Top); n2 = GetFaceNormal(FaceType.Front); break;
            case EdgeType.TopBack: start = verts[7]; end = verts[6]; n1 = GetFaceNormal(FaceType.Top); n2 = GetFaceNormal(FaceType.Back); break;
            case EdgeType.TopLeft: start = verts[4]; end = verts[7]; n1 = GetFaceNormal(FaceType.Top); n2 = GetFaceNormal(FaceType.Left); break;
            case EdgeType.TopRight: start = verts[5]; end = verts[6]; n1 = GetFaceNormal(FaceType.Top); n2 = GetFaceNormal(FaceType.Right); break;
            case EdgeType.BottomFront: start = verts[0]; end = verts[1]; n1 = GetFaceNormal(FaceType.Bottom); n2 = GetFaceNormal(FaceType.Front); break;
            case EdgeType.BottomBack: start = verts[3]; end = verts[2]; n1 = GetFaceNormal(FaceType.Bottom); n2 = GetFaceNormal(FaceType.Back); break;
            case EdgeType.BottomLeft: start = verts[0]; end = verts[3]; n1 = GetFaceNormal(FaceType.Bottom); n2 = GetFaceNormal(FaceType.Left); break;
            case EdgeType.BottomRight: start = verts[1]; end = verts[2]; n1 = GetFaceNormal(FaceType.Bottom); n2 = GetFaceNormal(FaceType.Right); break;
            case EdgeType.FrontLeft: start = verts[0]; end = verts[4]; n1 = GetFaceNormal(FaceType.Front); n2 = GetFaceNormal(FaceType.Left); break;
            case EdgeType.FrontRight: start = verts[1]; end = verts[5]; n1 = GetFaceNormal(FaceType.Front); n2 = GetFaceNormal(FaceType.Right); break;
            case EdgeType.BackLeft: start = verts[3]; end = verts[7]; n1 = GetFaceNormal(FaceType.Back); n2 = GetFaceNormal(FaceType.Left); break;
            case EdgeType.BackRight: start = verts[2]; end = verts[6]; n1 = GetFaceNormal(FaceType.Back); n2 = GetFaceNormal(FaceType.Right); break;
            default: throw new ArgumentException("Unknown EdgeType");
        }
        return (start, end, n1, n2);
    }
}
