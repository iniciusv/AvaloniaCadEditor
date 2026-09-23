using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Controls.Input;

public sealed class ViewportInputHandler
{
    private Point? _lastMousePos;
    private bool _isPanning = false;
    private bool _isOrbiting = false;

    public void HandlePointerWheelChanged(PointerWheelEventArgs e, CameraState camera, Action requestRender)
    {
        Debug.WriteLine($"[DEBUG] Mouse Wheel: {e.Delta.Y}");
        
        float zoomFactor = 1.1f;
        if (e.Delta.Y > 0)
            camera.Zoom(1.0f / zoomFactor);
        else
            camera.Zoom(zoomFactor);

        requestRender();
    }

    public void HandlePointerPressed(
        PointerPressedEventArgs e, 
        Visual visual, 
        Size bounds, 
        CameraState camera, 
        CadViewModel? viewModel, 
        Action requestRender)
    {
        var point = e.GetCurrentPoint(visual);
        
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
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                var ray = camera.ScreenPointToRay(
                    new Vector2((float)point.Position.X, (float)point.Position.Y), 
                    new Vector2((float)bounds.Width, (float)bounds.Height)
                );

                if (viewModel == null) return;

                // --- NOVA LÓGICA: Despacho por tipo de tool ---
                
                // Tools 3D: BooleanTool opera por seleção de feature via 3D raycast
                if (viewModel.CurrentTool is Application.Commands.BooleanTool)
                {
                    var hit = viewModel.Raycast3D(ray);
                    if (hit.HasValue && hit.Value.FeatureId.HasValue)
                    {
                        // Explicitly give the tool the sketch before triggering selection events
                        viewModel.CurrentTool.OnMouseDown(viewModel.Sketch, new AvaloniaCadEditor.Domain.ValueObjects.Point3D(0, 0, 0));
                        viewModel.SelectionService.SelectById(viewModel.Sketch, hit.Value.FeatureId.Value);
                        requestRender();
                    }
                    return; // Pula o fluxo 2D
                }

                // 1. Tools 2D: Prioritize active WorkPlane 2D intersection
                var planeNormal = new Vector3(
                    (float)viewModel.ActiveWorkPlane.Normal.X,
                    (float)viewModel.ActiveWorkPlane.Normal.Y,
                    (float)viewModel.ActiveWorkPlane.Normal.Z);
                var planeOrigin = new Vector3(
                    (float)viewModel.ActiveWorkPlane.Origin.X,
                    (float)viewModel.ActiveWorkPlane.Origin.Y,
                    (float)viewModel.ActiveWorkPlane.Origin.Z);
                
                var intersection = ray.IntersectPlane(planeNormal, planeOrigin);
                bool handledBy2D = false;
                
                if (intersection.HasValue)
                {
                    handledBy2D = viewModel.OnPointerPressed(intersection.Value);
                    requestRender();
                }

                // 2. Fallback 3D para SetWorkPlane e SelectTool
                if (!handledBy2D)
                {
                    if (viewModel.CurrentTool is Application.Commands.SetWorkPlaneCommand)
                    {
                        var hit = viewModel.Raycast3D(ray);
                        if (hit.HasValue)
                        {
                            var normal = hit.Value.Normal;
                            var origin = hit.Value.Point;
                            
                            if (hit.Value.FeatureId.HasValue)
                            {
                                var featureId = hit.Value.FeatureId.Value;
                                var mesh = viewModel.GetFeatureMesh(featureId);
                                if (mesh != null)
                                {
                                    var tempPlane = new AvaloniaCadEditor.Domain.ValueObjects.WorkPlane(normal, origin);
                                    double minX = double.MaxValue;
                                    double minY = double.MaxValue;
                                    bool foundAny = false;
                                    
                                    for (int i = 0; i < mesh.Indices.Length; i += 3)
                                    {
                                        uint i0 = mesh.Indices[i]; uint i1 = mesh.Indices[i + 1]; uint i2 = mesh.Indices[i + 2];
                                        var v0 = new AvaloniaCadEditor.Domain.ValueObjects.Point3D(mesh.Positions[i0*3], mesh.Positions[i0*3+1], mesh.Positions[i0*3+2]);
                                        var v1 = new AvaloniaCadEditor.Domain.ValueObjects.Point3D(mesh.Positions[i1*3], mesh.Positions[i1*3+1], mesh.Positions[i1*3+2]);
                                        var v2 = new AvaloniaCadEditor.Domain.ValueObjects.Point3D(mesh.Positions[i2*3], mesh.Positions[i2*3+1], mesh.Positions[i2*3+2]);
                                        
                                        var d1 = new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(v1.X - v0.X, v1.Y - v0.Y, v1.Z - v0.Z);
                                        var d2 = new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(v2.X - v0.X, v2.Y - v0.Y, v2.Z - v0.Z);
                                        var cross = d1.CrossProduct(d2);
                                        if (cross.Length > 1e-6)
                                        {
                                            var tNormal = cross.Normalize();
                                            if (tNormal.DotProduct(normal) > 0.99)
                                            {
                                                var p0 = tempPlane.ToLocalSpace(v0);
                                                var p1 = tempPlane.ToLocalSpace(v1);
                                                var p2 = tempPlane.ToLocalSpace(v2);
                                                
                                                minX = Math.Min(minX, Math.Min(p0.X, Math.Min(p1.X, p2.X)));
                                                minY = Math.Min(minY, Math.Min(p0.Y, Math.Min(p1.Y, p2.Y)));
                                                foundAny = true;
                                            }
                                        }
                                    }
                                    
                                    if (foundAny)
                                    {
                                        origin = tempPlane.ToWorldSpace(new AvaloniaCadEditor.Domain.ValueObjects.Point3D(minX, minY, 0));
                                    }
                                }
                            }

                            viewModel.ActiveWorkPlane = new AvaloniaCadEditor.Domain.ValueObjects.WorkPlane(normal, origin);
                            viewModel.SelectSelectToolCommand.Execute(null);
                            requestRender();
                        }
                    }
                    else if (viewModel.CurrentTool is Application.Commands.SelectTool)
                    {
                        var hit = viewModel.Raycast3D(ray);
                        if (hit.HasValue && hit.Value.FeatureId.HasValue)
                        {
                            viewModel.SelectionService.SelectById(viewModel.Sketch, hit.Value.FeatureId.Value);
                            requestRender();
                        }
                    }
                    else if (viewModel.CurrentTool is Application.Commands.ExtrudeCommand extCmd)
                    {
                        if (!extCmd.IsDefiningDepth)
                        {
                            var hit = viewModel.Raycast3D(ray);
                            if (hit.HasValue && hit.Value.FeatureId.HasValue)
                            {
                                var normal = hit.Value.Normal;
                                var origin = hit.Value.Point;
                                var plane = new AvaloniaCadEditor.Domain.ValueObjects.WorkPlane(normal, origin);
                                var baseFeature = viewModel.Sketch.Features.FirstOrDefault(f => f.Id == hit.Value.FeatureId.Value);
                                if (baseFeature != null) 
                                {
                                    var mesh = viewModel.GetFeatureMesh(hit.Value.FeatureId.Value);
                                    if (mesh != null)
                                    {
                                        AvaloniaCadEditor.Domain.Profiles.IProfile selectedProfile = null;
                                        
                                        if (baseFeature is AvaloniaCadEditor.Domain.Features.BoxFeature boxFeature)
                                        {
                                            var faceType = boxFeature.GetFaceTypeFromNormal(new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(normal.X, normal.Y, normal.Z));
                                            if (faceType.HasValue)
                                            {
                                                selectedProfile = new AvaloniaCadEditor.Domain.Profiles.ParametricBoxFace(boxFeature, faceType.Value);
                                            }
                                        }
                                        else if (baseFeature is AvaloniaCadEditor.Domain.Features.CylinderFeature cylinderFeature)
                                        {
                                            var faceType = cylinderFeature.GetFaceTypeFromNormal(new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(normal.X, normal.Y, normal.Z));
                                            if (faceType.HasValue)
                                            {
                                                selectedProfile = new AvaloniaCadEditor.Domain.Profiles.ParametricCylinderFace(cylinderFeature, faceType.Value);
                                            }
                                        }

                                        if (selectedProfile == null)
                                        {
                                            selectedProfile = new AvaloniaCadEditor.Domain.Profiles.FaceProfile(baseFeature, plane, mesh);
                                        }

                                        extCmd.StartDirectExtrusion(viewModel.Sketch, selectedProfile, hit.Value.Point);
                                        requestRender();
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    public void HandlePointerMoved(
        PointerEventArgs e, 
        Visual visual, 
        Size bounds, 
        CameraState camera, 
        CadViewModel? viewModel, 
        Action requestRender)
    {
        var point = e.GetCurrentPoint(visual);

        if (_lastMousePos.HasValue)
        {
            float deltaX = (float)(point.Position.X - _lastMousePos.Value.X);
            float deltaY = (float)(point.Position.Y - _lastMousePos.Value.Y);

            if (_isOrbiting)
            {
                Debug.WriteLine($"[DEBUG] Orbiting delta: {deltaX}, {deltaY}");
                float sens = 0.01f;
                camera.Orbit(deltaX * sens, deltaY * sens);
            }
            else if (_isPanning)
            {
                Debug.WriteLine($"[DEBUG] Panning delta: {deltaX}, {deltaY}");
                camera.Pan(deltaX, deltaY);
            }
        }
        
        // --- Raycasting ---
        if (bounds.Width > 0 && bounds.Height > 0 && viewModel != null)
        {
            var ray = camera.ScreenPointToRay(
                new Vector2((float)point.Position.X, (float)point.Position.Y), 
                new Vector2((float)bounds.Width, (float)bounds.Height)
            );

            // Intersect with active WorkPlane
            var planeNormal = new Vector3(
                (float)viewModel.ActiveWorkPlane.Normal.X,
                (float)viewModel.ActiveWorkPlane.Normal.Y,
                (float)viewModel.ActiveWorkPlane.Normal.Z);
            
            var planeOrigin = new Vector3(
                (float)viewModel.ActiveWorkPlane.Origin.X,
                (float)viewModel.ActiveWorkPlane.Origin.Y,
                (float)viewModel.ActiveWorkPlane.Origin.Z);
            
            if (viewModel.CurrentTool is Application.Commands.ExtrudeCommand extCmd && extCmd.IsDefiningDepth && extCmd.SelectedProfile != null)
            {
                if (_lastMousePos.HasValue)
                {
                    float deltaX = (float)(point.Position.X - _lastMousePos.Value.X);
                    float deltaY = (float)(point.Position.Y - _lastMousePos.Value.Y);
                    
                    var n = extCmd.SelectedProfile.Normal;
                    var o = extCmd.StartPointWorld;
                    
                    var viewMat = camera.GetViewMatrix();
                    var projMat = camera.GetProjectionMatrix();
                    var viewProj = viewMat * projMat;
                    
                    var oVec = new Vector4((float)o.X, (float)o.Y, (float)o.Z, 1.0f);
                    var nVec = new Vector4((float)(o.X + n.X), (float)(o.Y + n.Y), (float)(o.Z + n.Z), 1.0f);
                    
                    var oScreen = Vector4.Transform(oVec, viewProj);
                    var nScreen = Vector4.Transform(nVec, viewProj);
                    
                    if (oScreen.W != 0) oScreen /= oScreen.W;
                    if (nScreen.W != 0) nScreen /= nScreen.W;
                    
                    // Note: Inverting Y because screen Y goes down, but NDC Y goes up
                    var screenNormal = new Vector2(nScreen.X - oScreen.X, -(nScreen.Y - oScreen.Y));
                    
                    if (screenNormal.LengthSquared() > 1e-5f)
                    {
                        screenNormal = Vector2.Normalize(screenNormal);
                        float mouseProj = deltaX * screenNormal.X + deltaY * screenNormal.Y;
                        float sensitivity = 0.5f; // Screen pixels to depth units
                        extCmd.UpdateDepth(extCmd.CurrentDepth + mouseProj * sensitivity);
                        requestRender();
                    }
                }
            }
            else
            {
                var intersection = ray.IntersectPlane(planeNormal, planeOrigin);
                if (intersection.HasValue)
                {
                    viewModel.OnPointerMoved(intersection.Value);
                }
            }
            
            if (viewModel.CurrentTool is Application.Commands.BooleanTool || viewModel.CurrentTool is Application.Commands.ExtrudeCommand)
            {
                viewModel.ProcessHover(ray);
            }
        }

        requestRender();
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
