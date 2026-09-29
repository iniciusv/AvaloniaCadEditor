using System;

namespace AvaloniaCadEditor.Domain.ValueObjects;

/// <summary>
/// Posição de uma instância de peça no mundo: uma rotação (em torno da origem da peça) seguida
/// de uma translação. Imutável; combine com <see cref="Then"/>.
/// </summary>
/// <example>
/// <code>
/// Placement.At(100, 0, 0)                      // só move
/// Placement.At(100, 0, 0, rotationZDegrees: 90) // gira 90° em Z e move
/// Placement.Rotation(Vector3D.UnitX, 90).Then(Placement.At(0, 0, 50))
/// </code>
/// </example>
public sealed class Placement
{
    // Matriz de rotação (linhas) e translação.
    private readonly double _m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22;

    private Placement(
        double m00, double m01, double m02,
        double m10, double m11, double m12,
        double m20, double m21, double m22,
        Vector3D translation)
    {
        _m00 = m00; _m01 = m01; _m02 = m02;
        _m10 = m10; _m11 = m11; _m12 = m12;
        _m20 = m20; _m21 = m21; _m22 = m22;
        Translation = translation;
    }

    public static Placement Identity { get; } = new(1, 0, 0, 0, 1, 0, 0, 0, 1, new Vector3D(0, 0, 0));

    public Vector3D Translation { get; }

    /// <summary>True se o posicionamento inclui rotação (não é só translação).</summary>
    public bool HasRotation =>
        !(Close(_m00, 1, 1e-9) && Close(_m11, 1, 1e-9) && Close(_m22, 1, 1e-9) &&
          Close(_m01, 0, 1e-9) && Close(_m02, 0, 1e-9) && Close(_m10, 0, 1e-9) &&
          Close(_m12, 0, 1e-9) && Close(_m20, 0, 1e-9) && Close(_m21, 0, 1e-9));

    public bool IsIdentity => ReferenceEquals(this, Identity) ||
        (Close(_m00, 1) && Close(_m11, 1) && Close(_m22, 1) &&
         Close(_m01, 0) && Close(_m02, 0) && Close(_m10, 0) && Close(_m12, 0) && Close(_m20, 0) && Close(_m21, 0) &&
         Translation.Length < 1e-12);

    /// <summary>Move a origem da peça para (x, y, z), opcionalmente girando antes em torno de Z.</summary>
    public static Placement At(double x, double y, double z = 0, double rotationZDegrees = 0) =>
        RotationZ(rotationZDegrees).Then(Translate(new Vector3D(x, y, z)));

    /// <summary>Move a origem da peça para <paramref name="point"/>.</summary>
    public static Placement At(Point3D point, double rotationZDegrees = 0) =>
        At(point.X, point.Y, point.Z, rotationZDegrees);

    public static Placement Translate(Vector3D offset) =>
        new(1, 0, 0, 0, 1, 0, 0, 0, 1, offset);

    public static Placement RotationZ(double degrees) => Rotation(Vector3D.UnitZ, degrees);

    /// <summary>Rotação em torno de um eixo que passa pela origem da peça (regra da mão direita).</summary>
    public static Placement Rotation(Vector3D axis, double degrees)
    {
        var a = axis.Normalize();
        if (a.Length < 1e-9) throw new ArgumentException("O eixo de rotação não pode ser nulo.", nameof(axis));

        double t = degrees * Math.PI / 180.0;
        double c = Math.Cos(t), s = Math.Sin(t), k = 1 - c;
        double x = a.X, y = a.Y, z = a.Z;

        return new Placement(
            c + x * x * k, x * y * k - z * s, x * z * k + y * s,
            y * x * k + z * s, c + y * y * k, y * z * k - x * s,
            z * x * k - y * s, z * y * k + x * s, c + z * z * k,
            new Vector3D(0, 0, 0));
    }

    /// <summary>Aplica este posicionamento e depois <paramref name="next"/>.</summary>
    public Placement Then(Placement next)
    {
        ArgumentNullException.ThrowIfNull(next);
        // next ∘ this: R = Rn·Rt, t = Rn·tt + tn
        return new Placement(
            next._m00 * _m00 + next._m01 * _m10 + next._m02 * _m20,
            next._m00 * _m01 + next._m01 * _m11 + next._m02 * _m21,
            next._m00 * _m02 + next._m01 * _m12 + next._m02 * _m22,
            next._m10 * _m00 + next._m11 * _m10 + next._m12 * _m20,
            next._m10 * _m01 + next._m11 * _m11 + next._m12 * _m21,
            next._m10 * _m02 + next._m11 * _m12 + next._m12 * _m22,
            next._m20 * _m00 + next._m21 * _m10 + next._m22 * _m20,
            next._m20 * _m01 + next._m21 * _m11 + next._m22 * _m21,
            next._m20 * _m02 + next._m21 * _m12 + next._m22 * _m22,
            next.ApplyVector(Translation) + next.Translation);
    }

    public Point3D Apply(Point3D p) =>
        new Point3D(
            _m00 * p.X + _m01 * p.Y + _m02 * p.Z,
            _m10 * p.X + _m11 * p.Y + _m12 * p.Z,
            _m20 * p.X + _m21 * p.Y + _m22 * p.Z) + Translation;

    /// <summary>Aplica só a rotação (para direções e normais).</summary>
    public Vector3D ApplyVector(Vector3D v) =>
        new Vector3D(
            _m00 * v.X + _m01 * v.Y + _m02 * v.Z,
            _m10 * v.X + _m11 * v.Y + _m12 * v.Z,
            _m20 * v.X + _m21 * v.Y + _m22 * v.Z);

    /// <summary>Transforma um vetor em float (x, y, z) in-place — usado para malhas.</summary>
    public void ApplyToFloats(float[] values, int offset, bool isPoint)
    {
        double x = values[offset], y = values[offset + 1], z = values[offset + 2];
        double nx = _m00 * x + _m01 * y + _m02 * z;
        double ny = _m10 * x + _m11 * y + _m12 * z;
        double nz = _m20 * x + _m21 * y + _m22 * z;
        if (isPoint)
        {
            nx += Translation.X;
            ny += Translation.Y;
            nz += Translation.Z;
        }
        values[offset] = (float)nx;
        values[offset + 1] = (float)ny;
        values[offset + 2] = (float)nz;
    }

    public override string ToString() =>
        $"Placement(t=({Translation.X:0.###}, {Translation.Y:0.###}, {Translation.Z:0.###}))";

    private static bool Close(double a, double b, double tolerance = 1e-12) => Math.Abs(a - b) < tolerance;
}
