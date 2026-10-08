using System.Numerics;
using Avalonia;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Settings;
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

    /// <summary>
    /// Configuração de tesselação deste frame, resolvida uma vez por
    /// <see cref="SilkViewportControl"/>. Vem pronta no contexto, e não de um provider chamado
    /// dentro do pass, para que um render pass não faça I/O no meio do desenho.
    /// </summary>
    public required TessellationSettings Tessellation { get; init; }

    /// <summary>
    /// Ligado pelo <see cref="WireframeRenderPass"/> quando as faces dos sólidos ficaram no depth
    /// buffer (o prepasse das linhas ocultas) e ainda não foram limpas. Quem limpa é o
    /// <see cref="GridRenderPass"/>, que vem logo depois: antes de limpar ele desenha a grade com
    /// esse depth valendo, para a peça esconder a grade como já esconde no modo sombreado, em vez
    /// de a grade cruzar o desenho por cima.
    /// </summary>
    public bool SolidDepthPending { get; set; }
}
