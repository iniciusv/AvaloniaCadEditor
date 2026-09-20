using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.ViewModels;
using Silk.NET.OpenGL;
using System.Numerics;

namespace AvaloniaCadEditor.UI.Controls;

public class SilkViewportControl : OpenGlControlBase
{
    private GL? _gl;
    private CameraState _camera;

    private Point? _lastMousePos;
    private bool _isPanning = false;
    private bool _isOrbiting = false;
    
    private Vector3 _cubePos = Vector3.Zero;

    private uint _shaderProgram;
    private uint _vao;
    private uint _vbo;
    private int _vertexCount;

    public CadViewModel? ViewModel => DataContext as CadViewModel;

    public SilkViewportControl()
    {
        _camera = new CameraState();
        Focusable = true;
    }



    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);

        _gl = GL.GetApi(gl.GetProcAddress);

        _gl.ClearColor(0.15f, 0.15f, 0.15f, 1.0f);
        _gl.Disable(EnableCap.DepthTest); // Disabled to prevent issues if Avalonia didn't create a depth buffer
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        InitShaders();
        InitScene();
    }

    private void InitShaders()
    {
        string vertexShaderSource = @"
            attribute vec3 aPos;
            attribute vec3 aColor;
            
            uniform mat4 uModel;
            uniform mat4 uView;
            uniform mat4 uProjection;
            
            varying vec3 vertexColor;
            
            void main()
            {
                gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
                vertexColor = aColor;
            }";

        string fragmentShaderSource = @"
            #ifdef GL_ES
            precision mediump float;
            #endif
            
            varying vec3 vertexColor;
            
            void main()
            {
                gl_FragColor = vec4(vertexColor, 1.0);
            }";

        uint vertexShader = _gl!.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexShaderSource);
        _gl.CompileShader(vertexShader);
        string vLog = _gl.GetShaderInfoLog(vertexShader);
        if (!string.IsNullOrEmpty(vLog)) Debug.WriteLine("VERTEX SHADER ERROR: " + vLog);

        uint fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentShaderSource);
        _gl.CompileShader(fragmentShader);
        string fLog = _gl.GetShaderInfoLog(fragmentShader);
        if (!string.IsNullOrEmpty(fLog)) Debug.WriteLine("FRAGMENT SHADER ERROR: " + fLog);

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vertexShader);
        _gl.AttachShader(_shaderProgram, fragmentShader);
        
        // Bind attribute locations BEFORE linking
        _gl.BindAttribLocation(_shaderProgram, 0, "aPos");
        _gl.BindAttribLocation(_shaderProgram, 1, "aColor");
        
        _gl.LinkProgram(_shaderProgram);
        string pLog = _gl.GetProgramInfoLog(_shaderProgram);
        if (!string.IsNullOrEmpty(pLog)) Debug.WriteLine("PROGRAM LINK ERROR: " + pLog);

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);
    }

    private void InitScene()
    {
        // 1. Setup Grid
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

        int gridVertexCount = index / 6;

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

        _vao = _gl!.GenVertexArray();
        _vbo = _gl.GenBuffer();
        uint ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* v = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        unsafe
        {
            fixed (uint* idx = indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), idx, BufferUsageARB.StaticDraw);
            }
        }

        // Position attribute
        unsafe
        {
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
        }

        // Color attribute
        unsafe
        {
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
        }
        
        _gl.BindVertexArray(0);
        
        _vertexCount = gridVertexCount; // We will use _vertexCount for DrawArrays for grid
        // Cube uses DrawElements
        
        // --- Setup Sketch VAO / VBO ---
        _sketchVao = _gl.GenVertexArray();
        _sketchVbo = _gl.GenBuffer();
        
        _gl.BindVertexArray(_sketchVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _sketchVbo);
        
        unsafe
        {
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
        }
        _gl.BindVertexArray(0);
    }


    private uint _sketchVao;
    private uint _sketchVbo;

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl != null)
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteVertexArray(_sketchVao);
            _gl.DeleteBuffer(_sketchVbo);
            _gl.DeleteProgram(_shaderProgram);
            _gl.Dispose();
        }
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl == null || Bounds.Height == 0 || Bounds.Width == 0) return;

        _gl.Viewport(0, 0, (uint)Bounds.Width, (uint)Bounds.Height);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _camera.AspectRatio = (float)(Bounds.Width / Bounds.Height);
        var viewMatrix = _camera.GetViewMatrix();
        var projMatrix = _camera.GetProjectionMatrix();

        _gl.UseProgram(_shaderProgram);

        // Pass matrices to shader
        int viewLoc = _gl.GetUniformLocation(_shaderProgram, "uView");
        int projLoc = _gl.GetUniformLocation(_shaderProgram, "uProjection");
        int modelLoc = _gl.GetUniformLocation(_shaderProgram, "uModel");
        
        unsafe
        {
            _gl.UniformMatrix4(viewLoc, 1, false, (float*)&viewMatrix);
            _gl.UniformMatrix4(projLoc, 1, false, (float*)&projMatrix);
        }

        _gl.BindVertexArray(_vao);

        // Draw Grid (Model matrix = Identity)
        var identity = Matrix4x4.Identity;
        unsafe
        {
            _gl.UniformMatrix4(modelLoc, 1, false, (float*)&identity);
        }
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_vertexCount);

        // --- Render Sketch Entities ---
        if (ViewModel != null)
        {
            var sketchVertices = new System.Collections.Generic.List<float>();

            void AddLine(Point3D p1, Point3D p2, float r, float g, float b)
            {
                sketchVertices.Add((float)p1.X); sketchVertices.Add((float)p1.Y); sketchVertices.Add((float)p1.Z);
                sketchVertices.Add(r); sketchVertices.Add(g); sketchVertices.Add(b);
                sketchVertices.Add((float)p2.X); sketchVertices.Add((float)p2.Y); sketchVertices.Add((float)p2.Z);
                sketchVertices.Add(r); sketchVertices.Add(g); sketchVertices.Add(b);
            }

            // Draw finalized entities
            foreach (var entity in ViewModel.Sketch.Entities)
            {
                float r = entity.IsSelected ? 1.0f : 1.0f;
                float g = entity.IsSelected ? 0.5f : 1.0f;
                float b = entity.IsSelected ? 0.0f : 1.0f; // Selected = Orange, Normal = White

                if (entity is LineEntity line)
                {
                    AddLine(line.StartPoint, line.EndPoint, r, g, b);
                }
                else if (entity is CircleEntity circle)
                {
                    int segments = 32;
                    for (int i = 0; i < segments; i++)
                    {
                        double theta1 = 2.0 * Math.PI * i / segments;
                        double theta2 = 2.0 * Math.PI * (i + 1) / segments;
                        
                        Point3D p1 = new Point3D(circle.Center.X + circle.Radius * Math.Cos(theta1), circle.Center.Y + circle.Radius * Math.Sin(theta1), circle.Center.Z);
                        Point3D p2 = new Point3D(circle.Center.X + circle.Radius * Math.Cos(theta2), circle.Center.Y + circle.Radius * Math.Sin(theta2), circle.Center.Z);
                        
                        AddLine(p1, p2, r, g, b);
                    }
                }
            }

            // Draw preview entities (ghost lines)
            if (ViewModel.CurrentTool != null)
            {
                foreach (var entity in ViewModel.CurrentTool.GetPreviewEntities())
                {
                    if (entity is LineEntity line)
                    {
                        AddLine(line.StartPoint, line.EndPoint, 1.0f, 1.0f, 0.0f); // Preview = Yellow
                    }
                    else if (entity is CircleEntity circle)
                    {
                        int segments = 32;
                        for (int i = 0; i < segments; i++)
                        {
                            double theta1 = 2.0 * Math.PI * i / segments;
                            double theta2 = 2.0 * Math.PI * (i + 1) / segments;
                            
                            Point3D p1 = new Point3D(circle.Center.X + circle.Radius * Math.Cos(theta1), circle.Center.Y + circle.Radius * Math.Sin(theta1), circle.Center.Z);
                            Point3D p2 = new Point3D(circle.Center.X + circle.Radius * Math.Cos(theta2), circle.Center.Y + circle.Radius * Math.Sin(theta2), circle.Center.Z);
                            
                            AddLine(p1, p2, 1.0f, 1.0f, 0.0f);
                        }
                    }
                }
            }

            if (sketchVertices.Count > 0)
            {
                _gl.BindVertexArray(_sketchVao);
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _sketchVbo);

                unsafe
                {
                    var arr = sketchVertices.ToArray();
                    fixed (float* ptr = arr)
                    {
                        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(float)), ptr, BufferUsageARB.DynamicDraw);
                    }
                }

                unsafe
                {
                    _gl.UniformMatrix4(modelLoc, 1, false, (float*)&identity); // Reset model to Identity
                }
                
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(sketchVertices.Count / 6));
            }
        }
    }

    public void HandlePointerWheelChanged(PointerWheelEventArgs e)
    {
        Debug.WriteLine($"[DEBUG] Mouse Wheel: {e.Delta.Y}");
        
        float zoomFactor = 1.1f;
        if (e.Delta.Y > 0)
            _camera.Zoom(1.0f / zoomFactor);
        else
            _camera.Zoom(zoomFactor);

        RequestNextFrameRendering();
    }

    public void HandlePointerPressed(PointerPressedEventArgs e)
    {
        this.Focus();
        var point = e.GetCurrentPoint(this);
        
        Debug.WriteLine($"[DEBUG] Mouse Pressed: {point.Properties.PointerUpdateKind}");

        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _isPanning = true;
                Debug.WriteLine("[DEBUG] Started Panning");
            }
            else
            {
                _isOrbiting = true;
                Debug.WriteLine("[DEBUG] Started Orbiting");
            }
            _lastMousePos = point.Position;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            Debug.WriteLine("[DEBUG] Left Click");
            if (Bounds.Width > 0 && Bounds.Height > 0)
            {
                var ray = _camera.ScreenPointToRay(
                    new Vector2((float)point.Position.X, (float)point.Position.Y), 
                    new Vector2((float)Bounds.Width, (float)Bounds.Height)
                );
                var intersection = ray.IntersectPlane(Vector3.UnitZ, Vector3.Zero);
                if (intersection.HasValue)
                {
                    ViewModel?.OnPointerPressed(intersection.Value);
                    RequestNextFrameRendering();
                }
            }
        }
    }

    public void HandlePointerMoved(PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(this);

        if (_lastMousePos.HasValue)
        {
            float deltaX = (float)(point.Position.X - _lastMousePos.Value.X);
            float deltaY = (float)(point.Position.Y - _lastMousePos.Value.Y);

            if (_isOrbiting)
            {
                Debug.WriteLine($"[DEBUG] Orbiting delta: {deltaX}, {deltaY}");
                float sens = 0.01f;
                _camera.Orbit(deltaX * sens, deltaY * sens);
            }
            else if (_isPanning)
            {
                Debug.WriteLine($"[DEBUG] Panning delta: {deltaX}, {deltaY}");
                _camera.Pan(deltaX, deltaY);
            }
        }
        
        // --- Raycasting ---
        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            var ray = _camera.ScreenPointToRay(
                new Vector2((float)point.Position.X, (float)point.Position.Y), 
                new Vector2((float)Bounds.Width, (float)Bounds.Height)
            );

            // Intersect with default XY plane (Z=0)
            var intersection = ray.IntersectPlane(Vector3.UnitZ, Vector3.Zero);
            if (intersection.HasValue)
            {
                _cubePos = new Vector3((float)intersection.Value.X, (float)intersection.Value.Y, (float)intersection.Value.Z);
                ViewModel?.OnPointerMoved(intersection.Value);
            }
        }

        RequestNextFrameRendering();
        _lastMousePos = point.Position;
    }

    public void HandlePointerReleased(PointerReleasedEventArgs e)
    {
        Debug.WriteLine($"[DEBUG] Mouse Released: {e.InitialPressMouseButton}");
        if (e.InitialPressMouseButton == MouseButton.Middle || e.InitialPressMouseButton == MouseButton.Right)
        {
            _isPanning = false;
            _isOrbiting = false;
            _lastMousePos = null;
        }
    }
}
