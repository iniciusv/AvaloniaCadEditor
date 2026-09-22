using System.Numerics;
using Avalonia;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public sealed class RenderContext
{
    public required Matrix4x4 ViewMatrix { get; init; }
    public required Matrix4x4 ProjMatrix { get; init; }
    public required Matrix4x4 IdentityMatrix { get; init; }
    public required uint ShaderProgram { get; init; }
    public required ShaderUniforms Uniforms { get; init; }
    public required CameraState Camera { get; init; }
    public required CadViewModel? ViewModel { get; init; }
    public required Size Bounds { get; init; }
}
