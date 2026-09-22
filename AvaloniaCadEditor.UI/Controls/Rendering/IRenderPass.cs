using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public interface IRenderPass
{
    void Init(GL gl);
    void Render(GL gl, RenderContext context);
    void Dispose(GL gl);
}
