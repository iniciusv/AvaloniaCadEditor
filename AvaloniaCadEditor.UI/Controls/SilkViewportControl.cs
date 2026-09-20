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
using System.Linq;

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
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
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
            attribute vec3 aNormal;
            
            uniform mat4 uModel;
            uniform mat4 uView;
            uniform mat4 uProjection;
            uniform int uUseLighting;
            
            varying vec3 vertexColor;
            
            void main()
            {
                gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
                
                if (uUseLighting == 1) {
                    vec3 lightDir = normalize(vec3(0.5, 0.8, 1.0));
                    vec3 norm = normalize(mat3(uModel) * aNormal);
                    float diff = max(dot(norm, lightDir), 0.0);
                    float ambient = 0.40;
                    float light = ambient + (1.0 - ambient) * diff;
                    vertexColor = aColor * light;
                } else {
                    vertexColor = aColor;
                }
            }";

        string fragmentShaderSource = @"
            #ifdef GL_ES
            precision mediump float;
            #endif
            
            uniform float uAlpha;
            varying vec3 vertexColor;
            
            void main()
            {
                gl_FragColor = vec4(vertexColor, uAlpha);
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
        _gl.BindAttribLocation(_shaderProgram, 2, "aNormal");
        
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

        // --- Setup Solid Mesh VAO / VBO / EBO ---
        _solidVao = _gl.GenVertexArray();
        _solidVbo = _gl.GenBuffer();
        _solidEbo = _gl.GenBuffer();

        _gl.BindVertexArray(_solidVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _solidEbo);

        unsafe
        {
            // Attribute 0: Position (vec3), Attribute 1: Color (vec3), Attribute 2: Normal (vec3) -> 9 floats stride
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
        }
        _gl.BindVertexArray(0);

        // --- Setup Solid Edges VAO / VBO ---
        _solidEdgesVao = _gl.GenVertexArray();
        _solidEdgesVbo = _gl.GenBuffer();

        _gl.BindVertexArray(_solidEdgesVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidEdgesVbo);

        unsafe
        {
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
        }
        _gl.BindVertexArray(0);

        // --- Setup 2D Profile Face Shading VAO / VBO / EBO ---
        _profileFaceVao = _gl.GenVertexArray();
        _profileFaceVbo = _gl.GenBuffer();
        _profileFaceEbo = _gl.GenBuffer();

        _gl.BindVertexArray(_profileFaceVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _profileFaceVbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _profileFaceEbo);

        unsafe
        {
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
        }
        _gl.BindVertexArray(0);
    }

    private uint _sketchVao;
    private uint _sketchVbo;

    private uint _solidVao;
    private uint _solidVbo;
    private uint _solidEbo;

    private uint _solidEdgesVao;
    private uint _solidEdgesVbo;

    private uint _profileFaceVao;
    private uint _profileFaceVbo;
    private uint _profileFaceEbo;

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl != null)
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteVertexArray(_sketchVao);
            _gl.DeleteBuffer(_sketchVbo);
            _gl.DeleteVertexArray(_solidVao);
            _gl.DeleteBuffer(_solidVbo);
            _gl.DeleteBuffer(_solidEbo);
            _gl.DeleteVertexArray(_solidEdgesVao);
            _gl.DeleteBuffer(_solidEdgesVbo);
            _gl.DeleteVertexArray(_profileFaceVao);
            _gl.DeleteBuffer(_profileFaceVbo);
            _gl.DeleteBuffer(_profileFaceEbo);
            _gl.DeleteProgram(_shaderProgram);
            _gl.Dispose();
        }
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl == null || Bounds.Height == 0 || Bounds.Width == 0) return;

        _gl.Viewport(0, 0, (uint)Bounds.Width, (uint)Bounds.Height);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        _camera.AspectRatio = (float)(Bounds.Width / Bounds.Height);
        var viewMatrix = _camera.GetViewMatrix();
        var projMatrix = _camera.GetProjectionMatrix();

        _gl.UseProgram(_shaderProgram);

        // Pass matrices and uniforms to shader
        int viewLoc = _gl.GetUniformLocation(_shaderProgram, "uView");
        int projLoc = _gl.GetUniformLocation(_shaderProgram, "uProjection");
        int modelLoc = _gl.GetUniformLocation(_shaderProgram, "uModel");
        int lightingLoc = _gl.GetUniformLocation(_shaderProgram, "uUseLighting");
        int alphaLoc = _gl.GetUniformLocation(_shaderProgram, "uAlpha");
        
        unsafe
        {
            _gl.UniformMatrix4(viewLoc, 1, false, (float*)&viewMatrix);
            _gl.UniformMatrix4(projLoc, 1, false, (float*)&projMatrix);
        }

        var identity = Matrix4x4.Identity;
        unsafe
        {
            _gl.UniformMatrix4(modelLoc, 1, false, (float*)&identity);
            _gl.Uniform1(lightingLoc, 0); // No lighting for grid and 2D lines
            _gl.Uniform1(alphaLoc, 1.0f);
        }

        // Draw Grid
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_vertexCount);

        // --- Render 2D Profiles Face Shading (Subtle Shading, Hover Pre-highlight, Selection) ---
        if (ViewModel != null && ViewModel.Sketch.Profiles.Count > 0)
        {
            foreach (var profile in ViewModel.Sketch.Profiles)
            {
                var (faceVerts, faceIndices) = profile.GetTessellatedFace();
                if (faceVerts.Count == 0 || faceIndices.Count == 0) continue;

                float r = 0.38f, g = 0.55f, b = 0.80f, a = 0.22f; // Default subtle translucent fill
                if (profile.IsSelected)
                {
                    r = 1.00f; g = 0.65f; b = 0.15f; a = 0.50f; // Selected = Amber Gold
                }
                else if (profile == ViewModel.HoveredProfile)
                {
                    r = 0.20f; g = 0.80f; b = 1.00f; a = 0.42f; // Hover = Luminous Cyan
                }

                float[] vData = new float[faceVerts.Count * 9];
                for (int i = 0; i < faceVerts.Count; i++)
                {
                    int outIdx = i * 9;
                    vData[outIdx + 0] = (float)faceVerts[i].X;
                    vData[outIdx + 1] = (float)faceVerts[i].Y;
                    vData[outIdx + 2] = (float)faceVerts[i].Z;

                    vData[outIdx + 3] = r;
                    vData[outIdx + 4] = g;
                    vData[outIdx + 5] = b;

                    vData[outIdx + 6] = 0.0f;
                    vData[outIdx + 7] = 0.0f;
                    vData[outIdx + 8] = 1.0f;
                }

                _gl.BindVertexArray(_profileFaceVao);
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _profileFaceVbo);
                unsafe
                {
                    fixed (float* vPtr = vData)
                    {
                        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vData.Length * sizeof(float)), vPtr, BufferUsageARB.DynamicDraw);
                    }
                }

                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _profileFaceEbo);
                uint[] iData = faceIndices.ToArray();
                unsafe
                {
                    fixed (uint* iPtr = iData)
                    {
                        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(iData.Length * sizeof(uint)), iPtr, BufferUsageARB.DynamicDraw);
                    }
                }

                unsafe
                {
                    _gl.UniformMatrix4(modelLoc, 1, false, (float*)&identity);
                    _gl.Uniform1(lightingLoc, 0);
                    _gl.Uniform1(alphaLoc, a);
                    _gl.DrawElements(PrimitiveType.Triangles, (uint)iData.Length, DrawElementsType.UnsignedInt, (void*)0);
                }
            }
        }

        // Reset alpha to 1.0 for lines and solids
        unsafe
        {
            _gl.Uniform1(alphaLoc, 1.0f);
        }

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
                    _gl.UniformMatrix4(modelLoc, 1, false, (float*)&identity);
                    _gl.Uniform1(lightingLoc, 0);
                }
                
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(sketchVertices.Count / 6));
            }

            // --- Render 3D Solid Features with Individual Lighting & Crisp Edges ---
            var featuresToRender = new System.Collections.Generic.List<(AvaloniaCadEditor.Domain.Features.IFeature Feature, bool IsPreview)>();
            if (ViewModel != null)
            {
                foreach (var f in ViewModel.Sketch.Features)
                {
                    featuresToRender.Add((f, false));
                }

                if (ViewModel.CurrentTool is Application.Commands.ExtrudeCommand extCmd)
                {
                    var preview = extCmd.GetPreviewFeature();
                    if (preview != null)
                    {
                        featuresToRender.Add((preview, true));
                    }
                }
            }

            foreach (var (feature, isPreview) in featuresToRender)
            {
                var mesh = ViewModel!.GeometryKernel.BuildSolidMesh(new[] { feature });
                if (mesh == null || mesh.TriangleCount == 0) continue;

                int vertexCount = mesh.VertexCount;
                float[] solidVertexData = new float[vertexCount * 9];

                // Base Face Color: Emerald when previewing, Warm Amber if selected, Cool Steel Blue default
                float faceR, faceG, faceB;
                float edgeR, edgeG, edgeB;

                if (isPreview)
                {
                    faceR = 0.20f; faceG = 0.85f; faceB = 0.60f; // Vibrant Emerald Creation Feedback
                    edgeR = 0.05f; edgeG = 0.95f; edgeB = 0.65f;
                }
                else if (feature.IsSelected)
                {
                    faceR = 0.90f; faceG = 0.60f; faceB = 0.20f; // Warm Amber Selection Highlight
                    edgeR = 1.00f; edgeG = 0.85f; edgeB = 0.30f;
                }
                else
                {
                    faceR = 0.55f; faceG = 0.70f; faceB = 0.90f; // Metallic Steel Blue Unselected
                    edgeR = 0.12f; edgeG = 0.16f; edgeB = 0.22f;
                }

                // Interleave position + color + Normal
                for (int i = 0; i < vertexCount; i++)
                {
                    int posIdx = i * 3;
                    int outIdx = i * 9;

                    solidVertexData[outIdx + 0] = mesh.Positions[posIdx + 0];
                    solidVertexData[outIdx + 1] = mesh.Positions[posIdx + 1];
                    solidVertexData[outIdx + 2] = mesh.Positions[posIdx + 2];

                    solidVertexData[outIdx + 3] = faceR;
                    solidVertexData[outIdx + 4] = faceG;
                    solidVertexData[outIdx + 5] = faceB;

                    // Normal vector for real-time diffuse shading
                    solidVertexData[outIdx + 6] = mesh.Normals[posIdx + 0];
                    solidVertexData[outIdx + 7] = mesh.Normals[posIdx + 1];
                    solidVertexData[outIdx + 8] = mesh.Normals[posIdx + 2];
                }

                _gl.BindVertexArray(_solidVao);

                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);
                unsafe
                {
                    fixed (float* vPtr = solidVertexData)
                    {
                        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(solidVertexData.Length * sizeof(float)), vPtr, BufferUsageARB.DynamicDraw);
                    }
                }

                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _solidEbo);
                unsafe
                {
                    fixed (uint* iPtr = mesh.Indices)
                    {
                        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(mesh.Indices.Length * sizeof(uint)), iPtr, BufferUsageARB.DynamicDraw);
                    }
                }

                unsafe
                {
                    _gl.UniformMatrix4(modelLoc, 1, false, (float*)&identity);
                    _gl.Uniform1(lightingLoc, 1); // Enable directional lighting for 3D faces!
                    _gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.Indices.Length, DrawElementsType.UnsignedInt, (void*)0);
                }

                // --- Render Edge Outlines (Arestas e Vértices Nítidos / Highlight se selecionado) ---
                var edgeLines = new System.Collections.Generic.List<float>();
                for (int i = 0; i < mesh.Indices.Length; i += 3)
                {
                    uint i0 = mesh.Indices[i + 0];
                    uint i1 = mesh.Indices[i + 1];
                    uint i2 = mesh.Indices[i + 2];

                    void AddEdge(uint idxA, uint idxB)
                    {
                        int pA = (int)idxA * 3;
                        int pB = (int)idxB * 3;

                        // Point A
                        edgeLines.Add(mesh.Positions[pA + 0]);
                        edgeLines.Add(mesh.Positions[pA + 1]);
                        edgeLines.Add(mesh.Positions[pA + 2]);
                        // Edge Color
                        edgeLines.Add(edgeR); edgeLines.Add(edgeG); edgeLines.Add(edgeB);

                        // Point B
                        edgeLines.Add(mesh.Positions[pB + 0]);
                        edgeLines.Add(mesh.Positions[pB + 1]);
                        edgeLines.Add(mesh.Positions[pB + 2]);
                        // Edge Color
                        edgeLines.Add(edgeR); edgeLines.Add(edgeG); edgeLines.Add(edgeB);
                    }

                    AddEdge(i0, i1);
                    AddEdge(i1, i2);
                    AddEdge(i2, i0);
                }

                if (edgeLines.Count > 0)
                {
                    _gl.BindVertexArray(_solidEdgesVao);
                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidEdgesVbo);

                    unsafe
                    {
                        var edgeArr = edgeLines.ToArray();
                        fixed (float* ePtr = edgeArr)
                        {
                            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(edgeArr.Length * sizeof(float)), ePtr, BufferUsageARB.DynamicDraw);
                        }

                        _gl.Uniform1(lightingLoc, 0); // Solid color for edges
                        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(edgeLines.Count / 6));
                    }
                }
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

                // 1. Try 3D Solid Raycast Selection first
                if (ViewModel != null && (ViewModel.CurrentTool is Application.Commands.SelectTool || 
                    (ViewModel.CurrentTool is not Application.Commands.DrawLineCommand && 
                     ViewModel.CurrentTool is not Application.Commands.DrawCircleCommand)))
                {
                    if (ViewModel.TrySelect3DFeature(ray))
                    {
                        RequestNextFrameRendering();
                        return;
                    }
                }

                // 2. Intersect 2D Ground Plane for 2D entity selection and drawing tools
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
