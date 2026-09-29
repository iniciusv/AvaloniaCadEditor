using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

public class FilletTool : ICommandTool, IDisposable
{
    public event EventHandler<string>? CodeGenerated;
    private readonly ISelectionService _selectionService;
    private Sketch? _sketch;
    private readonly FilletMode _mode;

    public string Name => _mode == FilletMode.Bevel ? "Bevel" : "Fillet";

    public FilletTool(ISelectionService selectionService, FilletMode mode)
    {
        _selectionService = selectionService;
        _mode = mode;
    }

    public void OnMouseDown(Sketch sketch, Point3D point)
    {
        _sketch = sketch;
    }

    public void Execute(Sketch sketch)
    {
        var sel = _selectionService.SelectedObject;
        if (sel == null) return;

        string id = Guid.NewGuid().ToString("N").Substring(0, 4);
        string filletVar = $"fillet_{id}";
        string modeStr = _mode == FilletMode.Bevel ? "FilletMode.Bevel" : "FilletMode.Round";

        if (sel is IFeature feature && feature is not WorkPlaneFeature)
        {
            string code = $"var {filletVar} = new FilletFeature(name: \"{filletVar}\", baseFeature: {AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName(feature)}, radius: 2.0) {{ Mode = {modeStr}, ApplyToAllEdges = true }};\r\nAddFeature({filletVar});";
            CodeGenerated?.Invoke(this, code);
            Cancel();
        }
        else if (sel is AvaloniaCadEditor.Domain.Profiles.IProfile profile)
        {
            var faces = new List<AvaloniaCadEditor.Domain.Profiles.IProfile>();
            if (profile is AvaloniaCadEditor.Domain.Profiles.CompoundProfile comp)
                faces.AddRange(comp.SubProfiles);
            else
                faces.Add(profile);

            IFeature? baseFeature = null;
            var faceRefsCode = new List<string>();

            foreach (var face in faces)
            {
                if (face is AvaloniaCadEditor.Domain.Profiles.ParametricBoxFace boxFace)
                {
                    baseFeature = boxFace.ParentBox;
                    faceRefsCode.Add(System.FormattableString.Invariant($"    {filletVar}.Faces.Add(new FaceReference({AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName(baseFeature)}, (int)FaceType.{boxFace.SelectedFace}, new Vector3D({boxFace.Normal.X}, {boxFace.Normal.Y}, {boxFace.Normal.Z}), \"{boxFace.SelectedFace}\"));"));
                }
                else if (face is AvaloniaCadEditor.Domain.Profiles.ParametricCylinderFace cylFace)
                {
                    baseFeature = cylFace.ParentCylinder;
                    faceRefsCode.Add(System.FormattableString.Invariant($"    {filletVar}.Faces.Add(new FaceReference({AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName(baseFeature)}, (int)FaceType.{cylFace.SelectedFace}, new Vector3D({cylFace.Normal.X}, {cylFace.Normal.Y}, {cylFace.Normal.Z}), \"{cylFace.SelectedFace}\"));"));
                }
                else if (face is AvaloniaCadEditor.Domain.Profiles.FaceProfile faceProfile)
                {
                    baseFeature = faceProfile.BaseFeature;
                    faceRefsCode.Add(System.FormattableString.Invariant($"    {filletVar}.Faces.Add(new FaceReference({AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName(baseFeature)}, -1, new Vector3D({faceProfile.Normal.X}, {faceProfile.Normal.Y}, {faceProfile.Normal.Z}), \"CustomFace\"));"));
                }
            }

            if (baseFeature != null && faceRefsCode.Count > 0)
            {
                string code = $"var {filletVar} = new FilletFeature(name: \"{filletVar}\", baseFeature: {AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName(baseFeature)}, radius: 2.0) {{ Mode = {modeStr}, ApplyToAllEdges = false }};\r\n" +
                              string.Join("\r\n", faceRefsCode) +
                              $"\r\nAddFeature({filletVar});";
                CodeGenerated?.Invoke(this, code);
                Cancel();
            }
        }
    }

    public void StartDirectFillet(Sketch sketch, object selection)
    {
        _sketch = sketch;
        Execute(sketch);
    }

    public void OnMouseMove(Sketch sketch, Point3D point) { }

    public void Cancel()
    {
        _sketch = null;
        _selectionService.ClearSelection();
    }

    public void Dispose()
    {
    }

    public IEnumerable<Entity> GetPreviewEntities() => Enumerable.Empty<Entity>();

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape) Cancel();
        else if (key == CadKey.Enter) Execute(sketch);
    }

    public void OnValueEntered(Sketch sketch, double value) { }

    public string GetMeasurementText() => "";

    public string GetStatusText()
    {
        return $"Operação {Name}: Pressione 'F' para alternar entre Face/Sólido, selecione e pressione Enter.";
    }
}

