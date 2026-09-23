namespace AvaloniaCadEditor.Domain.ValueObjects;

public readonly struct Vector3D : IEquatable<Vector3D>
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public Vector3D(double x, double y, double z = 0.0)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector3D UnitX => new Vector3D(1, 0, 0);
    public static Vector3D UnitY => new Vector3D(0, 1, 0);
    public static Vector3D UnitZ => new Vector3D(0, 0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vector3D Normalize()
    {
        var length = Length;
        if (length < 1e-6) return new Vector3D(0, 0, 0);
        return new Vector3D(X / length, Y / length, Z / length);
    }

    public double DotProduct(Vector3D other)
    {
        return X * other.X + Y * other.Y + Z * other.Z;
    }

    public Vector3D CrossProduct(Vector3D other)
    {
        return new Vector3D(
            Y * other.Z - Z * other.Y,
            Z * other.X - X * other.Z,
            X * other.Y - Y * other.X
        );
    }

    public static Vector3D operator +(Vector3D a, Vector3D b) => new Vector3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3D operator -(Vector3D a, Vector3D b) => new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3D operator *(Vector3D a, double scalar) => new Vector3D(a.X * scalar, a.Y * scalar, a.Z * scalar);
    public bool Equals(Vector3D other)
    {
        const double tolerance = 1e-6;
        return Math.Abs(X - other.X) < tolerance &&
               Math.Abs(Y - other.Y) < tolerance &&
               Math.Abs(Z - other.Z) < tolerance;
    }

    public override bool Equals(object? obj) => obj is Vector3D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
}
