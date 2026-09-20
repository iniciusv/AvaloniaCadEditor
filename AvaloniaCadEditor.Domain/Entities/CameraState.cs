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

    public Vector3 Up => Vector3.UnitZ;

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Target, Up);
    }

    public Matrix4x4 GetProjectionMatrix()
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(Fov, AspectRatio, NearPlane, FarPlane);
    }

    public void Orbit(float deltaAzimuth, float deltaElevation)
    {
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
