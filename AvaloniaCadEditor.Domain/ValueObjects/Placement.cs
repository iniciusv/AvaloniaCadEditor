using System;

namespace AvaloniaCadEditor.Domain.ValueObjects;

/// <summary>
/// Posição de uma instância de peça no mundo: uma rotação (em torno da origem da peça) seguida
/// de uma translação — ou, com <see cref="Mirror"/>, uma reflexão. A parte linear é sempre
/// ortonormal (determinante +1 na rotação, -1 no espelho). Imutável; combine com <see cref="Then"/>.
/// </summary>
/// <example>
/// <code>
/// Placement.At(100, 0, 0)                      // só move
/// Placement.At(100, 0, 0, rotationZDegrees: 90) // gira 90° em Z e move
/// Placement.Rotation(Vector3D.UnitX, 90).Then(Placement.At(0, 0, 50))
/// Placement.MirrorX(atX: 50)                    // espelha pelo plano X = 50
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

    /// <summary>
    /// True se o posicionamento não é só translação: inclui rotação e/ou espelho. Quem só sabe
    /// transladar um sólido pronto precisa reconstruí-lo nesse caso.
    /// </summary>
    public bool HasRotation =>
        !(Close(_m00, 1, 1e-9) && Close(_m11, 1, 1e-9) && Close(_m22, 1, 1e-9) &&
          Close(_m01, 0, 1e-9) && Close(_m02, 0, 1e-9) && Close(_m10, 0, 1e-9) &&
          Close(_m12, 0, 1e-9) && Close(_m20, 0, 1e-9) && Close(_m21, 0, 1e-9));

    /// <summary>Determinante da parte linear: +1 para rotação pura, -1 quando há espelho.</summary>
    public double Determinant =>
        _m00 * (_m11 * _m22 - _m12 * _m21) -
        _m01 * (_m10 * _m22 - _m12 * _m20) +
        _m02 * (_m10 * _m21 - _m11 * _m20);

    /// <summary>
    /// True se o posicionamento espelha (inverte a "mão" do sólido). Faces e triângulos transformados
    /// por ele saem com a ordem dos vértices invertida, e eixos de revolução (pseudovetores)
    /// precisam do sinal trocado.
    /// </summary>
    public bool IsMirrored => Determinant < 0;

    /// <summary>
    /// True se a rotação (se houver) é só em torno de Z: o eixo Z local continua sendo o Z do mundo.
    /// Um espelho nunca é: <c>MirrorX()</c> tem a mesma diagonal de um giro de 180° em Z só em X, e
    /// <see cref="RotationZRadians"/> o confundiria com esse giro.
    /// </summary>
    public bool IsRotationAboutZOnly =>
        !IsMirrored &&
        Close(_m02, 0, 1e-9) && Close(_m12, 0, 1e-9) &&
        Close(_m20, 0, 1e-9) && Close(_m21, 0, 1e-9) && Close(_m22, 1, 1e-9);

    /// <summary>Ângulo (radianos, regra da mão direita) da rotação em Z. Só faz sentido com <see cref="IsRotationAboutZOnly"/>.</summary>
    public double RotationZRadians => Math.Atan2(_m10, _m00);

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

    /// <summary>Rotação em torno de um eixo que passa por <paramref name="pivot"/> (regra da mão direita).</summary>
    public static Placement RotationAbout(Point3D pivot, Vector3D axis, double degrees) =>
        Translate(new Vector3D(-pivot.X, -pivot.Y, -pivot.Z))
            .Then(Rotation(axis, degrees))
            .Then(Translate(new Vector3D(pivot.X, pivot.Y, pivot.Z)));

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

    /// <summary>
    /// Espelho pelo plano que passa por <paramref name="pointOnPlane"/> com normal
    /// <paramref name="normal"/>: cada ponto vai para o lado oposto do plano, à mesma distância.
    /// Aplicar duas vezes volta ao original.
    /// </summary>
    public static Placement Mirror(Point3D pointOnPlane, Vector3D normal)
    {
        var n = normal.Normalize();
        if (n.Length < 1e-9) throw new ArgumentException("A normal do plano de espelho não pode ser nula.", nameof(normal));

        // Householder: R = I − 2·n·nᵀ; t = 2·(p·n)·n, para que os pontos do plano fiquem parados.
        double x = n.X, y = n.Y, z = n.Z;
        double d = 2 * (pointOnPlane.X * x + pointOnPlane.Y * y + pointOnPlane.Z * z);
        return new Placement(
            1 - 2 * x * x, -2 * x * y, -2 * x * z,
            -2 * y * x, 1 - 2 * y * y, -2 * y * z,
            -2 * z * x, -2 * z * y, 1 - 2 * z * z,
            new Vector3D(d * x, d * y, d * z));
    }

    /// <summary>Espelho pelo plano X = <paramref name="atX"/>: inverte a coordenada X em torno dele (Y e Z ficam).</summary>
    public static Placement MirrorX(double atX = 0) => Mirror(new Point3D(atX, 0, 0), Vector3D.UnitX);

    /// <summary>Espelho pelo plano Y = <paramref name="atY"/>: inverte a coordenada Y em torno dele (X e Z ficam).</summary>
    public static Placement MirrorY(double atY = 0) => Mirror(new Point3D(0, atY, 0), Vector3D.UnitY);

    /// <summary>Espelho pelo plano Z = <paramref name="atZ"/>: inverte a coordenada Z em torno dele (X e Y ficam).</summary>
    public static Placement MirrorZ(double atZ = 0) => Mirror(new Point3D(0, 0, atZ), Vector3D.UnitZ);

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

    /// <summary>O posicionamento que desfaz este: <c>p.Then(p.Inverse())</c> é a identidade.</summary>
    public Placement Inverse()
    {
        // Parte linear ortonormal (rotação ou espelho): a inversa é a transposta; a translação vira -Rᵀ·t.
        var t = Translation;
        return new Placement(
            _m00, _m10, _m20,
            _m01, _m11, _m21,
            _m02, _m12, _m22,
            new Vector3D(
                -(_m00 * t.X + _m10 * t.Y + _m20 * t.Z),
                -(_m01 * t.X + _m11 * t.Y + _m21 * t.Z),
                -(_m02 * t.X + _m12 * t.Y + _m22 * t.Z)));
    }

    public Point3D Apply(Point3D p) =>
        new Point3D(
            _m00 * p.X + _m01 * p.Y + _m02 * p.Z,
            _m10 * p.X + _m11 * p.Y + _m12 * p.Z,
            _m20 * p.X + _m21 * p.Y + _m22 * p.Z) + Translation;

    /// <summary>
    /// Aplica só a parte linear (para direções e normais — que, por ela ser ortonormal, se
    /// transformam igual). Eixos de revolução são pseudovetores: com <see cref="IsMirrored"/>, troque o sinal.
    /// </summary>
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
