using System.Collections.Generic;
using AvaloniaCadEditor.Application.Commands;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

/// <summary>
/// Desenha as linhas guia de uma ferramenta ativa que implemente <see cref="IGuideProvider"/>
/// (ex.: o transferidor do RotateTool). Sem guarda de <c>CurrentMode</c>: ao contrário do
/// <see cref="SketchEntityRenderPass"/> (só sketch), essas ferramentas atuam sobre features 3D
/// soltas na cena, então o transferidor precisa aparecer em qualquer modo. Mesmo padrão do
/// <see cref="AvaloniaCadEditor.UI.ReferenceGuides.ReferenceGuideRenderPass"/>: monta os vértices
/// a cada frame (poucos segmentos, sem custo relevante) e desenha com o depth test desligado.
/// </summary>
public sealed class ToolGuideRenderPass : IRenderPass
{
    private uint _vao;
    private uint _vbo;

    public void Init(GL gl)
    {
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();

        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
        }
        gl.BindVertexArray(0);
    }

    public void Render(GL gl, RenderContext context)
    {
        if (context.ViewModel?.CurrentTool is not IGuideProvider guideProvider) return;

        var vertices = new List<float>();
        foreach (var segment in guideProvider.GetGuideSegments())
        {
            vertices.Add((float)segment.From.X); vertices.Add((float)segment.From.Y); vertices.Add((float)segment.From.Z);
            vertices.Add(segment.R); vertices.Add(segment.G); vertices.Add(segment.B);
            vertices.Add((float)segment.To.X); vertices.Add((float)segment.To.Y); vertices.Add((float)segment.To.Z);
            vertices.Add(segment.R); vertices.Add(segment.G); vertices.Add(segment.B);
        }
        if (vertices.Count == 0) return;

        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        var identity = context.IdentityMatrix;
        unsafe
        {
            gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&identity);
        }
        gl.Uniform1(context.Uniforms.LightingLoc, 0);
        gl.Uniform1(context.Uniforms.AlphaLoc, 1.0f);
        gl.Disable(EnableCap.DepthTest);

        unsafe
        {
            var arr = vertices.ToArray();
            fixed (float* ptr = arr)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(float)), ptr, BufferUsageARB.DynamicDraw);
            }
        }
        gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertices.Count / 6));

        gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose(GL gl)
    {
        if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
        if (_vbo != 0) { gl.DeleteBuffer(_vbo); _vbo = 0; }
    }
}
