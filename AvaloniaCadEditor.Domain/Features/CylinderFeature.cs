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
}
