using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public sealed class GridRenderPass : IRenderPass
{
    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private int _vertexCount;

    /// <summary>Vertex count of the grid lines only (excludes the RGB axes that follow them
    /// in the buffer). Used so the grid and the axes can be drawn with different model
    /// matrices: the grid scales and re-centers to track the camera, the axes only scale.</summary>
    private int _gridVertexCount;

    /// <summary>Half-width, in local (unscaled) grid units, of the base grid geometry: matches
    /// the original fixed 200x200 grid so the on-screen size is unchanged at the default zoom.</summary>
    private const float BaseHalfSize = 100.0f;
    private const float BaseStep = 5.0f;

    public void Init(GL gl)
    {
        int lines = 40;
        float size = 200.0f;
        float step = size / lines;

        float[] vertices = new float[(lines * 4 + 4 + 6) * 6 + 8 * 6]; // grid + axes + cube
        int index = 0;
        
        // Grid lines (Gray)
        for (int i = -lines / 2; i <= lines / 2; i++)
        {
            float pos = i * step;
            // X-parallel
            vertices[index++] = -size / 2; vertices[index++] = pos; vertices[index++] = 0;
            vertices[index++] = 0.3f; vertices[index++] = 0.3f; vertices[index++] = 0.3f;
            vertices[index++] = size / 2; vertices[index++] = pos; vertices[index++] = 0;
            vertices[index++] = 0.3f; vertices[index++] = 0.3f; vertices[index++] = 0.3f;
            
            // Y-parallel
            vertices[index++] = pos; vertices[index++] = -size / 2; vertices[index++] = 0;
            vertices[index++] = 0.3f; vertices[index++] = 0.3f; vertices[index++] = 0.3f;
            vertices[index++] = pos; vertices[index++] = size / 2; vertices[index++] = 0;
            vertices[index++] = 0.3f; vertices[index++] = 0.3f; vertices[index++] = 0.3f;
        }

        _gridVertexCount = index / 6;

        // Axes (Red, Green, Blue)
        vertices[index++] = 0; vertices[index++] = 0; vertices[index++] = 0;
        vertices[index++] = 1; vertices[index++] = 0; vertices[index++] = 0;
        vertices[index++] = size/2; vertices[index++] = 0; vertices[index++] = 0;
        vertices[index++] = 1; vertices[index++] = 0; vertices[index++] = 0;

        vertices[index++] = 0; vertices[index++] = 0; vertices[index++] = 0;
        vertices[index++] = 0; vertices[index++] = 1; vertices[index++] = 0;
        vertices[index++] = 0; vertices[index++] = size/2; vertices[index++] = 0;
        vertices[index++] = 0; vertices[index++] = 1; vertices[index++] = 0;

        vertices[index++] = 0; vertices[index++] = 0; vertices[index++] = 0;
        vertices[index++] = 0; vertices[index++] = 0; vertices[index++] = 1;
        vertices[index++] = 0; vertices[index++] = 0; vertices[index++] = size/2;
        vertices[index++] = 0; vertices[index++] = 0; vertices[index++] = 1;

        _vertexCount = index / 6;

        // Cube vertices
        float s = 10.0f;
        int cubeStartIndex = index / 6;
        float[] cubeVerts = {
            -s, -s, -s,   1.0f, 0.0f, 0.0f,
             s, -s, -s,   0.0f, 1.0f, 0.0f,
             s,  s, -s,   0.0f, 0.0f, 1.0f,
            -s,  s, -s,   1.0f, 1.0f, 0.0f,
            -s, -s,  s,   1.0f, 0.0f, 1.0f,
             s, -s,  s,   0.0f, 1.0f, 1.0f,
             s,  s,  s,   1.0f, 1.0f, 1.0f,
            -s,  s,  s,   0.5f, 0.5f, 0.5f
        };
        for (int i = 0; i < cubeVerts.Length; i++) vertices[index++] = cubeVerts[i];

        // Cube lines
        uint baseIdx = (uint)cubeStartIndex;
        uint[] indices = {
            baseIdx+0, baseIdx+1, baseIdx+1, baseIdx+2, baseIdx+2, baseIdx+3, baseIdx+3, baseIdx+0,
            baseIdx+4, baseIdx+5, baseIdx+5, baseIdx+6, baseIdx+6, baseIdx+7, baseIdx+7, baseIdx+4,
            baseIdx+0, baseIdx+4, baseIdx+1, baseIdx+5, baseIdx+2, baseIdx+6, baseIdx+3, baseIdx+7
        };

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        _ebo = gl.GenBuffer();

        gl.BindVertexArray(_vao);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* v = vertices)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }
        }

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        unsafe
        {
            fixed (uint* idx = indices)
            {
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), idx, BufferUsageARB.StaticDraw);
            }
        }

        // Position attribute
        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(0);
        }

        // Color attribute
        unsafe
        {
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
        }
        
        gl.BindVertexArray(0);
    }

    public void Render(GL gl, RenderContext context)
    {
        // Fora do sketch, com uma peça isolada (H), a grade e os eixos vão para o referencial dela.
        var wp = context.ViewModel?.CurrentMode == AvaloniaCadEditor.UI.ViewModels.EditorMode.SketchEdit
            ? context.ViewModel.ActiveWorkPlane
            : context.ViewModel?.DisplayFrame?.XY ?? AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY;

        var planeMatrix = new Matrix4x4(
            (float)wp.XAxis.X, (float)wp.XAxis.Y, (float)wp.XAxis.Z, 0,
            (float)wp.YAxis.X, (float)wp.YAxis.Y, (float)wp.YAxis.Z, 0,
            (float)wp.Normal.X, (float)wp.Normal.Y, (float)wp.Normal.Z, 0,
            (float)wp.Origin.X, (float)wp.Origin.Y, (float)wp.Origin.Z, 1
        );

        // Grow/shrink the grid in powers of 10 so it always covers the visible area around the
        // camera target, whether the model is tiny or huge, without changing the vertex buffer.
        // Instead of snapping to the nearest power of 10, keep the two neighboring levels (fine
        // and coarse) and cross-fade between them by how far "level" is into the current decade,
        // so the grid spacing changes gradually with zoom instead of jumping.
        float distance = MathF.Max(context.Camera.Distance, 0.001f);
        float level = MathF.Log10(distance / BaseHalfSize);
        float levelFloor = MathF.Floor(level);
        float frac = level - levelFloor;
        float scaleFine = MathF.Pow(10f, levelFloor);
        float scaleCoarse = scaleFine * 10f;
        float alphaFine = 1f - frac;
        float alphaCoarse = frac;

        var targetLocal = wp.ToLocalSpace(new AvaloniaCadEditor.Domain.ValueObjects.Point3D(
            context.Camera.Target.X, context.Camera.Target.Y, context.Camera.Target.Z));

        gl.Uniform1(context.Uniforms.LightingLoc, 0);
        gl.BindVertexArray(_vao);

        DrawGridLevel(gl, context, planeMatrix, targetLocal, scaleFine, alphaFine);
        DrawGridLevel(gl, context, planeMatrix, targetLocal, scaleCoarse, alphaCoarse);

        // Axes are drawn once (no cross-fade needed: they read as infinite lines crossing the
        // screen regardless of scale) using the coarse level so they always reach past the edges.
        var axesMatrix = Matrix4x4.CreateScale(scaleCoarse) * planeMatrix;
        gl.Uniform1(context.Uniforms.AlphaLoc, 1.0f);
        unsafe
        {
            gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&axesMatrix);
        }
        gl.DrawArrays(PrimitiveType.Lines, _gridVertexCount, (uint)(_vertexCount - _gridVertexCount));
    }

    /// <summary>Draws just the grid lines (not the axes) at the given scale, re-centered on the
    /// camera target and snapped to that scale's own grid step, with the given fade alpha.</summary>
    private void DrawGridLevel(GL gl, RenderContext context, Matrix4x4 planeMatrix,
        AvaloniaCadEditor.Domain.ValueObjects.Point3D targetLocal, float scale, float alpha)
    {
        if (alpha <= 0f) return;

        float worldStep = BaseStep * scale;
        float tx = MathF.Round((float)targetLocal.X / worldStep) * worldStep;
        float ty = MathF.Round((float)targetLocal.Y / worldStep) * worldStep;

        var gridMatrix = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(tx, ty, 0f) * planeMatrix;

        gl.Uniform1(context.Uniforms.AlphaLoc, alpha);
        unsafe
        {
            gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&gridMatrix);
        }
        gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);
    }

    public void Dispose(GL gl)
    {
        if (_vao != 0)
        {
            gl.DeleteVertexArray(_vao);
            _vao = 0;
        }
        if (_vbo != 0)
        {
            gl.DeleteBuffer(_vbo);
            _vbo = 0;
        }
        if (_ebo != 0)
        {
            gl.DeleteBuffer(_ebo);
            _ebo = 0;
        }
    }
}
