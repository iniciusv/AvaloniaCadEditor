using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.UI.Controls.Input;
using AvaloniaCadEditor.UI.Controls.Rendering;
using AvaloniaCadEditor.UI.ViewModels;
using Silk.NET.OpenGL;

namespace AvaloniaCadEditor.UI.Controls;

public class SilkViewportControl : OpenGlControlBase
{
    private GL? _gl;
    private readonly CameraState _camera;
    private readonly ViewportShaderManager _shaderManager;
    private readonly ViewportInputHandler _inputHandler;
    private readonly List<IRenderPass> _renderPasses;

    public CadViewModel? ViewModel => DataContext as CadViewModel;

    /// <summary>Exposed so overlays outside the GL surface (e.g. reference-guide labels) can
    /// project world points to screen space using the same camera used for rendering.</summary>
    public CameraState Camera => _camera;

    /// <summary>Raised after each GL frame, so screen-space overlays can reposition themselves
    /// exactly when the camera/viewport actually changed, without polling.</summary>
    public event System.Action? FrameRendered;

    public SilkViewportControl()
    {
        _camera = new CameraState();
        _shaderManager = new ViewportShaderManager();
        _inputHandler = new ViewportInputHandler();

        // Exact rendering order preserved:
        // 1. Solid features (3D)
        // 2. WorkPlane grid & axes
        // 3. 2D profile faces
        // 4. Sketch wireframe & preview entities
        _renderPasses = new List<IRenderPass>
        {
            new SolidFeatureRenderPass(),
            new GridRenderPass(),
            new ProfileFaceRenderPass(),
            new SketchEntityRenderPass(),
            new AvaloniaCadEditor.UI.ReferenceGuides.ReferenceGuideRenderPass()
        };

        Focusable = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
        {
            if (change.OldValue is CadViewModel oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
                if (oldVm.SelectionService != null)
                {
                    oldVm.SelectionService.SelectionChanged -= ViewModel_SelectionChanged;
                }
            }
            if (change.NewValue is CadViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                if (newVm.SelectionService != null)
                {
                    newVm.SelectionService.SelectionChanged += ViewModel_SelectionChanged;
                }
            }
        }
    }

    private void ViewModel_SelectionChanged()
    {
        RequestNextFrameRendering();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CadViewModel.SolidMesh) || 
            e.PropertyName == nameof(CadViewModel.Profiles) || 
            e.PropertyName == nameof(CadViewModel.HoveredProfile) ||
            e.PropertyName == nameof(CadViewModel.HoveredFeatureId))
        {
            RequestNextFrameRendering();
        }
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

        _shaderManager.Init(_gl);

        foreach (var pass in _renderPasses)
        {
            pass.Init(_gl);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl != null)
        {
            foreach (var pass in _renderPasses)
            {
                pass.Dispose(_gl);
            }

            _shaderManager.Dispose(_gl);
            _gl.Dispose();
            _gl = null;
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

        _gl.UseProgram(_shaderManager.ShaderProgram);

        unsafe
        {
            _gl.UniformMatrix4(_shaderManager.Uniforms.ViewLoc, 1, false, (float*)&viewMatrix);
            _gl.UniformMatrix4(_shaderManager.Uniforms.ProjLoc, 1, false, (float*)&projMatrix);
        }

        var context = new RenderContext
        {
            ViewMatrix = viewMatrix,
            ProjMatrix = projMatrix,
            IdentityMatrix = Matrix4x4.Identity,
            ShaderProgram = _shaderManager.ShaderProgram,
            Uniforms = _shaderManager.Uniforms,
            Camera = _camera,
            ViewModel = ViewModel,
            Bounds = Bounds.Size
        };

        foreach (var pass in _renderPasses)
        {
            pass.Render(_gl, context);
        }

        FrameRendered?.Invoke();
    }

    public void SetStandardView(StandardView view)
    {
        _camera.SetStandardView(view);
        RequestNextFrameRendering();
    }

    public void SetOrthographic(bool orthographic)
    {
        _camera.IsOrthographic = orthographic;
        RequestNextFrameRendering();
    }

    public void HandlePointerWheelChanged(PointerWheelEventArgs e)
    {
        _inputHandler.HandlePointerWheelChanged(e, _camera, RequestNextFrameRendering);
    }

    public void HandlePointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        _inputHandler.HandlePointerPressed(e, this, Bounds.Size, _camera, ViewModel, RequestNextFrameRendering);
    }

    public void HandlePointerMoved(PointerEventArgs e)
    {
        _inputHandler.HandlePointerMoved(e, this, Bounds.Size, _camera, ViewModel, RequestNextFrameRendering);
    }

    public void HandlePointerReleased(PointerReleasedEventArgs e)
    {
        _inputHandler.HandlePointerReleased(e);
    }
}
