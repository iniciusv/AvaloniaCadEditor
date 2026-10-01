using System;
using System.Diagnostics;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

public sealed class ViewportShaderManager
{
    private const string VertexShaderSource = @"
        attribute vec3 aPos;
        attribute vec3 aColor;
        attribute vec3 aNormal;
        
        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform int uUseLighting;
        uniform int uColorFacesByNormal;
        uniform float uDepthBias;

        varying vec3 vertexColor;

        void main()
        {
            gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
            // Aproxima a geometria da câmera em espaço de clip (proporcional a w, então
            // o efeito independe da distância), evitando z-fighting sem depender de
            // glDepthRange (indisponível em contextos ANGLE/GLES usados pelo Avalonia).
            gl_Position.z -= uDepthBias * gl_Position.w;

            vec3 currentColor = aColor;
            vec3 viewNormal = normalize(mat3(uView * uModel) * aNormal);
            
            if (uColorFacesByNormal == 1) {
                if (viewNormal.z > 0.0) {
                    currentColor = vec3(0.2, 0.6, 1.0); // Facing camera (azulzinho)
                } else {
                    currentColor = vec3(1.0, 0.4, 0.4); // Facing away (vermelhinho)
                }
            }

            if (uUseLighting == 1) {
                // Light comes from slightly above and right of the camera
                vec3 lightDir = normalize(vec3(0.3, 0.5, 1.0));
                float diff = max(dot(viewNormal, lightDir), 0.0);
                
                // Double sided lighting for faces looking away but still visible
                if (diff == 0.0) {
                    diff = max(dot(-viewNormal, lightDir), 0.0) * 0.5; // Dimmer on the back
                }
                
                float ambient = 0.40;
                float light = ambient + (1.0 - ambient) * diff;
                vertexColor = currentColor * light;
            } else {
                vertexColor = currentColor;
            }
        }";

    private const string FragmentShaderSource = @"
        #ifdef GL_ES
        precision mediump float;
        #endif
        
        uniform float uAlpha;
        varying vec3 vertexColor;
        
        void main()
        {
            gl_FragColor = vec4(vertexColor, uAlpha);
        }";

    public uint ShaderProgram { get; private set; }
    public ShaderUniforms Uniforms { get; private set; }

    public void Init(GL gl)
    {
        uint vertexShader = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vertexShader, VertexShaderSource);
        gl.CompileShader(vertexShader);
        string vLog = gl.GetShaderInfoLog(vertexShader);
        if (!string.IsNullOrEmpty(vLog)) Debug.WriteLine("VERTEX SHADER ERROR: " + vLog);

        uint fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fragmentShader, FragmentShaderSource);
        gl.CompileShader(fragmentShader);
        string fLog = gl.GetShaderInfoLog(fragmentShader);
        if (!string.IsNullOrEmpty(fLog)) Debug.WriteLine("FRAGMENT SHADER ERROR: " + fLog);

        ShaderProgram = gl.CreateProgram();
        gl.AttachShader(ShaderProgram, vertexShader);
        gl.AttachShader(ShaderProgram, fragmentShader);
        
        // Bind attribute locations BEFORE linking
        gl.BindAttribLocation(ShaderProgram, 0, "aPos");
        gl.BindAttribLocation(ShaderProgram, 1, "aColor");
        gl.BindAttribLocation(ShaderProgram, 2, "aNormal");
        
        gl.LinkProgram(ShaderProgram);
        string pLog = gl.GetProgramInfoLog(ShaderProgram);
        if (!string.IsNullOrEmpty(pLog)) Debug.WriteLine("PROGRAM LINK ERROR: " + pLog);

        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        Uniforms = new ShaderUniforms(
            ViewLoc: gl.GetUniformLocation(ShaderProgram, "uView"),
            ProjLoc: gl.GetUniformLocation(ShaderProgram, "uProjection"),
            ModelLoc: gl.GetUniformLocation(ShaderProgram, "uModel"),
            LightingLoc: gl.GetUniformLocation(ShaderProgram, "uUseLighting"),
            AlphaLoc: gl.GetUniformLocation(ShaderProgram, "uAlpha"),
            ColorFacesByNormalLoc: gl.GetUniformLocation(ShaderProgram, "uColorFacesByNormal"),
            DepthBiasLoc: gl.GetUniformLocation(ShaderProgram, "uDepthBias")
        );
    }

    public void Dispose(GL gl)
    {
        if (ShaderProgram != 0)
        {
            gl.DeleteProgram(ShaderProgram);
            ShaderProgram = 0;
        }
    }

    public static (float R, float G, float B, float A) ParseHexColor(string hex, (float, float, float, float) fallback)
    {
        if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith("#") || hex.Length != 9)
            return fallback;
            
        try 
        {
            float a = Convert.ToInt32(hex.Substring(1, 2), 16) / 255f;
            float r = Convert.ToInt32(hex.Substring(3, 2), 16) / 255f;
            float g = Convert.ToInt32(hex.Substring(5, 2), 16) / 255f;
            float b = Convert.ToInt32(hex.Substring(7, 2), 16) / 255f;
            return (r, g, b, a);
        }
        catch 
        {
            return fallback;
        }
    }
}
