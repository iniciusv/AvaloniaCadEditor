using System.Numerics;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public sealed class GridRenderPass : IRenderPass
{
    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private int _vertexCount;

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
        var gridMatrix = context.IdentityMatrix;
        if (context.ViewModel != null)
        {
            var wp = context.ViewModel.ActiveWorkPlane;
            gridMatrix = new Matrix4x4(
                (float)wp.XAxis.X, (float)wp.XAxis.Y, (float)wp.XAxis.Z, 0,
                (float)wp.YAxis.X, (float)wp.YAxis.Y, (float)wp.YAxis.Z, 0,
                (float)wp.Normal.X, (float)wp.Normal.Y, (float)wp.Normal.Z, 0,
                (float)wp.Origin.X, (float)wp.Origin.Y, (float)wp.Origin.Z, 1
            );
        }

        unsafe
        {
            gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&gridMatrix);
            gl.Uniform1(context.Uniforms.LightingLoc, 0); 
            gl.Uniform1(context.Uniforms.AlphaLoc, 1.0f);
        }

        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_vertexCount);
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
