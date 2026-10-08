using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using AvaloniaCadEditor.Domain.Mesh;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

/// <summary>
/// Modo de exibição "desenho técnico" (<see cref="ViewModels.CadViewModel.IsWireframe"/>): sem
/// faces, só quinas e silhuetas; o trecho de cada linha que fica atrás de alguma face sai
/// tracejado e mais apagado.
/// <para>
/// O que está oculto sai do depth buffer, por pixel: (1) as faces de todos os sólidos são
/// desenhadas só na profundidade, sem cor; (2) as linhas com <c>GREATER</c> são as ocultas,
/// tracejadas; (3) as linhas com <c>LEQUAL</c> são as visíveis, contínuas. Todas as ocultas
/// antes de todas as visíveis, para a contínua ficar por cima onde as duas coincidem em projeção.
/// </para>
/// </summary>
public sealed class WireframeRenderPass : IRenderPass
{
    private readonly record struct DrawnSolid(SolidRenderable Item, TriangleMesh Mesh, Matrix4x4 Transform, (float R, float G, float B) Color);

    private const float DashOnPixels = 6f;
    private const float DashPeriodPixels = 10f;
    private const float HiddenBrightness = 0.5f;

    // Empurra as faces do prepasse para trás, para a aresta que está sobre uma face visível não
    // brigar com ela no depth test (o que a tracejaria aos pedaços).
    private const float DepthPrepassOffset = 2f;

    private readonly WireframeEdgeShader _edgeShader = new();

    // Arestas classificadas por malha, só das malhas que já apareceram no modo. Malhas são
    // imutáveis e reaproveitadas entre frames, e a tabela larga a entrada quando a malha morre.
    // Uma mudança de CreaseAngle gera malhas novas (ele está no GeometryKey), então a chave por
    // instância continua coerente. A exceção é malha importada (STL), que não é refeita: ela só
    // pega o ângulo novo quando o modo é desligado e religado.
    private readonly ConditionalWeakTable<TriangleMesh, float[]> _lineVertices = new();
    private bool _hasCachedLines;

    private uint _depthVao;
    private uint _depthVbo;
    private uint _depthEbo;
    private uint _linesVao;
    private uint _linesVbo;

    public void Init(GL gl)
    {
        _edgeShader.Init(gl);

        // Prepasse: só posição. aColor/aNormal ficam desligados e o ColorMask descarta a cor.
        _depthVao = gl.GenVertexArray();
        _depthVbo = gl.GenBuffer();
        _depthEbo = gl.GenBuffer();
        gl.BindVertexArray(_depthVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _depthVbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _depthEbo);
        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(0);
        }
        gl.BindVertexArray(0);

