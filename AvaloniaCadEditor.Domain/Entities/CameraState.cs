using System;
using System.Numerics;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Entities;

public class CameraState
{
    public Vector3 Target { get; set; } = Vector3.Zero;
    public float Distance { get; set; } = 100f;
    public float Azimuth { get; set; } = MathF.PI / 4f; // 45 degrees
    public float Elevation { get; set; } = MathF.PI / 4f; // 45 degrees

    public float Fov { get; set; } = MathF.PI / 3f; // 60 degrees
    public float AspectRatio { get; set; } = 1f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 10000f;

    /// <summary>
    /// Rotação da tela (radianos) em torno da direção de visão, além do "cima" orbital de
    /// Azimuth/Elevation. Zero na navegação livre (<see cref="Orbit"/> sempre zera); diferente de
    /// zero só quando <see cref="SetStandardView"/> olha para um referencial girado fora de Z, cujas
    /// vistas padrão não ficam "de pé" olhando com a câmera Z-up sem girar a tela.
    /// </summary>
    public float Roll { get; set; }

    public Vector3 Position
    {
        get
        {
            float x = Target.X + Distance * MathF.Cos(Elevation) * MathF.Sin(Azimuth);
            float y = Target.Y + Distance * MathF.Cos(Elevation) * MathF.Cos(Azimuth);
            float z = Target.Z + Distance * MathF.Sin(Elevation);
            return new Vector3(x, y, z);
        }
    }

    public bool IsOrthographic { get; set; }

