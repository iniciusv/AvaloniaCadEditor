using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

/// <summary>
/// Grade do plano de trabalho e os três eixos.
/// <para>
/// A grade é adaptativa: a cada década de zoom o passo troca de 5·10^k para 5·10^(k+1). Para a
/// troca não ser um salto, a malha guardada tem passo 1 e as linhas de múltiplo de 10 ficam
/// separadas das outras no buffer: as de múltiplo de 10 são a grade principal e as outras nove de
/// cada célula são a <b>subdivisão dela</b>, que some por transparência conforme a câmera se
/// afasta. Nunca são duas grades independentes — a fraca cai exatamente dentro da forte — e no fim
/// da década a subdivisão já sumiu e as linhas fortes estão onde ela estava, com o mesmo traço,
/// então a passagem para a década seguinte não tem salto visível.
/// </para>
/// <para>
/// A malha vai até ±<see cref="HalfSteps"/> passos, e não até a borda da vista: cada nível tem que
/// cobrir a tela inteira durante toda a sua transparência. Com malha curta a subdivisão aparece
/// como um retalho quadrado em volta do alvo da câmera, com borda visível, em vez de um fade — era
/// o que acontecia quando os dois níveis eram a mesma malha em escalas diferentes, porque a fina
/// cobria 1/10 da área da grossa.
/// </para>
/// </summary>
public sealed class GridRenderPass : IRenderPass
{
    private uint _vao;
    private uint _vbo;

    // Fatias do buffer, em vértices: grade principal, subdivisão e eixos, nessa ordem.
    private int _majorVertexCount;
    private int _minorVertexCount;
    private int _axesVertexCount;

    /// <summary>Meia-largura da malha, em passos da subdivisão. 500 passos = 50 células fortes, ou
    /// 2,5x a maior distância de câmera da década — o bastante para a borda ficar fora da vista
    /// durante toda a transição.</summary>
    private const int HalfSteps = 500;

    /// <summary>Quantas linhas da subdivisão cabem numa célula da grade principal.</summary>
    private const int MajorEvery = 10;

    /// <summary>Passo da subdivisão, em unidades do mundo, na década de referência
    /// (<see cref="ReferenceDistance"/>): mantém o tamanho na tela que a grade sempre teve.</summary>
    private const float BaseStep = 5.0f;

    /// <summary>Distância de câmera em que a década é zero.</summary>
    private const float ReferenceDistance = 100.0f;

    private const float LineGray = 0.3f;

    // A subdivisão nasce opaca no começo da década e sai de cena antes de ficar densa demais para
    // ler. Depois disso fica só a grade principal, que é justamente o passo que a década seguinte
    // herda como subdivisão.
    private const float MinorFadeStart = 0.1f;
    private const float MinorFadeEnd = 0.75f;

    /// <summary>No modo de desenho técnico a grade recua: traço fino cinza disputando com aresta
    /// oculta tracejada (também fina e cinza) deixa as duas ilegíveis.</summary>
    private const float WireframeDim = 0.5f;