        _linesVao = gl.GenVertexArray();
        _linesVbo = gl.GenBuffer();
        gl.BindVertexArray(_linesVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _linesVbo);
        unsafe
        {
            uint stride = WireframeEdgeShader.FloatsPerVertex * sizeof(float);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, stride, (void*)(9 * sizeof(float)));
            gl.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, stride, (void*)(12 * sizeof(float)));
            for (uint i = 0; i < 5; i++) gl.EnableVertexAttribArray(i);
        }
        gl.BindVertexArray(0);
    }

    public void Render(GL gl, RenderContext context)
    {
        var vm = context.ViewModel;
        if (vm == null || !vm.IsWireframe)
        {
            // Modo desligado: não segura memória por causa dele.
            if (_hasCachedLines)
            {
                _lineVertices.Clear();
                _hasCachedLines = false;
            }
            return;
        }

        var solids = new List<DrawnSolid>();
        foreach (var item in SolidRenderables.Collect(vm))
        {
            var mesh = SolidRenderables.GetMesh(vm, item);
            if (mesh == null || mesh.TriangleCount == 0) continue;

            // A cor de face é a que lê bem sobre o fundo escuro; a de aresta foi pensada para
            // ficar sobre a face clara do modo sombreado.
            var (face, _) = SolidRenderables.ResolveColors(vm, item);
            solids.Add(new DrawnSolid(item, mesh, SolidRenderables.GetModelTransform(vm, item, context.IdentityMatrix), face));
        }

        DrawDepthPrepass(gl, context, solids);

        gl.UseProgram(_edgeShader.Program);
        var view = context.ViewMatrix;
        var proj = context.ProjMatrix;
        unsafe
        {
            gl.UniformMatrix4(_edgeShader.ViewLoc, 1, false, (float*)&view);
            gl.UniformMatrix4(_edgeShader.ProjLoc, 1, false, (float*)&proj);
        }
        gl.Uniform2(_edgeShader.ViewportLoc, (float)context.Bounds.Width, (float)context.Bounds.Height);
        gl.Uniform1(_edgeShader.OrthoLoc, context.Camera.IsOrthographic ? 1 : 0);
        gl.Uniform1(_edgeShader.ColorByAxisLoc, vm.OsnapSettings.ColorEdgesByAxis ? 1 : 0);
        gl.Uniform1(_edgeShader.DashOnLoc, DashOnPixels);
        gl.Uniform1(_edgeShader.DashPeriodLoc, DashPeriodPixels);

        gl.DepthMask(false);
        gl.BindVertexArray(_linesVao);

        float creaseAngle = context.Tessellation.CreaseAngle;

        gl.DepthFunc(DepthFunction.Greater);
        gl.Uniform1(_edgeShader.DashedLoc, 1);
        gl.Uniform1(_edgeShader.BrightnessLoc, HiddenBrightness);
        DrawLines(gl, solids, creaseAngle);

        gl.DepthFunc(DepthFunction.Lequal);
        gl.Uniform1(_edgeShader.DashedLoc, 0);
        gl.Uniform1(_edgeShader.BrightnessLoc, 1f);
        DrawLines(gl, solids, creaseAngle);

        gl.BindVertexArray(0);
        gl.DepthMask(true);

        // As faces do prepasse são invisíveis, mas a profundidade delas serve mais uma vez: é com
        // ela que a grade fica escondida atrás da peça. Então a limpeza do depth fica para o
        // GridRenderPass, que vem logo depois (ver RenderContext.SolidDepthPending); do esboço e
        // das guias para a frente o depth já está limpo, como antes.
        context.SolidDepthPending = true;

        // Os passes seguintes assumem o programa principal, com view/projeção já carregados.
        gl.UseProgram(context.ShaderProgram);
    }

    private void DrawDepthPrepass(GL gl, RenderContext context, List<DrawnSolid> solids)
    {
        gl.UseProgram(context.ShaderProgram);
        gl.Uniform1(context.Uniforms.LightingLoc, 0);
        gl.ColorMask(false, false, false, false);
        gl.Enable(EnableCap.PolygonOffsetFill);
        gl.PolygonOffset(DepthPrepassOffset, DepthPrepassOffset);
        gl.BindVertexArray(_depthVao);

        foreach (var (item, mesh, transform, _) in solids)
        {
            // Ferramenta de peça é um fantasma: não esconde o sólido que ela está cortando.
            if (item.IsToolFeature) continue;

            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _depthVbo);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _depthEbo);
            unsafe
            {
                fixed (float* p = mesh.Positions)
                {
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(mesh.Positions.Length * sizeof(float)), p, BufferUsageARB.DynamicDraw);
                }
                fixed (uint* i = mesh.Indices)
                {
                    gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(mesh.Indices.Length * sizeof(uint)), i, BufferUsageARB.DynamicDraw);
                }
                var model = transform;
                gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&model);
                gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.Indices.Length, DrawElementsType.UnsignedInt, (void*)0);
            }
        }

        gl.BindVertexArray(0);
        gl.Disable(EnableCap.PolygonOffsetFill);
        gl.ColorMask(true, true, true, true);
    }

    private void DrawLines(GL gl, List<DrawnSolid> solids, float creaseAngle)
    {
        foreach (var (_, mesh, transform, color) in solids)
        {
            var vertices = _lineVertices.GetValue(mesh, m => WireframeGeometry.BuildLineVertices(m, creaseAngle));
            _hasCachedLines = true;
            if (vertices.Length == 0) continue;

            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _linesVbo);
            unsafe
            {
                fixed (float* v = vertices)
                {
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.DynamicDraw);
                }
                var model = transform;
                gl.UniformMatrix4(_edgeShader.ModelLoc, 1, false, (float*)&model);
            }
            gl.Uniform3(_edgeShader.ColorLoc, color.R, color.G, color.B);
            gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertices.Length / WireframeEdgeShader.FloatsPerVertex));
        }
    }

    public void Dispose(GL gl)
    {
        _edgeShader.Dispose(gl);
        if (_depthVao != 0) { gl.DeleteVertexArray(_depthVao); _depthVao = 0; }
        if (_depthVbo != 0) { gl.DeleteBuffer(_depthVbo); _depthVbo = 0; }
        if (_depthEbo != 0) { gl.DeleteBuffer(_depthEbo); _depthEbo = 0; }
        if (_linesVao != 0) { gl.DeleteVertexArray(_linesVao); _linesVao = 0; }
        if (_linesVbo != 0) { gl.DeleteBuffer(_linesVbo); _linesVbo = 0; }
    }
}
