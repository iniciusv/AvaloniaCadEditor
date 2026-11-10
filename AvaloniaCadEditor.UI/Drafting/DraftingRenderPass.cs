using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.UI.Controls.Rendering;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Drafting;

/// <summary>
/// Desenha as linhas das cotas (chamadas, linha de cota, setas), as linhas de centro e, no modo
/// "Cotar", os pontos que podem ser escolhidos. Os textos ficam no <see cref="DraftingLabelsOverlay"/>
/// (a camada GL não desenha texto). Só lê <c>context.ViewModel.Drafting</c>; como o pass das guias
/// de referência, refaz a lista de vértices a cada quadro — são poucas dezenas de cotas.
/// </summary>
public sealed class DraftingRenderPass : IRenderPass
{
    private uint _vao;
    private uint _vbo;

    private static readonly (float R, float G, float B) AutoColor = (0.55f, 0.85f, 1.0f);
    private static readonly (float R, float G, float B) ManualColor = (1.0f, 0.85f, 0.2f);
    private static readonly (float R, float G, float B) CenterColor = (0.45f, 0.9f, 0.45f);
    private static readonly (float R, float G, float B) AnchorColor = (0.85f, 0.85f, 0.85f);
    private static readonly (float R, float G, float B) PendingColor = (1.0f, 0.5f, 0.1f);
    private static readonly (float R, float G, float B) SelectedColor = (1.0f, 0.6f, 0.15f);
    private static readonly (float R, float G, float B) MeasuredColor = (0.25f, 1.0f, 0.95f);
    private static readonly (float R, float G, float B) AffectedColor = (1.0f, 0.35f, 0.85f);
    private static readonly (float R, float G, float B) GhostColor = (1.0f, 0.75f, 0.95f);

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
        var drafting = context.ViewModel?.Drafting;
        if (drafting?.Sheet is not { } sheet || !drafting.IsShowing(context.Camera)) return;

        var vertices = new List<float>();
        double arrow = drafting.ArrowSize;

        var levers = drafting.Levers;
        foreach (var dimension in drafting.Dimensions)
        {
            var color = dimension.Key == levers.Selected?.Key ? SelectedColor
                : dimension.IsManual ? ManualColor : AutoColor;
            foreach (var (a, b) in DimensionGraphics.Build(dimension, arrow).Segments)
            {
                AddSegment(vertices, drafting.ToWorld(a), drafting.ToWorld(b), color);
            }
        }

        double dash = sheet.Gap * 0.5;
        var pattern = new[] { dash, dash * 0.3, dash * 0.08, dash * 0.3 };
        foreach (var line in sheet.CenterLines)
        {
            AddDashed(vertices, drafting.ToWorld(line.A), drafting.ToWorld(line.B), CenterColor, pattern);
        }

        if (levers.HasSelection)
        {
            // O que a opção move por fora, o que a cota mede por dentro: um elemento pode ser os dois.
            AddOutlines(vertices, drafting, sheet.Entities, levers.AffectedSources, sheet.Gap * 0.16, AffectedColor);
            AddOutlines(vertices, drafting, sheet.Entities, levers.MeasuredSources, sheet.Gap * 0.06, MeasuredColor);

            // A prévia do valor digitado: o que se mexe, tracejado, já no lugar novo.
            var ghost = levers.GhostEntities;
            if (ghost.Count > 0)
            {
                double ghostDash = sheet.Gap * 0.22;
                AddOutlines(vertices, drafting, ghost, ghost.Select(e => e.SourceName).ToList(), 0, GhostColor,
                    dashes: [ghostDash, ghostDash * 0.6]);
            }
        }

        if (drafting.IsPicking)
        {
            double s = sheet.Gap * 0.1;
            foreach (var anchor in sheet.Anchors)
            {
                AddCross(vertices, drafting, anchor.Point, s, AnchorColor);
            }
            foreach (var pending in new[] { drafting.PendingFirst, drafting.PendingSecond })
            {
                if (pending != null) AddSquare(vertices, drafting, pending.Point, s * 2.5, PendingColor);
            }
        }

