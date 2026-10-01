using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public sealed class SketchEntityRenderPass : IRenderPass
{
    private uint _sketchVao;
    private uint _sketchVbo;

    public void Init(GL gl)
    {
        _sketchVao = gl.GenVertexArray();
        _sketchVbo = gl.GenBuffer();

        gl.BindVertexArray(_sketchVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _sketchVbo);

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

        var sketchVertices = new List<float>();

        void AddLine(Point3D p1, Point3D p2, WorkPlane plane, float r, float g, float b)
        {
            var w1 = plane.ToWorldSpace(p1);
            var w2 = plane.ToWorldSpace(p2);
            sketchVertices.Add((float)w1.X); sketchVertices.Add((float)w1.Y); sketchVertices.Add((float)w1.Z);
            sketchVertices.Add(r); sketchVertices.Add(g); sketchVertices.Add(b);
            sketchVertices.Add((float)w2.X); sketchVertices.Add((float)w2.Y); sketchVertices.Add((float)w2.Z);
            sketchVertices.Add(r); sketchVertices.Add(g); sketchVertices.Add(b);
        }

        // Draw finalized entities
        foreach (var entity in vm.Sketch.Entities)
        {
            if (!entity.IsVisible) continue;
            float r = entity.IsSelected ? 1.0f : 1.0f;
            float g = entity.IsSelected ? 0.5f : 1.0f;
            float b = entity.IsSelected ? 0.0f : 1.0f; // Selected = Orange, Normal = White

            if (entity is LineEntity line)
            {
                AddLine(line.StartPoint, line.EndPoint, entity.Plane, r, g, b);
            }
            else if (entity is CircleEntity circle)
            {
                int segments = vm.ProfileSegments;
                for (int i = 0; i < segments; i++)
                {
                    double theta1 = 2.0 * Math.PI * i / segments;
                    double theta2 = 2.0 * Math.PI * (i + 1) / segments;
                    
                    Point3D p1 = new Point3D(circle.Center.X + circle.Radius * Math.Cos(theta1), circle.Center.Y + circle.Radius * Math.Sin(theta1), circle.Center.Z);
                    Point3D p2 = new Point3D(circle.Center.X + circle.Radius * Math.Cos(theta2), circle.Center.Y + circle.Radius * Math.Sin(theta2), circle.Center.Z);
                    
                    AddLine(p1, p2, entity.Plane, r, g, b);
                }
            }
        }

        // Draw preview entities (ghost lines)
        if (vm.CurrentTool != null)
        {
            foreach (var entity in vm.CurrentTool.GetPreviewEntities())
            {
                // Preview entities should respect their own Plane, fallback to ActiveWorkPlane
                var entityPlane = entity.Plane ?? vm.ActiveWorkPlane;
                
                if (entity is LineEntity line)
                {
                    float pr = 1.0f, pg = 1.0f, pb = 0.0f;
                    if (vm.CurrentTool is Application.Commands.ExtrudeCommand extCmd)
                    {
                        if (extCmd.CurrentHeight < 0)
                        {
                            pr = 1.0f; pg = 0.2f; pb = 0.2f; // Red for subtract
                        }
                        else
                        {
                            pr = 0.2f; pg = 1.0f; pb = 0.2f; // Green for union
                        }
                    }
                    AddLine(line.StartPoint, line.EndPoint, entityPlane, pr, pg, pb);
                }
                else if (entity is CircleEntity circle)
                {
                    int segments = vm.ProfileSegments;
                    for (int i = 0; i < segments; i++)
                    {
                        double theta1 = 2.0 * Math.PI * i / segments;
                        double theta2 = 2.0 * Math.PI * (i + 1) / segments;
                        
                        Point3D p1 = new Point3D(circle.Center.X + circle.Radius * Math.Cos(theta1), circle.Center.Y + circle.Radius * Math.Sin(theta1), circle.Center.Z);
                        Point3D p2 = new Point3D(circle.Center.X + circle.Radius * Math.Cos(theta2), circle.Center.Y + circle.Radius * Math.Sin(theta2), circle.Center.Z);
                        
                        AddLine(p1, p2, entityPlane, 1.0f, 1.0f, 0.0f);
                    }
                }
            }
        }

        // Draw Snap Indicator if in Drawing Mode
        if (vm.IsDrawingMode)
        {
            var snapLocal = vm.CurrentSnappedPoint;
            float s = 2.0f; // size of the snap indicator cross
            float pr = 1.0f, pg = 0.5f, pb = 0.0f; // Orange snap indicator
            
            // Vertical line of the cross
            var p1 = vm.ActiveWorkPlane.ToWorldSpace(new Point3D(snapLocal.X, snapLocal.Y - s, snapLocal.Z));
            var p2 = vm.ActiveWorkPlane.ToWorldSpace(new Point3D(snapLocal.X, snapLocal.Y + s, snapLocal.Z));
            sketchVertices.Add((float)p1.X); sketchVertices.Add((float)p1.Y); sketchVertices.Add((float)p1.Z);
            sketchVertices.Add(pr); sketchVertices.Add(pg); sketchVertices.Add(pb);
            sketchVertices.Add((float)p2.X); sketchVertices.Add((float)p2.Y); sketchVertices.Add((float)p2.Z);
            sketchVertices.Add(pr); sketchVertices.Add(pg); sketchVertices.Add(pb);
            
            // Horizontal line of the cross
            var p3 = vm.ActiveWorkPlane.ToWorldSpace(new Point3D(snapLocal.X - s, snapLocal.Y, snapLocal.Z));
            var p4 = vm.ActiveWorkPlane.ToWorldSpace(new Point3D(snapLocal.X + s, snapLocal.Y, snapLocal.Z));
            sketchVertices.Add((float)p3.X); sketchVertices.Add((float)p3.Y); sketchVertices.Add((float)p3.Z);
            sketchVertices.Add(pr); sketchVertices.Add(pg); sketchVertices.Add(pb);
            sketchVertices.Add((float)p4.X); sketchVertices.Add((float)p4.Y); sketchVertices.Add((float)p4.Z);
            sketchVertices.Add(pr); sketchVertices.Add(pg); sketchVertices.Add(pb);
        }

        if (sketchVertices.Count > 0)
        {
            gl.BindVertexArray(_sketchVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _sketchVbo);

            unsafe
            {
                var arr = sketchVertices.ToArray();
                fixed (float* ptr = arr)
                {
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(float)), ptr, BufferUsageARB.DynamicDraw);
                }
            }

            var identity = context.IdentityMatrix;
            unsafe
            {
                gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&identity);
                gl.Uniform1(context.Uniforms.LightingLoc, 0);
            }
            
            // Depth test ligado: o sketch (inclusive selecionado) não aparece atrás de sólidos.
            // GL_POLYGON_OFFSET_LINE não afeta primitivas GL_LINES (só polígonos em modo wireframe),
            // então para evitar z-fighting com geometria coplanar (grade do plano, faces de sólidos)
            // aplicamos um leve viés de profundidade no vertex shader (uDepthBias): isso aproxima
            // o sketch da câmera o suficiente para vencer empates de profundidade, sem deixar de
            // respeitar sólidos genuinamente à frente. (glDepthRange não é usado aqui porque o
            // contexto ANGLE/GLES do Avalonia no Windows não expõe esse símbolo nativo.)
            gl.Uniform1(context.Uniforms.DepthBiasLoc, 0.0005f);
            gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(sketchVertices.Count / 6));
            gl.Uniform1(context.Uniforms.DepthBiasLoc, 0.0f);
        }
    }

    public void Dispose(GL gl)
    {
        if (_sketchVao != 0) { gl.DeleteVertexArray(_sketchVao); _sketchVao = 0; }
        if (_sketchVbo != 0) { gl.DeleteBuffer(_sketchVbo); _sketchVbo = 0; }
    }
}
