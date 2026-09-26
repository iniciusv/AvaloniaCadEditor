using System;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

/// <summary>
/// A native CSG primitive for a cylinder.
/// The model's properties are stored in Global coordinates.
/// </summary>
public class CylinderFeature : FeatureBase
{
    // Math model defined in global coordinates
    public Point3D GlobalBaseCenter { get; set; }
    public Vector3D GlobalDirection { get; set; }
    public double Radius { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// Creates a cylinder directly in global coordinates.
    /// </summary>
    public CylinderFeature(Point3D globalBaseCenter, Vector3D globalDirection, double radius, double height, string? name = null)
    {
        GlobalBaseCenter = globalBaseCenter;
        GlobalDirection = globalDirection.Normalize();
        Radius = radius;
        Height = height;
        Name = name ?? $"Cylinder (R{radius}, H{height})";
    }

    /// <summary>
    /// Creates a cylinder using a WorkPlane as a 'lens' or reference coordinate system.
    /// The coordinates passed here are Local to the work plane, but they are converted
    /// and stored as Global coordinates.
    /// </summary>
    public static CylinderFeature FromWorkPlane(WorkPlane plane, Point3D localCenter, double radius, double height, string? name = null)
    {
        // TODO: Need a Matrix4x4 transformation in WorkPlane to correctly map local to global.
        // For now, simple translation based on WorkPlane origin and assuming normal is Z
        // A complete implementation will multiply the localCenter by the Plane's transformation matrix.
        
        // Mocking the transformation:
        var globalCenter = new Point3D(
            plane.Origin.X + localCenter.X,
            plane.Origin.Y + localCenter.Y,
            plane.Origin.Z + localCenter.Z
        );

        // Direction is the plane's normal
        var globalDirection = new Vector3D(plane.Normal.X, plane.Normal.Y, plane.Normal.Z);

        return new CylinderFeature(globalCenter, globalDirection, radius, height, name);
    }

    public Point3D GetFaceCenter(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Bottom => GlobalBaseCenter,
            FaceType.Top => new Point3D(
                GlobalBaseCenter.X + GlobalDirection.X * Height,
                GlobalBaseCenter.Y + GlobalDirection.Y * Height,
                GlobalBaseCenter.Z + GlobalDirection.Z * Height),
            _ => GlobalBaseCenter
        };
    }

    public Vector3D GetFaceNormal(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Top => GlobalDirection,
            FaceType.Bottom => new Vector3D(-GlobalDirection.X, -GlobalDirection.Y, -GlobalDirection.Z),
            _ => GlobalDirection
        };
    }

    public FaceType? GetFaceTypeFromNormal(Vector3D normal)
    {
        double tol = 0.99;
        if (normal.DotProduct(GlobalDirection) > tol) return FaceType.Top;
        if (normal.DotProduct(new Vector3D(-GlobalDirection.X, -GlobalDirection.Y, -GlobalDirection.Z)) > tol) return FaceType.Bottom;
        return null;
    }

    public override IReadOnlyList<Point3D> GetVertices()
    {
        var bottomCenter = GetFaceCenter(FaceType.Bottom);
        var topCenter = GetFaceCenter(FaceType.Top);
        return new List<Point3D> { bottomCenter, topCenter }.AsReadOnly();
    }
}