    // Derivative of Position w.r.t. Elevation: always orthogonal to the view direction, so it stays
    // well-defined even looking straight down (Top view), where a fixed +Z up would degenerate.
    // With Roll != 0, rotated around the view direction (Position - Target).
    public Vector3 Up
    {
        get
        {
            var baseUp = new Vector3(
                -MathF.Sin(Elevation) * MathF.Sin(Azimuth),
                -MathF.Sin(Elevation) * MathF.Cos(Azimuth),
                MathF.Cos(Elevation));
            if (Roll == 0f) return baseUp;

            var axis = Vector3.Normalize(Position - Target);
            return Vector3.Transform(baseUp, Matrix4x4.CreateFromAxisAngle(axis, Roll));
        }
    }

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Target, Up);
    }

    public Matrix4x4 GetProjectionMatrix()
    {
        // Scale the clip planes with Distance so zooming far out (e.g. to see a large model, or
        // the grid growing to match it) doesn't get near/far-clipped by the fixed defaults.
        float far = MathF.Max(FarPlane, Distance * 100f);

        if (IsOrthographic)
        {
            // Same framing as the perspective view at the Target plane, so wheel zoom (Distance) still works.
            // Negative near plane keeps geometry behind the eye visible when zoomed in.
            float height = 2f * Distance * MathF.Tan(Fov / 2f);
            return Matrix4x4.CreateOrthographic(height * AspectRatio, height, -far, far);
        }
        float near = MathF.Min(NearPlane, Distance * 0.001f);
        return Matrix4x4.CreatePerspectiveFieldOfView(Fov, AspectRatio, near, far);
    }

    /// <summary>
    /// Aponta a câmera para uma vista padrão. Sem <paramref name="frame"/>, são as vistas do mundo
    /// (rolagem sempre 0). Com <paramref name="frame"/>, a direção e o "cima" canônicos da vista são
    /// levados pelos eixos do referencial (<see cref="Placement.ApplyVector"/>) antes de apontar a
    /// câmera — com uma peça isolada girada fora de Z, "Front" passa a ser a frente da peça, e não a
    /// do mundo, e a rolagem acompanha o que for preciso para a tela ficar "de pé" para a peça.
    /// </summary>
    public void SetStandardView(StandardView view, PartFrame? frame = null)
    {
        SetCanonicalStandardView(view);
        if (frame == null) return;

        var dir0 = Vector3.Normalize(Position - Target);
        var up0 = Up;

        var worldDir = ToVector3(frame.FrameToWorld.ApplyVector(ToVector3D(dir0)));
        var worldUp = ToVector3(frame.FrameToWorld.ApplyVector(ToVector3D(up0)));
        LookFrom(worldDir, worldUp);
    }

    /// <summary>A tabela de vistas padrão em coordenadas do mundo, sem rolagem — a base usada por
    /// <see cref="SetStandardView"/> antes de levar para o referencial de uma peça isolada.</summary>
    private void SetCanonicalStandardView(StandardView view)
    {
        Roll = 0f;
        switch (view)
        {
            case StandardView.Front:
                Azimuth = MathF.PI;
                Elevation = 0f;
                break;
            case StandardView.Back:
                Azimuth = 0f;
                Elevation = 0f;
                break;
            case StandardView.Right:
                Azimuth = MathF.PI / 2f;
                Elevation = 0f;
                break;
            case StandardView.Left:
                Azimuth = -MathF.PI / 2f;
                Elevation = 0f;
                break;
            case StandardView.Top:
                Azimuth = MathF.PI;
                Elevation = MathF.PI / 2f;
                break;
            case StandardView.Bottom:
                // Olhando de baixo com X para a direita: a frente (-Y) fica no alto da tela.
                Azimuth = MathF.PI;
                Elevation = -MathF.PI / 2f;
                break;
            case StandardView.Isometric:
                Azimuth = 3f * MathF.PI / 4f;
                Elevation = MathF.Atan(1f / MathF.Sqrt(2f)); // true isometric (~35.26°)
                break;
        }
    }

    /// <summary>
    /// Define Azimuth, Elevation e Roll para a câmera olhar na direção <paramref name="eyeDirection"/>
    /// (do alvo para o olho) com o "cima" da tela em <paramref name="up"/> (os dois perpendiculares).
    /// Usada por <see cref="SetStandardView"/> para vistas levadas por um referencial girado.
    /// </summary>
    public void LookFrom(Vector3 eyeDirection, Vector3 up)
    {
        var dir = Vector3.Normalize(eyeDirection);
        up = Vector3.Normalize(up);

        float cosElevation = MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        Elevation = MathF.Atan2(dir.Z, cosElevation);

        const float epsilon = 1e-5f;
        if (cosElevation > epsilon)
        {
            Azimuth = MathF.Atan2(dir.X, dir.Y);
            Roll = 0f;
            Roll = SignedAngleAround(Up, up, dir);
        }
        else
        {
            // Olhando na vertical: a direção não diz o azimute (toda rotação em Z dá a mesma
            // direção) — escolhe o azimute que já produz o "cima" pedido, com rolagem 0.
            float s = dir.Z >= 0 ? 1f : -1f;
            Azimuth = MathF.Atan2(-up.X * s, -up.Y * s);
            Roll = 0f;
        }
    }

    /// <summary>Ângulo com sinal (regra da mão direita em torno de <paramref name="axis"/>) de
    /// <paramref name="from"/> para <paramref name="to"/>.</summary>
    private static float SignedAngleAround(Vector3 from, Vector3 to, Vector3 axis)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        axis = Vector3.Normalize(axis);
        float cos = Vector3.Dot(from, to);
        float sin = Vector3.Dot(Vector3.Cross(from, to), axis);
        return MathF.Atan2(sin, cos);
    }

    private static Vector3D ToVector3D(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 ToVector3(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public void Orbit(float deltaAzimuth, float deltaElevation)
    {
        Roll = 0f;
        Azimuth += deltaAzimuth;
        Elevation += deltaElevation;

        // Clamp elevation to avoid flipping
        float epsilon = 0.01f;
        if (Elevation > MathF.PI / 2f - epsilon) Elevation = MathF.PI / 2f - epsilon;
        if (Elevation < -MathF.PI / 2f + epsilon) Elevation = -MathF.PI / 2f + epsilon;
    }

    public void Pan(float deltaX, float deltaY)
    {
        // Calculate camera local axes directly
        var forward = Vector3.Normalize(Target - Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Up));
        var cameraUp = Vector3.Normalize(Vector3.Cross(right, forward));

        // Adjust pan speed based on distance
        float speed = Distance * 0.001f;
        
        // Dragging right (deltaX > 0) -> scene goes right -> Target goes left (-=)
        Target -= right * deltaX * speed;
        // Dragging down (deltaY > 0) -> scene goes down -> Target goes up (+=)
        Target += cameraUp * deltaY * speed;
    }

    public void Zoom(float factor)
    {
        Distance *= factor;
        if (Distance < 0.1f) Distance = 0.1f;
    }

    public Ray ScreenPointToRay(Vector2 screenPoint, Vector2 viewportSize)
    {
        // 1. Convert screen coordinates (X, Y) to Normalized Device Coordinates (NDC)
        // Screen Y goes down, NDC Y goes up.
        float ndcX = (2.0f * screenPoint.X) / viewportSize.X - 1.0f;
        float ndcY = 1.0f - (2.0f * screenPoint.Y) / viewportSize.Y;

        // 2. We want to find points on the Near and Far planes in NDC space
        var ndcNear = new Vector4(ndcX, ndcY, -1.0f, 1.0f); // OpenGL depth range is -1 to 1 (in some configs), but generally Z=-1 is near
        var ndcFar = new Vector4(ndcX, ndcY, 1.0f, 1.0f);

        // 3. Get the Inverse View-Projection Matrix
        var viewProj = GetViewMatrix() * GetProjectionMatrix();
        Matrix4x4.Invert(viewProj, out var invViewProj);

        // 4. Unproject by multiplying with the inverse matrix
        var worldNear4 = Vector4.Transform(ndcNear, invViewProj);
        var worldFar4 = Vector4.Transform(ndcFar, invViewProj);

        // 5. Perspective divide to get true 3D coordinates
        var worldNear = new Vector3(worldNear4.X, worldNear4.Y, worldNear4.Z) / worldNear4.W;
        var worldFar = new Vector3(worldFar4.X, worldFar4.Y, worldFar4.Z) / worldFar4.W;

        // 6. The ray goes from the near plane into the far plane
        var direction = Vector3.Normalize(worldFar - worldNear);

        return new Ray(worldNear, direction);
    }
}
