using System.Diagnostics;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

/// <summary>
/// Programa de linhas do modo wireframe (<see cref="WireframeRenderPass"/>). Separado do
/// <see cref="ViewportShaderManager"/> porque cada vértice de linha precisa saber do outro
/// extremo da aresta (para o tracejado) e das normais das duas faces vizinhas (para a silhueta).
/// <para>
/// GLES2, como o programa principal: sem geometry shader e sem <c>noperspective</c>, por isso o
/// tracejado é feito com a distância em pixels interpolada pelo truque <c>d·w / w</c>.
/// </para>
/// </summary>
public sealed class WireframeEdgeShader
{
    // Layout de vértice montado por WireframeGeometry.BuildLineVertices.
    public const int FloatsPerVertex = 14;

    private const string VertexShaderSource = @"
        attribute vec3 aPos;
        attribute vec3 aOther;
        attribute vec3 aN1;
        attribute vec3 aN2;
        attribute vec2 aFlags; // x: 0 no início da aresta, 1 no fim; y: 1 se é candidata a silhueta

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform vec2 uViewport;
        uniform int uOrtho;
        uniform vec3 uColor;
        uniform float uBrightness;
        uniform int uColorByAxis;

        varying vec3 vColor;
        varying float vDistW;
        varying float vW;

        void main()
        {
            mat4 modelView = uView * uModel;
            vec4 posView = modelView * vec4(aPos, 1.0);
            vec4 otherView = modelView * vec4(aOther, 1.0);

            if (aFlags.y > 0.5) {
                // Aresta entre duas facetas de uma superfície curva: só aparece onde uma face
                // olha para a câmera e a outra não. O ponto médio garante que os dois vértices
                // da aresta cheguem à mesma conclusão.
                vec3 toEye = uOrtho == 1 ? vec3(0.0, 0.0, 1.0) : -0.5 * (posView.xyz + otherView.xyz);
                mat3 normalMatrix = mat3(modelView);
                bool front1 = dot(normalMatrix * aN1, toEye) > 0.0;
                bool front2 = dot(normalMatrix * aN2, toEye) > 0.0;
                if (front1 == front2) {
                    gl_Position = vec4(2.0, 2.0, 2.0, 1.0); // fora do volume de recorte: some
                    vColor = uColor;
                    vDistW = 0.0;
                    vW = 1.0;
                    return;
                }
            }

            vec4 clipA = uProjection * posView;
            vec4 clipB = uProjection * otherView;

            // Distância em pixels desde o início da aresta. Linha que cruza o plano próximo
            // (w <= 0 num dos extremos) fica contínua.
            float dist = 0.0;
            if (clipA.w > 0.0 && clipB.w > 0.0) {
                vec2 screenA = clipA.xy / clipA.w * 0.5 * uViewport;
                vec2 screenB = clipB.xy / clipB.w * 0.5 * uViewport;
                dist = aFlags.x * length(screenB - screenA);
            }

            gl_Position = clipA;
            // A interpolação de varyings é corrigida por perspectiva; d·w dividido por w volta a
            // ser linear em tela, então o tracejado tem o mesmo passo na aresta inteira.
            vDistW = dist * clipA.w;
            vW = clipA.w;

            vec3 color = uColor;
            if (uColorByAxis == 1) {
                vec3 d = aOther - aPos;
                float len = length(d);
                if (len > 1e-5) {
                    d /= len;
                    if (abs(d.x) > 0.999) color = vec3(1.0, 0.0, 0.0);
                    else if (abs(d.y) > 0.999) color = vec3(0.0, 1.0, 0.0);
                    else if (abs(d.z) > 0.999) color = vec3(0.0, 0.0, 1.0);
                }
            }
            vColor = color * uBrightness;
        }";

    private const string FragmentShaderSource = @"
        #ifdef GL_ES
        #ifdef GL_FRAGMENT_PRECISION_HIGH
        precision highp float;
        #else
        precision mediump float;
        #endif
        #endif

        uniform int uDashed;
        uniform float uDashOn;
        uniform float uDashPeriod;

        varying vec3 vColor;
        varying float vDistW;
        varying float vW;

        void main()
        {
            if (uDashed == 1 && mod(vDistW / vW, uDashPeriod) > uDashOn) {
                discard;
            }
            gl_FragColor = vec4(vColor, 1.0);
        }";

    public uint Program { get; private set; }

    public int ModelLoc { get; private set; }
    public int ViewLoc { get; private set; }
    public int ProjLoc { get; private set; }
    public int ViewportLoc { get; private set; }
    public int OrthoLoc { get; private set; }
    public int ColorLoc { get; private set; }
    public int BrightnessLoc { get; private set; }
    public int ColorByAxisLoc { get; private set; }
    public int DashedLoc { get; private set; }
    public int DashOnLoc { get; private set; }
    public int DashPeriodLoc { get; private set; }

    public void Init(GL gl)
    {
        uint vertexShader = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vertexShader, VertexShaderSource);
        gl.CompileShader(vertexShader);
        string vLog = gl.GetShaderInfoLog(vertexShader);
        if (!string.IsNullOrEmpty(vLog)) Debug.WriteLine("WIREFRAME VERTEX SHADER ERROR: " + vLog);

        uint fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fragmentShader, FragmentShaderSource);
        gl.CompileShader(fragmentShader);
        string fLog = gl.GetShaderInfoLog(fragmentShader);
        if (!string.IsNullOrEmpty(fLog)) Debug.WriteLine("WIREFRAME FRAGMENT SHADER ERROR: " + fLog);

        Program = gl.CreateProgram();
        gl.AttachShader(Program, vertexShader);
        gl.AttachShader(Program, fragmentShader);

        gl.BindAttribLocation(Program, 0, "aPos");
        gl.BindAttribLocation(Program, 1, "aOther");
        gl.BindAttribLocation(Program, 2, "aN1");
        gl.BindAttribLocation(Program, 3, "aN2");
        gl.BindAttribLocation(Program, 4, "aFlags");

        gl.LinkProgram(Program);
        string pLog = gl.GetProgramInfoLog(Program);
        if (!string.IsNullOrEmpty(pLog)) Debug.WriteLine("WIREFRAME PROGRAM LINK ERROR: " + pLog);

        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        ModelLoc = gl.GetUniformLocation(Program, "uModel");
        ViewLoc = gl.GetUniformLocation(Program, "uView");
        ProjLoc = gl.GetUniformLocation(Program, "uProjection");
        ViewportLoc = gl.GetUniformLocation(Program, "uViewport");
        OrthoLoc = gl.GetUniformLocation(Program, "uOrtho");
        ColorLoc = gl.GetUniformLocation(Program, "uColor");
        BrightnessLoc = gl.GetUniformLocation(Program, "uBrightness");
        ColorByAxisLoc = gl.GetUniformLocation(Program, "uColorByAxis");
        DashedLoc = gl.GetUniformLocation(Program, "uDashed");
        DashOnLoc = gl.GetUniformLocation(Program, "uDashOn");
        DashPeriodLoc = gl.GetUniformLocation(Program, "uDashPeriod");
    }

    public void Dispose(GL gl)
    {
        if (Program != 0)
        {
            gl.DeleteProgram(Program);
            Program = 0;
        }
    }
}
