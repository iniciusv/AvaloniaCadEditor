using System.Linq;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public sealed class ProfileFaceRenderPass : IRenderPass
{
    private uint _profileFaceVao;
    private uint _profileFaceVbo;
    private uint _profileFaceEbo;

    public void Init(GL gl)
    {
        _profileFaceVao = gl.GenVertexArray();
        _profileFaceVbo = gl.GenBuffer();
        _profileFaceEbo = gl.GenBuffer();

        gl.BindVertexArray(_profileFaceVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _profileFaceVbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _profileFaceEbo);

        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), (void*)(6 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
        }
        gl.BindVertexArray(0);
    }

    public void Render(GL gl, RenderContext context)
    {
        var vm = context.ViewModel;
        if (vm == null) return;

        var profilesToRender = new System.Collections.Generic.List<AvaloniaCadEditor.Domain.Profiles.IProfile>(vm.Sketch.Profiles);
        
        if (vm.SelectionService.SelectedObject is AvaloniaCadEditor.Domain.Profiles.IProfile selectedProfile && 
            !profilesToRender.Contains(selectedProfile))
        {
            profilesToRender.Add(selectedProfile);
        }

        if (profilesToRender.Count == 0) return;

        gl.DepthMask(false);

        // Render 2D Profiles Face Shading (Subtle Shading, Hover Pre-highlight, Selection)
        foreach (var profile in profilesToRender)
        {
            if (!profile.IsVisible) continue;
            var (faceVerts, faceIndices) = profile.GetTessellatedFace();
            if (faceVerts.Count == 0 || faceIndices.Count == 0) continue;

            float r = 0.38f, g = 0.55f, b = 0.80f, a = 0.22f; // Default subtle translucent fill
            if (profile.IsSelected)
            {
                r = 1.00f; g = 0.65f; b = 0.15f; a = 0.50f; // Selected = Amber Gold
            }
            else if (profile == vm.HoveredProfile)
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

            gl.BindVertexArray(_profileFaceVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _profileFaceVbo);
            unsafe
            {
                fixed (float* vPtr = vData)
                {
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vData.Length * sizeof(float)), vPtr, BufferUsageARB.DynamicDraw);
                }
            }

            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _profileFaceEbo);
            uint[] iData = faceIndices.ToArray();
            unsafe
            {
                fixed (uint* iPtr = iData)
                {
                    gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(iData.Length * sizeof(uint)), iPtr, BufferUsageARB.DynamicDraw);
                }
            }

            var identity = context.IdentityMatrix;
            unsafe
            {
                gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&identity);
                gl.Uniform1(context.Uniforms.LightingLoc, 0);
                gl.Uniform1(context.Uniforms.AlphaLoc, a);
                gl.DrawElements(PrimitiveType.Triangles, (uint)iData.Length, DrawElementsType.UnsignedInt, (void*)0);
            }
        }

        gl.DepthMask(true);

        // Reset alpha to 1.0 for lines and solids
        unsafe
        {
            gl.Uniform1(context.Uniforms.AlphaLoc, 1.0f);
        }
    }

    public void Dispose(GL gl)
    {
        if (_profileFaceVao != 0) { gl.DeleteVertexArray(_profileFaceVao); _profileFaceVao = 0; }
        if (_profileFaceVbo != 0) { gl.DeleteBuffer(_profileFaceVbo); _profileFaceVbo = 0; }
        if (_profileFaceEbo != 0) { gl.DeleteBuffer(_profileFaceEbo); _profileFaceEbo = 0; }
    }
}
