using System.Numerics;
using Avalonia;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.UI.ReferenceGuides;

/// <summary>
/// World-to-screen projection for placing 2D labels over the 3D viewport. Same math as the
/// inline projection in <see cref="AvaloniaCadEditor.UI.Controls.Input.ViewportInputHandler"/>
/// (view*proj, perspective divide, Y flip), kept local to this feature instead of promoting
/// it onto <see cref="CameraState"/>.
/// </summary>
public static class ScreenProjection
{
    public static Point? WorldToScreen(CameraState camera, Point3D worldPoint, Size viewportSize)
    {
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0) return null;

        var viewProj = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        var clip = Vector4.Transform(new Vector4((float)worldPoint.X, (float)worldPoint.Y, (float)worldPoint.Z, 1.0f), viewProj);

        if (clip.W <= 0.0001f) return null; // behind the camera

        var ndc = clip / clip.W;
        double screenX = (ndc.X + 1.0) * 0.5 * viewportSize.Width;
        double screenY = (1.0 - ndc.Y) * 0.5 * viewportSize.Height; // NDC Y is up, screen Y is down

        return new Point(screenX, screenY);
    }
}
