using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

/// <summary>
/// Desenha a geometria de referência do plano de trabalho perto do cursor
/// (<c>CadViewModel.NearbyPlaneReferences</c>): em ciano o contorno das faces que estão no plano e em
/// magenta o corte dos sólidos que o atravessam. Só acende o que o snap pode pegar enquanto se desenha —
/// todas as referências o tempo todo poluíam a tela. Cores tênues, e as linhas do esboço vêm depois, por cima. Depth test
/// desligado como no <see cref="ToolGuideRenderPass"/>: o corte fica dentro do sólido, e com depth ligado
/// a própria casca o esconderia.
/// </summary>
public sealed class PlaneReferenceRenderPass : IRenderPass
{
    private static readonly (float R, float G, float B) CoplanarColor = (0.35f, 0.75f, 0.85f);
    private static readonly (float R, float G, float B) SectionColor = (0.85f, 0.45f, 0.75f);

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
        var vm = context.ViewModel;
        if (vm == null || vm.CurrentMode != AvaloniaCadEditor.UI.ViewModels.EditorMode.SketchEdit) return;

        var references = vm.NearbyPlaneReferences;
        if (references.Count == 0) return;

        var plane = vm.ActiveWorkPlane;
        var vertices = new List<float>();

        void Add(Point3D local, (float R, float G, float B) c)
        {
            var w = plane.ToWorldSpace(local);
            vertices.Add((float)w.X); vertices.Add((float)w.Y); vertices.Add((float)w.Z);
            vertices.Add(c.R); vertices.Add(c.G); vertices.Add(c.B);
        }

        foreach (var reference in references)
        {
            var color = reference.Kind == PlaneReferenceKind.Coplanar ? CoplanarColor : SectionColor;
            var pts = reference.Points;
            int segments = reference.Closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Add(pts[i], color);
                Add(pts[(i + 1) % pts.Count], color);
            }
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
