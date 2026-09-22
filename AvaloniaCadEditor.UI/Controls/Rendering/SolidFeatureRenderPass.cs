using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public sealed class SolidFeatureRenderPass : IRenderPass
{
    private uint _solidVao;
    private uint _solidVbo;
    private uint _solidEbo;

    private uint _featureEdgesVao;
    private uint _featureEdgesVbo;
    private uint _internalEdgesVao;
    private uint _internalEdgesVbo;

    public void Init(GL gl)
    {
        // Setup Solid Mesh VAO / VBO / EBO
        _solidVao = gl.GenVertexArray();
        _solidVbo = gl.GenBuffer();
        _solidEbo = gl.GenBuffer();

        gl.BindVertexArray(_solidVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _solidEbo);

        unsafe
        {
            // Attribute 0: Position (vec3), Attribute 1: Color (vec3), Attribute 2: Normal (vec3) -> 9 floats stride
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)(6 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
        }
        gl.BindVertexArray(0);

        // Setup Feature Edges VAO / VBO
        _featureEdgesVao = gl.GenVertexArray();
        _featureEdgesVbo = gl.GenBuffer();

        gl.BindVertexArray(_featureEdgesVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _featureEdgesVbo);

        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
        }
        gl.BindVertexArray(0);

        // Setup Internal Edges VAO / VBO
        _internalEdgesVao = gl.GenVertexArray();
        _internalEdgesVbo = gl.GenBuffer();

        gl.BindVertexArray(_internalEdgesVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _internalEdgesVbo);

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
        if (vm == null) return;

        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.Enable(EnableCap.PolygonOffsetLine);
        gl.PolygonOffset(1.0f, 1.0f);

        // Render 3D Solid Features with Individual Lighting & Crisp Edges
        var featuresToRender = new List<(IFeature Feature, bool IsPreview)>();
        foreach (var f in vm.Sketch.Features)
        {
            if (f.IsVisible) featuresToRender.Add((f, false));
        }

        if (vm.CurrentTool is Application.Commands.ExtrudeCommand extCmd)
        {
            var preview = extCmd.GetPreviewFeature();
            if (preview != null)
            {
                featuresToRender.Add((preview, true));
            }
        }

        var identity = context.IdentityMatrix;

        foreach (var (feature, isPreview) in featuresToRender)
        {
            var mesh = vm.GeometryKernel.BuildSolidMesh(new[] { feature });
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
                faceR = 0.10f; faceG = 0.40f; faceB = 1.00f; // Strong Vivid Blue Selection Highlight
                edgeR = 0.00f; edgeG = 0.20f; edgeB = 0.80f;
            }
            else if (vm.HoveredFeatureId == feature.Id)
            {
                faceR = 1.00f; faceG = 0.65f; faceB = 0.15f; // Strong Amber/Gold for Hover 3D face
                edgeR = 1.00f; edgeG = 0.80f; edgeB = 0.20f;
            }
            else
            {
                faceR = 0.92f; faceG = 0.92f; faceB = 0.95f; // Near white / light grey Base Color
                edgeR = 0.60f; edgeG = 0.60f; edgeB = 0.65f;
                
                // Dim background objects if we are in a custom workplane
                if (!vm.ActiveWorkPlane.Equals(AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY))
                {
                    faceR *= 0.6f; faceG *= 0.6f; faceB *= 0.6f;
                    edgeR *= 0.6f; edgeG *= 0.6f; edgeB *= 0.6f;
                }
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

            gl.BindVertexArray(_solidVao);

            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _solidVbo);
            unsafe
            {
                fixed (float* vPtr = solidVertexData)
                {
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(solidVertexData.Length * sizeof(float)), vPtr, BufferUsageARB.DynamicDraw);
                }
            }

            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _solidEbo);
            unsafe
            {
                fixed (uint* iPtr = mesh.Indices)
                {
                    gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(mesh.Indices.Length * sizeof(uint)), iPtr, BufferUsageARB.DynamicDraw);
                }
            }

            unsafe
            {
                gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&identity);
                gl.Uniform1(context.Uniforms.LightingLoc, 1); // Enable directional lighting for 3D faces!
                gl.Uniform1(context.Uniforms.ColorFacesByNormalLoc, vm.OsnapSettings.ColorFacesByNormal ? 1 : 0);
                gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.Indices.Length, DrawElementsType.UnsignedInt, (void*)0);
                gl.Uniform1(context.Uniforms.ColorFacesByNormalLoc, 0); // Restore to 0 for other passes just in case
            }

            // Render Edge Outlines
            var settings = new AvaloniaCadEditor.Infrastructure.Settings.JsonTessellationSettingsProvider().GetSettings();
            
            var featureColor = ViewportShaderManager.ParseHexColor(settings.FeatureEdgeColor, (edgeR, edgeG, edgeB, 1.0f));
            var internalColor = ViewportShaderManager.ParseHexColor(settings.InternalEdgeColor, (0.5f, 0.5f, 0.5f, 0.5f));

            if (feature.IsSelected || isPreview)
            {
                featureColor = (edgeR, edgeG, edgeB, 1.0f);
                internalColor = (edgeR, edgeG, edgeB, 0.5f);
            }

            void BuildEdgeBuffer(uint[] edgeIndices, (float R, float G, float B, float A) color, uint vao, uint vbo)
            {
                if (edgeIndices == null || edgeIndices.Length == 0) return;

                var edgeLines = new List<float>();
                bool colorByAxis = vm.OsnapSettings.ColorEdgesByAxis;

                for (int i = 0; i < edgeIndices.Length; i += 2)
                {
                    int pA = (int)edgeIndices[i] * 3;
                    int pB = (int)edgeIndices[i + 1] * 3;

                    float ax = mesh.Positions[pA + 0]; float ay = mesh.Positions[pA + 1]; float az = mesh.Positions[pA + 2];
                    float bx = mesh.Positions[pB + 0]; float by = mesh.Positions[pB + 1]; float bz = mesh.Positions[pB + 2];
                    
                    float r = color.R; float g = color.G; float b = color.B;

                    if (colorByAxis)
                    {
                        float dx = bx - ax; float dy = by - ay; float dz = bz - az;
                        float len = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (len > 1e-5f)
                        {
                            dx /= len; dy /= len; dz /= len;
                            if (System.Math.Abs(dx) > 0.999f) { r = 1.0f; g = 0.0f; b = 0.0f; }
                            else if (System.Math.Abs(dy) > 0.999f) { r = 0.0f; g = 1.0f; b = 0.0f; }
                            else if (System.Math.Abs(dz) > 0.999f) { r = 0.0f; g = 0.0f; b = 1.0f; }
                        }
                    }

                    // Point A
                    edgeLines.Add(ax); edgeLines.Add(ay); edgeLines.Add(az);
                    edgeLines.Add(r); edgeLines.Add(g); edgeLines.Add(b);

                    // Point B
                    edgeLines.Add(bx); edgeLines.Add(by); edgeLines.Add(bz);
                    edgeLines.Add(r); edgeLines.Add(g); edgeLines.Add(b);
                }

                gl.BindVertexArray(vao);
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

                unsafe
                {
                    var edgeArr = edgeLines.ToArray();
                    fixed (float* ePtr = edgeArr)
                    {
                        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(edgeArr.Length * sizeof(float)), ePtr, BufferUsageARB.DynamicDraw);
                    }

                    gl.Uniform1(context.Uniforms.LightingLoc, 0); // Solid color for edges
                    
                    // Enable blending for transparent edges
                    if (color.A < 1.0f) {
                        gl.Enable(EnableCap.Blend);
                        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    }
                    
                    gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(edgeLines.Count / 6));
                    
                    if (color.A < 1.0f) {
                        gl.Disable(EnableCap.Blend);
                    }
                }
            }

            BuildEdgeBuffer(mesh.FeatureEdges, featureColor, _featureEdgesVao, _featureEdgesVbo);
            if (settings.ShowInternalEdges)
            {
                BuildEdgeBuffer(mesh.InternalEdges, internalColor, _internalEdgesVao, _internalEdgesVbo);
            }
        }

        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.Disable(EnableCap.PolygonOffsetLine);
    }

    public void Dispose(GL gl)
    {
        if (_solidVao != 0) { gl.DeleteVertexArray(_solidVao); _solidVao = 0; }
        if (_solidVbo != 0) { gl.DeleteBuffer(_solidVbo); _solidVbo = 0; }
        if (_solidEbo != 0) { gl.DeleteBuffer(_solidEbo); _solidEbo = 0; }
        if (_featureEdgesVao != 0) { gl.DeleteVertexArray(_featureEdgesVao); _featureEdgesVao = 0; }
        if (_featureEdgesVbo != 0) { gl.DeleteBuffer(_featureEdgesVbo); _featureEdgesVbo = 0; }
        if (_internalEdgesVao != 0) { gl.DeleteVertexArray(_internalEdgesVao); _internalEdgesVao = 0; }
        if (_internalEdgesVbo != 0) { gl.DeleteBuffer(_internalEdgesVbo); _internalEdgesVbo = 0; }
    }
}