        if (vertices.Count == 0) return;

        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var identity = context.IdentityMatrix;
        unsafe
        {
            gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&identity);
            var arr = vertices.ToArray();
            fixed (float* ptr = arr)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(float)), ptr, BufferUsageARB.DynamicDraw);
            }
        }
        gl.Uniform1(context.Uniforms.LightingLoc, 0);
        gl.Uniform1(context.Uniforms.AlphaLoc, 1.0f);
        gl.Disable(EnableCap.DepthTest);
        gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertices.Count / 6));
        gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose(GL gl)
    {
        if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
        if (_vbo != 0) { gl.DeleteBuffer(_vbo); _vbo = 0; }
    }

    /// <summary>Contorno das primitivas <paramref name="names"/>, afastado <paramref name="inflate"/> mm para
    /// fora, para não se confundir com as arestas da peça. Com <paramref name="dashes"/>, tracejado.</summary>
    private static void AddOutlines(List<float> target, DraftingViewModel drafting, IReadOnlyList<ViewEntity> entities,
        IReadOnlyList<string> names, double inflate, (float, float, float) color, double[]? dashes = null)
    {
        if (names.Count == 0) return;
        var wanted = new HashSet<string>(names, StringComparer.Ordinal);

        void AddSegment(List<float> into, Point3D a, Point3D b, (float, float, float) c)
        {
            if (dashes == null) DraftingRenderPass.AddSegment(into, a, b, c);
            else AddDashed(into, a, b, c, dashes);
        }

        foreach (var entity in entities)
        {
            if (!wanted.Contains(entity.SourceName)) continue;
            switch (entity)
            {
                case ViewCircle circle:
                {
                    const int segments = 48;
                    double r = circle.Radius + inflate;
                    for (int i = 0; i < segments; i++)
                    {
                        double a0 = 2 * Math.PI * i / segments, a1 = 2 * Math.PI * (i + 1) / segments;
                        AddSegment(target,
                            drafting.ToWorld(new P2(circle.Center.U + r * Math.Cos(a0), circle.Center.V + r * Math.Sin(a0))),
                            drafting.ToWorld(new P2(circle.Center.U + r * Math.Cos(a1), circle.Center.V + r * Math.Sin(a1))),
                            color);
                    }
                    break;
                }
                case ViewPolygon polygon:
                    for (int i = 0; i < polygon.Points.Count; i++)
                    {
                        AddSegment(target, drafting.ToWorld(polygon.Points[i]), drafting.ToWorld(polygon.Points[(i + 1) % polygon.Points.Count]), color);
                    }
                    break;
                default:
                {
                    var min = new P2(entity.Min.U - inflate, entity.Min.V - inflate);
                    var max = new P2(entity.Max.U + inflate, entity.Max.V + inflate);
                    var p1 = drafting.ToWorld(min);
                    var p2 = drafting.ToWorld(new P2(max.U, min.V));
                    var p3 = drafting.ToWorld(max);
                    var p4 = drafting.ToWorld(new P2(min.U, max.V));
                    AddSegment(target, p1, p2, color);
                    AddSegment(target, p2, p3, color);
                    AddSegment(target, p3, p4, color);
                    AddSegment(target, p4, p1, color);
                    break;
                }
            }
        }
    }

    private static void AddCross(List<float> target, DraftingViewModel drafting, P2 c, double s, (float, float, float) color)
    {
        AddSegment(target, drafting.ToWorld(new P2(c.U - s, c.V)), drafting.ToWorld(new P2(c.U + s, c.V)), color);
        AddSegment(target, drafting.ToWorld(new P2(c.U, c.V - s)), drafting.ToWorld(new P2(c.U, c.V + s)), color);
    }

    private static void AddSquare(List<float> target, DraftingViewModel drafting, P2 c, double s, (float, float, float) color)
    {
        var p1 = drafting.ToWorld(new P2(c.U - s, c.V - s));
        var p2 = drafting.ToWorld(new P2(c.U + s, c.V - s));
        var p3 = drafting.ToWorld(new P2(c.U + s, c.V + s));
        var p4 = drafting.ToWorld(new P2(c.U - s, c.V + s));
        AddSegment(target, p1, p2, color);
        AddSegment(target, p2, p3, color);
        AddSegment(target, p3, p4, color);
        AddSegment(target, p4, p1, color);
    }

    private static void AddSegment(List<float> target, Point3D a, Point3D b, (float R, float G, float B) color)
    {
        target.Add((float)a.X); target.Add((float)a.Y); target.Add((float)a.Z);
        target.Add(color.R); target.Add(color.G); target.Add(color.B);
        target.Add((float)b.X); target.Add((float)b.Y); target.Add((float)b.Z);
        target.Add(color.R); target.Add(color.G); target.Add(color.B);
    }

    /// <summary>Traço-ponto: só os trechos de índice par do padrão são desenhados.</summary>
    private static void AddDashed(List<float> target, Point3D a, Point3D b, (float R, float G, float B) color, double[] pattern)
    {
        var delta = b - a;
        double total = delta.Length;
        if (total < 1e-9) return;
        var unit = delta.Normalize();

        double pos = 0;
        int index = 0;
        while (pos < total)
        {
            double step = Math.Min(pattern[index % pattern.Length], total - pos);
            if (index % 2 == 0 && step > 1e-6)
            {
                AddSegment(target, a + unit * pos, a + unit * (pos + step), color);
            }
            pos += step;
            index++;
        }
    }
}
