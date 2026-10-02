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

    // Seleção por retângulo (SelectTool): começa no botão esquerdo pressionado e só vira um
    // retângulo depois de arrastar alguns pixels, para não atrapalhar o clique simples.
    private const double BoxDragThreshold = 4.0;
    private Point? _boxStart;
    private bool _boxActive;
    private bool _boxAdditive;
    private BoxSelector.Snapshot? _boxSnapshot;

    /// <summary>Retângulo de seleção em pixels do viewport (null = esconder) e se é janela
    /// (true, esquerda → direita) ou crossing (false).</summary>
    public event Action<Rect?, bool>? SelectionBoxChanged;

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

                if (e.ClickCount == 2)
                {
                    var hit = viewModel.Raycast3D(ray);
                    if (hit.HasValue && hit.Value.FeatureId.HasValue)
                    {
                        var feature = viewModel.Sketch.Features.FirstOrDefault(f => f.Id == hit.Value.FeatureId.Value)
                                      ?? viewModel.ActiveToolFeatures.FirstOrDefault(f => f.Id == hit.Value.FeatureId.Value);
                        if (feature is AvaloniaCadEditor.Domain.Features.BooleanFeature bf)
                        {
                            viewModel.ActiveBooleanGroupName = bf.Name;
                        }
                        else if (feature is AvaloniaCadEditor.Domain.Features.PartInstance instance
                                 && CadViewModel.CollectToolFeatures(instance.Definition).Count > 0)
                        {
                            viewModel.ActiveBooleanGroupName = instance.Name;
                        }
                        else
                        {
                            viewModel.ActiveBooleanGroupName = null;
                        }
                    }
                    else
                    {
                        viewModel.ActiveBooleanGroupName = null;
                    }
                    requestRender();
                    return;
                }

                // Candidato a seleção por retângulo. O snapshot é tirado antes do clique abaixo,
                // que pode limpar a seleção - com Ctrl o retângulo soma ao que já estava marcado.
                if (viewModel.CurrentTool is Application.Commands.SelectTool)
                {
                    _boxStart = point.Position;
                    _boxActive = false;
                    _boxAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Control) || viewModel.IsCtrlPressed;
                    _boxSnapshot = _boxAdditive ? BoxSelector.Snapshot.Capture(viewModel) : null;
                }

                // --- NOVA LÓGICA: Despacho por tipo de tool ---
                
                // Tools 3D: BooleanTool e FilletTool operam por seleção de feature via 3D raycast
                if (viewModel.CurrentTool is Application.Commands.BooleanTool booleanTool)
                {
                    var hit = viewModel.Raycast3D(ray);
                    if (hit.HasValue && hit.Value.FeatureId.HasValue)
                    {
                        // Explicitly give the tool the sketch before triggering selection events
                        booleanTool.OnMouseDown(viewModel.Sketch, new AvaloniaCadEditor.Domain.ValueObjects.Point3D(0, 0, 0));

                        // Ctrl+clique acumula bases/ferramentas em vez de confirmar na hora (não passa
                        // pelo SelectionService.SelectById, que desmarcaria tudo o mais).
                        var feature = viewModel.Sketch.Features.FirstOrDefault(f => f.Id == hit.Value.FeatureId.Value);
                        if (feature != null)
                        {
                            bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || viewModel.IsCtrlPressed;
                            booleanTool.PickFeature(feature, isCtrl);
                        }
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
                    UpdateWorldUnitsPerPixel(viewModel, camera, point.Position, bounds, planeNormal, planeOrigin, intersection.Value);
                    bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                    handledBy2D = viewModel.OnPointerPressed(intersection.Value, isCtrl);
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
                            var plane = GetStableWorkPlane(viewModel, hit.Value);
                            viewModel.ActiveWorkPlane = plane;
                            viewModel.SelectSelectToolCommand.Execute(null);
                            requestRender();
                        }
                    }
                    else if (viewModel.CurrentTool is Application.Commands.SelectTool || viewModel.CurrentTool is Application.Commands.FilletTool)
                    {
                        var hit = viewModel.Raycast3D(ray);
                        if (hit.HasValue && hit.Value.FeatureId.HasValue)
                        {
                            if (viewModel.CurrentSelectionMode == CadViewModel.TargetSelectionMode.Feature)
                            {
                                // Ctrl+clique acumula vários sólidos marcados (ex.: para abrir Cut/Fuse
                                // com várias bases de uma vez); só faz sentido no SelectTool - o
                                // FilletTool sempre opera num alvo só, então continua no seleciona-único.
                                bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || viewModel.IsCtrlPressed;
                                var tool = viewModel.ActiveToolFeatures.FirstOrDefault(f => f.Id == hit.Value.FeatureId.Value);
                                if (tool != null && viewModel.CurrentTool is Application.Commands.SelectTool)
                                {
                                    // Sólido vermelho do grupo aberto: ferramentas soltas continuam selecionáveis;
                                    // as de peça não estão no sketch, então só realçam e mostram o código.
                                    if (viewModel.Sketch.Features.Contains(tool))
                                    {
                                        viewModel.SelectionService.SelectById(viewModel.Sketch, tool.Id);
                                    }
                                    viewModel.RevealToolFeature(tool);
                                }
                                else if (isCtrl && viewModel.CurrentTool is Application.Commands.SelectTool)
                                {
                                    var feature = viewModel.Sketch.Features.FirstOrDefault(f => f.Id == hit.Value.FeatureId.Value);
                                    if (feature != null && feature is not AvaloniaCadEditor.Domain.Features.WorkPlaneFeature)
                                    {
                                        viewModel.SelectionService.ToggleFeature(viewModel.Sketch, feature);
                                    }
                                }
                                else
                                {
                                    viewModel.SelectionService.SelectById(viewModel.Sketch, hit.Value.FeatureId.Value);
                                }
                                requestRender();
                            }
                            else if (viewModel.CurrentSelectionMode == CadViewModel.TargetSelectionMode.Face)
                            {
                                var selectedProfile = BuildFaceProfile(viewModel, hit.Value);
                                if (selectedProfile != null)
                                {
                                    selectedProfile.IsSelected = true;
                                    bool isCtrlPressed = (e.KeyModifiers & Avalonia.Input.KeyModifiers.Control) != 0;
                                    if (isCtrlPressed || viewModel.IsCtrlPressed)
                                    {
                                        var currentSelection = viewModel.SelectionService.SelectedObject as AvaloniaCadEditor.Domain.Profiles.IProfile;
                                        var subs = new System.Collections.Generic.List<AvaloniaCadEditor.Domain.Profiles.IProfile>();
                                        if (currentSelection is AvaloniaCadEditor.Domain.Profiles.CompoundProfile compound)
                                            subs.AddRange(compound.SubProfiles);
                                        else if (currentSelection != null)
                                            subs.Add(currentSelection);

                                        var existing = subs.FirstOrDefault(p =>
                                            (p is AvaloniaCadEditor.Domain.Profiles.ParametricBoxFace pb && selectedProfile is AvaloniaCadEditor.Domain.Profiles.ParametricBoxFace spb && pb.ParentBox.Id == spb.ParentBox.Id && pb.SelectedFace == spb.SelectedFace) ||
                                            (p is AvaloniaCadEditor.Domain.Profiles.ParametricCylinderFace pc && selectedProfile is AvaloniaCadEditor.Domain.Profiles.ParametricCylinderFace spc && pc.ParentCylinder.Id == spc.ParentCylinder.Id && pc.SelectedFace == spc.SelectedFace));

                                        if (existing != null)
                                            subs.Remove(existing);
                                        else
                                            subs.Add(selectedProfile);

                                        if (subs.Count == 0)
                                            viewModel.SelectionService.ClearSelection();
                                        else if (subs.Count == 1)
                                            viewModel.SelectionService.SelectProfile(subs[0]);
                                        else
                                        {
                                            var newComp = new AvaloniaCadEditor.Domain.Profiles.CompoundProfile(subs) { IsSelected = true };
                                            foreach (var s in newComp.SubProfiles) s.IsSelected = true;
                                            viewModel.SelectionService.SelectProfile(newComp);
                                        }
                                    }
                                    else
                                    {
                                        viewModel.SelectionService.SelectProfile(selectedProfile);
                                    }
                                    requestRender();
                                }
                            }
                        }
                        else if (viewModel.CurrentTool is Application.Commands.SelectTool)
                        {
                            // Clique em espaço vazio: limpa a seleção, exceto com Ctrl (que só
                            // acumula, nunca remove tudo ao errar o alvo).
                            bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || viewModel.IsCtrlPressed;
                            if (!isCtrl)
                            {
                                viewModel.SelectionService.ClearSelection(viewModel.Sketch);
                                requestRender();
                            }
                        }
                    }
                    if (viewModel.CurrentTool is Application.Commands.ExtrudeCommand extCmd)
                    {
                        if (!extCmd.IsDefiningHeight)
                        {
                            var hit = viewModel.Raycast3D(ray);
                            if (hit.HasValue && hit.Value.FeatureId.HasValue)
                            {
                                var selectedProfile = BuildFaceProfile(viewModel, hit.Value);
                                if (selectedProfile != null)
                                {
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

    public void HandlePointerMoved(
        PointerEventArgs e, 
        Visual visual, 
        Size bounds, 
        CameraState camera, 
        CadViewModel? viewModel, 
        Action requestRender)
    {
        var point = e.GetCurrentPoint(visual);

        if (_boxStart is { } boxStart)
        {
            if (!point.Properties.IsLeftButtonPressed)
            {
                ResetBox();
            }
            else
            {
                var d = point.Position - boxStart;
                if (!_boxActive && Math.Sqrt(d.X * d.X + d.Y * d.Y) > BoxDragThreshold)
                    _boxActive = true;

                if (_boxActive)
                {
                    SelectionBoxChanged?.Invoke(
                        BoxSelectionGeometry.Normalize(boxStart, point.Position),
                        BoxSelectionGeometry.IsWindowSelection(boxStart, point.Position));
                    _lastMousePos = point.Position;
                    return;
                }
            }
        }

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
            
            if (viewModel.CurrentTool is Application.Commands.ExtrudeCommand extCmd && extCmd.IsDefiningHeight && extCmd.SelectedProfile != null)
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
                        extCmd.UpdateHeight(extCmd.CurrentHeight + mouseProj * sensitivity);
                        requestRender();
                    }
                }
            }
            else
            {
                var intersection = ray.IntersectPlane(planeNormal, planeOrigin);
                if (intersection.HasValue)
                {
                    UpdateWorldUnitsPerPixel(viewModel, camera, point.Position, bounds, planeNormal, planeOrigin, intersection.Value);
                    viewModel.OnPointerMoved(intersection.Value);
                }
            }
            
            if (viewModel.CurrentTool is Application.Commands.BooleanTool ||
                viewModel.CurrentTool is Application.Commands.ExtrudeCommand ||
                viewModel.CurrentTool is Application.Commands.FilletTool ||
                viewModel.CurrentTool is Application.Commands.SelectTool ||
                viewModel.CurrentTool is Application.Commands.SetWorkPlaneCommand)
            {
                viewModel.ProcessHover(ray);
            }
        }

        requestRender();
        _lastMousePos = point.Position;
    }

    public void HandlePointerReleased(
        PointerReleasedEventArgs e,
        Visual visual,
        Size bounds,
        CameraState camera,
        CadViewModel? viewModel,
        Action requestRender)
    {
        Debug.WriteLine($"[DEBUG] Mouse Released: {e.InitialPressMouseButton}");
        if (e.InitialPressMouseButton == MouseButton.Left && _boxStart is { } boxStart)
        {
            if (_boxActive && viewModel != null && bounds.Width > 0 && bounds.Height > 0)
            {
                var end = e.GetPosition(visual);
                BoxSelector.Apply(viewModel, camera, bounds,
                    BoxSelectionGeometry.Normalize(boxStart, end),
                    BoxSelectionGeometry.IsWindowSelection(boxStart, end),
                    _boxAdditive ? _boxSnapshot : null);
                requestRender();
            }
            ResetBox();
        }

        if (e.InitialPressMouseButton == MouseButton.Middle || e.InitialPressMouseButton == MouseButton.Right)
        {
            _isPanning = false;
            _isOrbiting = false;
            _lastMousePos = null;
        }
    }

    private void ResetBox()
    {
        bool wasActive = _boxActive;
        _boxStart = null;
        _boxActive = false;
        _boxSnapshot = null;
        if (wasActive) SelectionBoxChanged?.Invoke(null, true);
    }

    /// <summary>
    /// Measures how many world units one screen pixel spans on the work plane at the cursor, by casting a
    /// second ray one pixel to the side. Works for perspective and orthographic cameras at any orientation.
    /// </summary>
    private static void UpdateWorldUnitsPerPixel(
        CadViewModel viewModel, CameraState camera, Point screenPos, Size bounds,
        Vector3 planeNormal, Vector3 planeOrigin, AvaloniaCadEditor.Domain.ValueObjects.Point3D hitAtCursor)
    {
        var viewport = new Vector2((float)bounds.Width, (float)bounds.Height);
        var neighborRay = camera.ScreenPointToRay(new Vector2((float)screenPos.X + 1f, (float)screenPos.Y), viewport);
        var neighbor = neighborRay.IntersectPlane(planeNormal, planeOrigin);
        if (!neighbor.HasValue) return;

        double unitsPerPixel = hitAtCursor.DistanceTo(neighbor.Value);
        if (unitsPerPixel > 0 && double.IsFinite(unitsPerPixel))
            viewModel.WorldUnitsPerPixel = unitsPerPixel;
    }

    /// <summary>
    /// Constrói o perfil de face (box/cilindro paramétrico, ou genérico a partir da malha) correspondente
    /// a um hit de raycast. Usado tanto pelo clique (SelectTool/ExtrudeCommand) quanto pelo hover de face.
    /// </summary>
    internal static AvaloniaCadEditor.Domain.Profiles.IProfile? BuildFaceProfile(CadViewModel viewModel, AvaloniaCadEditor.Domain.ValueObjects.RaycastHit hit)
    {
        if (!hit.FeatureId.HasValue) return null;

        var normal = hit.Normal;
        var plane = GetStableWorkPlane(viewModel, hit);
        var baseFeature = viewModel.Sketch.Features.FirstOrDefault(f => f.Id == hit.FeatureId.Value);
        if (baseFeature == null) return null;

        var mesh = viewModel.GetFeatureMesh(hit.FeatureId.Value);
        if (mesh == null) return null;

        AvaloniaCadEditor.Domain.Profiles.IProfile? selectedProfile = null;

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

        selectedProfile ??= new AvaloniaCadEditor.Domain.Profiles.FaceProfile(baseFeature, plane, mesh);
        return selectedProfile;
    }

    internal static AvaloniaCadEditor.Domain.ValueObjects.WorkPlane GetStableWorkPlane(CadViewModel viewModel, AvaloniaCadEditor.Domain.ValueObjects.RaycastHit hit)
    {
        var normal = hit.Normal;
        var origin = hit.Point;
        
        if (hit.FeatureId.HasValue)
        {
            var featureId = hit.FeatureId.Value;
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

                var baseFeature = viewModel.Sketch.Features.FirstOrDefault(f => f.Id == hit.FeatureId.Value);
                if (baseFeature is AvaloniaCadEditor.Domain.Features.BoxFeature boxFeature)
                {
                    var faceType = boxFeature.GetFaceTypeFromNormal(new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(normal.X, normal.Y, normal.Z));
                    if (faceType.HasValue)
                    {
                        var faceRef = baseFeature.FindFace((int)faceType.Value, new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(normal.X, normal.Y, normal.Z));
                        return new AvaloniaCadEditor.Domain.ValueObjects.LinkedWorkPlane(faceRef);
                    }
                }
                else if (baseFeature is AvaloniaCadEditor.Domain.Features.CylinderFeature cylinderFeature)
                {
                    var faceType = cylinderFeature.GetFaceTypeFromNormal(new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(normal.X, normal.Y, normal.Z));
                    if (faceType.HasValue)
                    {
                        var faceRef = baseFeature.FindFace((int)faceType.Value, new AvaloniaCadEditor.Domain.ValueObjects.Vector3D(normal.X, normal.Y, normal.Z));
                        return new AvaloniaCadEditor.Domain.ValueObjects.LinkedWorkPlane(faceRef);
                    }
                }
            }
        }
        
        return new AvaloniaCadEditor.Domain.ValueObjects.WorkPlane(normal, origin);
    }
}


