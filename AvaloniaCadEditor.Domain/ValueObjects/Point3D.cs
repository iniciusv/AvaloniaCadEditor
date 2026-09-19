namespace AvaloniaCadEditor.Domain.ValueObjects;

public readonly struct Point3D : IEquatable<Point3D>
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public Point3D(double x, double y, double z = 0.0)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Point3D Origin => new Point3D(0, 0, 0);

    public double DistanceTo(Point3D other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static Point3D operator +(Point3D p, Vector3D v) => new Point3D(p.X + v.X, p.Y + v.Y, p.Z + v.Z);
    public static Point3D operator -(Point3D p, Vector3D v) => new Point3D(p.X - v.X, p.Y - v.Y, p.Z - v.Z);
    public static Vector3D operator -(Point3D p1, Point3D p2) => new Vector3D(p1.X - p2.X, p1.Y - p2.Y, p1.Z - p2.Z);

    public bool Equals(Point3D other)
    {
        const double tolerance = 1e-6;
        return Math.Abs(X - other.X) < tolerance &&
               Math.Abs(Y - other.Y) < tolerance &&
               Math.Abs(Z - other.Z) < tolerance;
    }

    public override bool Equals(object? obj) => obj is Point3D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    
    public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
}
