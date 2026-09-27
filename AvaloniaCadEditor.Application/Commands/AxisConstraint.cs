using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public enum AxisConstraint { None, X, Y, Z }

public static class AxisConstraintHelper
{
    public static Point3D Apply(Point3D currentPoint, Point3D basePoint, AxisConstraint constraint)
    {
        return constraint switch
        {
            AxisConstraint.X => new Point3D(currentPoint.X, basePoint.Y, basePoint.Z),
            AxisConstraint.Y => new Point3D(basePoint.X, currentPoint.Y, basePoint.Z),
            AxisConstraint.Z => new Point3D(basePoint.X, basePoint.Y, currentPoint.Z),
            _ => currentPoint
        };
    }

    public static AxisConstraint ToggleConstraint(CadKey key, AxisConstraint currentConstraint)
    {
        var newConstraint = currentConstraint;
        switch (key)
        {
            case CadKey.ArrowRight: newConstraint = AxisConstraint.X; break;
            case CadKey.ArrowLeft: newConstraint = AxisConstraint.Y; break;
            case CadKey.ArrowUp: newConstraint = AxisConstraint.Z; break;
        }

        if (currentConstraint == newConstraint && (key == CadKey.ArrowRight || key == CadKey.ArrowLeft || key == CadKey.ArrowUp))
        {
            return AxisConstraint.None; // Toggle off if the same key is pressed
        }
        
        return newConstraint;
    }
}