    public void Init(GL gl)
    {
        var vertices = new List<float>();

        void Line(float x1, float y1, float z1, float x2, float y2, float z2, float r, float g, float b)
        {
            vertices.Add(x1); vertices.Add(y1); vertices.Add(z1);
            vertices.Add(r); vertices.Add(g); vertices.Add(b);
            vertices.Add(x2); vertices.Add(y2); vertices.Add(z2);
            vertices.Add(r); vertices.Add(g); vertices.Add(b);
        }

        const float end = HalfSteps;

        // Grade principal: as linhas de múltiplo de MajorEvery passos. Primeiras no buffer para
        // poderem ser desenhadas sozinhas, com traço cheio, enquanto a subdivisão desaparece.
        for (int i = -HalfSteps; i <= HalfSteps; i += MajorEvery)
        {
            Line(-end, i, 0, end, i, 0, LineGray, LineGray, LineGray);
            Line(i, -end, 0, i, end, 0, LineGray, LineGray, LineGray);
        }
        _majorVertexCount = vertices.Count / 6;

        // Subdivisão: as outras nove linhas de cada célula. Mesmo cinza da principal, porque a
        // diferença entre as duas é só a transparência — é isso que faz a troca de década ser
        // contínua, já que a linha que era forte continua com o mesmo traço quando vira fraca.
        for (int i = -HalfSteps; i <= HalfSteps; i++)
        {
            if (i % MajorEvery == 0) continue;
            Line(-end, i, 0, end, i, 0, LineGray, LineGray, LineGray);
            Line(i, -end, 0, i, end, 0, LineGray, LineGray, LineGray);
        }
        _minorVertexCount = vertices.Count / 6 - _majorVertexCount;

        // Eixos X/Y/Z do plano, do tamanho da malha.
        Line(0, 0, 0, end, 0, 0, 1, 0, 0);
        Line(0, 0, 0, 0, end, 0, 0, 1, 0);
        Line(0, 0, 0, 0, 0, end, 0, 0, 1);
        _axesVertexCount = vertices.Count / 6 - _majorVertexCount - _minorVertexCount;

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();

        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        float[] data = vertices.ToArray();
        unsafe
        {
            fixed (float* v = data)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }

            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(0);
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

        // Década de zoom: sobe de 1 a cada 10x de distância, e frac diz onde dentro da década a
        // câmera está. A malha tem passo 1, então a escala é o próprio passo da subdivisão.
        float distance = MathF.Max(context.Camera.Distance, 0.001f);
        float level = MathF.Log10(distance / ReferenceDistance);
        float decade = MathF.Floor(level);
        float frac = level - decade;

        float minorStep = BaseStep * MathF.Pow(10f, decade);
        float majorStep = minorStep * MajorEvery;
        float minorAlpha = 1f - Smoothstep(MinorFadeStart, MinorFadeEnd, frac);

        float dim = context.ViewModel?.IsWireframe == true ? WireframeDim : 1f;

        // A grade segue o alvo da câmera para não acabar antes da borda da tela, encaixada no passo
        // da grade principal: assim toda linha cai num múltiplo exato do passo nas coordenadas do
        // plano, e qual linha é forte não muda conforme a câmera anda.
        var targetLocal = wp.ToLocalSpace(new AvaloniaCadEditor.Domain.ValueObjects.Point3D(
            context.Camera.Target.X, context.Camera.Target.Y, context.Camera.Target.Z));
        float tx = MathF.Round((float)targetLocal.X / majorStep) * majorStep;
        float ty = MathF.Round((float)targetLocal.Y / majorStep) * majorStep;

        var gridMatrix = Matrix4x4.CreateScale(minorStep) * Matrix4x4.CreateTranslation(tx, ty, 0f) * planeMatrix;

        gl.Uniform1(context.Uniforms.LightingLoc, 0);
        gl.BindVertexArray(_vao);

        unsafe
        {
            gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&gridMatrix);
        }

        gl.Uniform1(context.Uniforms.AlphaLoc, dim);
        gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_majorVertexCount);

        if (minorAlpha > 0f)
        {
            gl.Uniform1(context.Uniforms.AlphaLoc, minorAlpha * dim);
            gl.DrawArrays(PrimitiveType.Lines, _majorVertexCount, (uint)_minorVertexCount);
        }

        // Eixos: passam pela origem do plano, então acompanham só a escala, não o alvo da câmera.
        var axesMatrix = Matrix4x4.CreateScale(minorStep) * planeMatrix;
        gl.Uniform1(context.Uniforms.AlphaLoc, 1.0f);
        unsafe
        {
            gl.UniformMatrix4(context.Uniforms.ModelLoc, 1, false, (float*)&axesMatrix);
        }
        gl.DrawArrays(PrimitiveType.Lines, _majorVertexCount + _minorVertexCount, (uint)_axesVertexCount);

        gl.BindVertexArray(0);

        // O prepasse de faces do modo wireframe ficou de pé só até aqui, para a peça esconder a
        // grade; dos perfis e do esboço para a frente o depth tem que estar limpo.
        if (context.SolidDepthPending)
        {
            gl.Clear((uint)ClearBufferMask.DepthBufferBit);
            context.SolidDepthPending = false;
        }
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
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
    }
}
