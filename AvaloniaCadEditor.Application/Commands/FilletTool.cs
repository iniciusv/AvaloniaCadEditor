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

    /// <summary>
    /// Disparado, em vez de <see cref="CodeGenerated"/>, quando o alvo é uma instância de peça: o fillet
    /// vai para o <c>Build()</c> do record (ver <see cref="AvaloniaCadEditor.Application.Scenes.PartBuildFilletEditor"/>),
    /// não para a cena. Cada face vai como PLANO (normal + um ponto dela), em coordenadas do <b>mundo</b>
    /// (quem ouve converte para a peça, como no Espelhar); lista vazia = todas as arestas.
    /// </summary>
    public event EventHandler<(PartInstance Owner, IReadOnlyList<(Vector3D Normal, Point3D Point)> WorldFaces, FilletMode Mode, double Radius)>? PartFilletRequested;

    private const double DefaultRadius = 2.0;
    private readonly ISelectionService _selectionService;
    private Sketch? _sketch;
    private readonly FilletMode _mode;
    private string? _refusal;

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
        _refusal = null;

        if (sel is PartInstance instance)
        {
            PartFilletRequested?.Invoke(this, (instance, Array.Empty<(Vector3D, Point3D)>(), _mode, DefaultRadius));
            Cancel();
        }
        else if (sel is AvaloniaCadEditor.Domain.Profiles.IProfile partFace && PartFaces(partFace) is { } partFaces)
        {
            var owners = partFaces.Select(f => (f as AvaloniaCadEditor.Domain.Profiles.FaceProfile)?.BaseFeature as PartInstance).Distinct().ToList();
            if (owners.Count > 1 || owners[0] == null)
            {
                _refusal = "✗ Fillet: todas as faces precisam ser da mesma instância de peça; aplique uma instância por vez.";
                return;
            }
            // Center = Plane.Origin, que fica no plano da face (o mesmo plano que o realce da seleção usa).
            var faces = partFaces.Cast<AvaloniaCadEditor.Domain.Profiles.FaceProfile>().Select(f => (f.Normal, f.Center)).ToList();
            PartFilletRequested?.Invoke(this, (owners[0]!, faces, _mode, DefaultRadius));
            Cancel();
        }
        else if (sel is IFeature feature && feature is not WorkPlaneFeature)
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
                    // O ponto faz da face um plano: sem ele, o chanfro pegaria toda face paralela.
                    var c = faceProfile.Center;
                    faceRefsCode.Add(System.FormattableString.Invariant($"    {filletVar}.Faces.Add(new FaceReference({AvaloniaCadEditor.Domain.Scripting.FeatureOrigin.VariableName(baseFeature)}, -1, new Vector3D({faceProfile.Normal.X}, {faceProfile.Normal.Y}, {faceProfile.Normal.Z}), new Point3D({Math.Round(c.X, 6)}, {Math.Round(c.Y, 6)}, {Math.Round(c.Z, 6)}), \"CustomFace\"));"));
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

    /// <summary>
    /// As faces da seleção, se alguma for de uma instância de peça (aí todas têm de ser da mesma, ou não há
    /// como gerar um fillet só — quem chama confere); null quando nenhuma é: o caminho antigo, de código na cena.
    /// </summary>
    private static List<AvaloniaCadEditor.Domain.Profiles.IProfile>? PartFaces(AvaloniaCadEditor.Domain.Profiles.IProfile profile)
    {
        var faces = profile is AvaloniaCadEditor.Domain.Profiles.CompoundProfile comp
            ? comp.SubProfiles.ToList()
            : new List<AvaloniaCadEditor.Domain.Profiles.IProfile> { profile };

        bool anyOnPart = faces.Any(f => f is AvaloniaCadEditor.Domain.Profiles.FaceProfile { BaseFeature: PartInstance });
        return anyOnPart ? faces : null;
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
        if (_refusal != null) return _refusal;
        return $"Operação {Name}: Pressione 'F' para alternar entre Face/Sólido, selecione e pressione Enter.";
    }
}

