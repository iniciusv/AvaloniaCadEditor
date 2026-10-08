using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ReferenceGuides;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.Controls.Rendering;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.ReferenceGuides;

/// <summary>
/// Draws the reference guide lines and dimension connectors produced by the last
/// <see cref="ReferenceGuidesViewModel"/> analysis. Self-contained: it only reads
/// <c>context.ViewModel.ReferenceGuides</c>, so it can be dropped into or removed from
/// <see cref="AvaloniaCadEditor.UI.Controls.SilkViewportControl"/> with a single line.
///
/// The cached <see cref="ReferenceGuideSet"/> is turned into a vertex list on every frame
/// (like <see cref="SketchEntityRenderPass"/> already does for sketch entities) - cheap,
/// since a scene has at most a few dozen guides, and it avoids any buffer-versioning logic.
/// The expensive part, the scene analysis itself, only runs when the user asks for it.
/// </summary>
public sealed class ReferenceGuideRenderPass : IRenderPass
{
    private uint _vao;
    private uint _vbo;

    private static readonly double[] DashPattern = { 1.0, 0.6 };
    private static readonly double[] DashDotPattern = { 1.4, 0.5, 0.2, 0.5 };

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
        var guidesVm = context.ViewModel?.ReferenceGuides;
        var result = guidesVm?.Result;
        if (guidesVm == null || !guidesVm.IsVisible || result == null) return;
        if (result.Lines.Count == 0 && result.Dimensions.Count == 0) return;

        var selectedId = (context.ViewModel?.SelectionService?.SelectedObject as IFeature)?.Id;

        var highlighted = new List<float>();
        var dimmed = new List<float>();

        foreach (var line in result.Lines)
        {
            var target = TargetFor(line.SourceFeatureIds, line.OwnerIds, selectedId, highlighted, dimmed);
            var color = ColorFor(line.Kind);
            var pattern = line.Kind == GuideKind.Symmetry ? DashDotPattern : DashPattern;
            AddDashedSegment(target, line.Start, line.End, color, pattern);
        }

        foreach (var dimension in result.Dimensions)
        {
            var target = TargetFor(dimension.SourceFeatureIds, dimension.OwnerIds, selectedId, highlighted, dimmed);
            AddSegment(target, dimension.From, dimension.To, ColorFor(dimension.Kind));
        }

        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        var identity = context.IdentityMatrix;
        unsafe
        {
            gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&identity);
        }
        gl.Uniform1(context.Uniforms.LightingLoc, 0);
        gl.Disable(EnableCap.DepthTest);

        DrawBatch(gl, context, highlighted, alpha: 1.0f);
        DrawBatch(gl, context, dimmed, alpha: 0.25f);

        gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose(GL gl)
    {
        if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
        if (_vbo != 0) { gl.DeleteBuffer(_vbo); _vbo = 0; }
    }

    /// <summary>Destaca o que pertence à feature selecionada: uma primitiva solta pelo próprio Id, um
    /// furo dentro de uma peça pelo Id da instância (o dono de topo).</summary>
    private static List<float> TargetFor(IReadOnlyList<Guid> sourceIds, IReadOnlyList<Guid>? ownerIds, Guid? selectedId, List<float> highlighted, List<float> dimmed)
    {
        if (selectedId == null || sourceIds.Contains(selectedId.Value) || ownerIds?.Contains(selectedId.Value) == true) return highlighted;
        return dimmed;
    }

    private void DrawBatch(GL gl, RenderContext context, List<float> vertices, float alpha)
    {
        if (vertices.Count == 0) return;

        gl.Uniform1(context.Uniforms.AlphaLoc, alpha);
        unsafe
        {
            var arr = vertices.ToArray();
            fixed (float* ptr = arr)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(float)), ptr, BufferUsageARB.DynamicDraw);
            }
        }
        gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertices.Count / 6));
    }

    private static (float R, float G, float B) ColorFor(GuideKind kind) => kind switch
    {
        GuideKind.HoleRow => (0.2f, 0.9f, 0.95f),
        GuideKind.Wall => (1.0f, 0.6f, 0.0f),
        GuideKind.ArcCenter => (1.0f, 0.9f, 0.1f),
        GuideKind.Symmetry => (0.3f, 0.95f, 0.3f),
        GuideKind.NearMiss => (1.0f, 0.2f, 0.2f),
        _ => (1.0f, 1.0f, 1.0f)
    };

    private static void AddSegment(List<float> target, Point3D a, Point3D b, (float R, float G, float B) color)
    {
        target.Add((float)a.X); target.Add((float)a.Y); target.Add((float)a.Z);
        target.Add(color.R); target.Add(color.G); target.Add(color.B);
        target.Add((float)b.X); target.Add((float)b.Y); target.Add((float)b.Z);
        target.Add(color.R); target.Add(color.G); target.Add(color.B);
    }

    /// <summary>Splits a segment into dash/gap pieces of world-space fixed length. Only the
    /// "on" pieces of <paramref name="pattern"/> (even indices) are actually added as line
    /// vertices; <paramref name="pattern"/> must have an even length.</summary>
    private static void AddDashedSegment(List<float> target, Point3D a, Point3D b, (float R, float G, float B) color, double[] pattern)
    {
        var delta = b - a;
        double total = delta.Length;
        if (total < 1e-9) return;
        var unit = delta.Normalize();

        double pos = 0;
        int patternIndex = 0;
        bool draw = true;
        while (pos < total)
        {
            double stepLen = Math.Min(pattern[patternIndex % pattern.Length], total - pos);
            if (draw && stepLen > 1e-6)
            {
                var p1 = a + unit * pos;
                var p2 = a + unit * (pos + stepLen);
                AddSegment(target, p1, p2, color);
            }
            pos += stepLen;
            patternIndex++;
            draw = !draw;
        }
    }
}
