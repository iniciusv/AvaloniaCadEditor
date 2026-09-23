using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class CsCodeGenerator
{
    private static string Fmt(double val) => val.ToString("F2", CultureInfo.InvariantCulture);
    private static string Fmt(double? val) => val?.ToString("F2", CultureInfo.InvariantCulture) ?? "";

    public string GenerateCode(Sketch sketch, WorkPlane? referenceLens = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Avalonia CAD Editor - C# Scene Representation");
        sb.AppendLine();
        
        if (referenceLens != null)
        {
            sb.AppendLine("// --- Lente de Referência Ativa ---");
            var origin = referenceLens.Origin;
            var normal = referenceLens.Normal;
            sb.AppendLine($"var lensPlane = new WorkPlane(origin: new Point3D({Fmt(origin.X)}, {Fmt(origin.Y)}, {Fmt(origin.Z)}), normal: new Vector3D({Fmt(normal.X)}, {Fmt(normal.Y)}, {Fmt(normal.Z)}));");
            sb.AppendLine();
        }

        var allFeatures = new List<IFeature>();
        allFeatures.AddRange(sketch.WorkPlanes);
        allFeatures.AddRange(sketch.Features);
        
        var writtenFeatures = new HashSet<Guid>();
        var featureNames = new Dictionary<Guid, string>();
        
        int cylinderCount = 1;
        int boxCount = 1;
        int extrudeCount = 1;
        int booleanCount = 1;

        // Simple topological sort / dependency resolution
        // Keep looping until all features are written
        bool progress = true;
        while (allFeatures.Count > 0 && progress)
        {
            progress = false;
            for (int i = 0; i < allFeatures.Count; i++)
            {
                var feature = allFeatures[i];
                
                // Check if dependencies are already written
                bool depsMet = true;
                if (feature is BooleanFeature bf)
                {
                    foreach(var dep in bf.Dependencies)
                    {
                        if (dep != null && !writtenFeatures.Contains(dep.Id))
                        {
                            depsMet = false;
                            break;
                        }
                    }
                }
                
                if (depsMet)
                {
                    WriteFeature(feature, sb, referenceLens, featureNames, 
                        ref cylinderCount, ref boxCount, ref extrudeCount, ref booleanCount);
                    
                    writtenFeatures.Add(feature.Id);
                    allFeatures.RemoveAt(i);
                    progress = true;
                    break; // Restart the loop since we modified the list
                }
            }
        }
        
        // If there are features left due to circular deps (shouldn't happen in a valid DAG), write them as comments
        if (allFeatures.Count > 0)
        {
            sb.AppendLine("// --- Unresolved Dependencies ---");
            foreach (var f in allFeatures)
            {
                sb.AppendLine($"// Feature {f.Name} (Id: {f.Id}) could not be written due to unresolved dependencies.");
            }
        }

        return sb.ToString();
    }

    private void WriteFeature(IFeature feature, StringBuilder sb, WorkPlane? lens, 
        Dictionary<Guid, string> featureNames,
        ref int cylinderCount, ref int boxCount, ref int extrudeCount, ref int booleanCount)
    {
        string? createdVarName = null;
        if (feature is ExtrudeFeature extrude)
        {
            createdVarName = WriteExtrudeFeature(extrude, sb, lens, featureNames, ref cylinderCount, ref boxCount, ref extrudeCount);
        }
        else if (feature is BooleanFeature boolean)
        {
            createdVarName = WriteBooleanFeature(boolean, sb, featureNames, ref booleanCount);
        }
        else if (feature is WorkPlaneFeature wpf)
        {
            createdVarName = WriteWorkPlaneFeature(wpf, sb, featureNames);
        }
        else if (feature is BoxFeature box)
        {
            createdVarName = WriteBoxFeature(box, sb, featureNames, ref boxCount);
        }
        else if (feature is CylinderFeature cylinder)
        {
            createdVarName = WriteCylinderFeature(cylinder, sb, featureNames, ref cylinderCount);
        }
        else
        {
            // Generic fallback
            string varName = $"feature_{feature.Id.ToString("N").Substring(0, 4)}";
            featureNames[feature.Id] = varName;
            sb.AppendLine($"// Note: Unknown feature type {feature.GetType().Name}");
            sb.AppendLine($"var {varName} = /* {feature.Name} */;");
            createdVarName = varName;
        }

        if (createdVarName != null)
        {
            sb.AppendLine($"AddFeature({createdVarName});");
            sb.AppendLine();
        }
    }

    private string WriteExtrudeFeature(ExtrudeFeature extrude, StringBuilder sb, WorkPlane? lens, 
        Dictionary<Guid, string> featureNames,
        ref int cylinderCount, ref int boxCount, ref int extrudeCount)
    {
        string? returnedVarName = null;
        // Pattern Matching for Intelligent Translation
        if (extrude.Profile is CircleProfile circle)
        {
            string varName = $"cylinder_{cylinderCount++}";
            featureNames[extrude.Id] = varName;
            returnedVarName = varName;
            
            if (lens != null)
            {
                // In a real implementation, convert circle.Center (Global) to Local based on the lens Matrix.
                // For now, we mock the local coordinates by subtracting the lens origin.
                var dx = circle.Center.X - lens.Origin.X;
                var dy = circle.Center.Y - lens.Origin.Y;
                var dz = circle.Center.Z - lens.Origin.Z;
                
                sb.AppendLine($"var {varName} = CylinderFeature.FromWorkPlane(lensPlane, localCenter: new Point3D({Fmt(dx)}, {Fmt(dy)}, {Fmt(dz)}), radius: {Fmt(circle.Radius)}, height: {Fmt(extrude.Depth)});");
            }
            else
            {
                sb.AppendLine($"var {varName} = new CylinderFeature(globalBaseCenter: new Point3D({Fmt(circle.Center.X)}, {Fmt(circle.Center.Y)}, {Fmt(circle.Center.Z)}), globalDirection: new Vector3D({Fmt(extrude.Direction.X)}, {Fmt(extrude.Direction.Y)}, {Fmt(extrude.Direction.Z)}), radius: {Fmt(circle.Radius)}, height: {Fmt(extrude.Depth)});");
            }
        }
        else if (extrude.Profile is RectangleProfile rect)
        {
            string varName = $"box_{boxCount++}";
            featureNames[extrude.Id] = varName;
            returnedVarName = varName;
            
            if (lens != null)
            {
                var dx = rect.Center.X - lens.Origin.X;
                var dy = rect.Center.Y - lens.Origin.Y;
                var dz = rect.Center.Z - lens.Origin.Z;
                sb.AppendLine($"// Mock box from workplane implementation");
                sb.AppendLine($"var {varName} = BoxFeature.FromWorkPlane(lensPlane, localCenter: new Point3D({Fmt(dx)}, {Fmt(dy)}, {Fmt(dz)}), width: {Fmt(rect.Width)}, length: {Fmt(rect.Height)}, height: {Fmt(extrude.Depth)});");
            }
            else
            {
                double hw = rect.Width / 2.0;
                double hh = rect.Height / 2.0;
                double cos = Math.Cos(rect.RotationAngle);
                double sin = Math.Sin(rect.RotationAngle);
                double cx = -hw * cos - (-hh) * sin;
                double cy = -hw * sin + (-hh) * cos;
                var localCorner = new Point3D(rect.Center.X + cx, rect.Center.Y + cy, rect.Center.Z);
                var corner = rect.Plane.ToWorldSpace(localCorner);
                sb.AppendLine($"var {varName} = new BoxFeature(baseCorner: new Point3D({Fmt(corner.X)}, {Fmt(corner.Y)}, {Fmt(corner.Z)}), width: {Fmt(rect.Width)}, length: {Fmt(rect.Height)}, height: {Fmt(extrude.Depth)})");
                sb.AppendLine($"{{");
                sb.AppendLine($"    DirectionX = new Vector3D({Fmt(rect.Plane.XAxis.X)}, {Fmt(rect.Plane.XAxis.Y)}, {Fmt(rect.Plane.XAxis.Z)}),");
                sb.AppendLine($"    DirectionY = new Vector3D({Fmt(rect.Plane.YAxis.X)}, {Fmt(rect.Plane.YAxis.Y)}, {Fmt(rect.Plane.YAxis.Z)}),");
                sb.AppendLine($"    DirectionZ = new Vector3D({Fmt(rect.Plane.Normal.X)}, {Fmt(rect.Plane.Normal.Y)}, {Fmt(rect.Plane.Normal.Z)})");
                sb.AppendLine($"}};");
            }
        }
        else if (extrude.Profile is ParametricBoxFace paramBox)
        {
            string varName = $"box_{boxCount++}";
            featureNames[extrude.Id] = varName;
            returnedVarName = varName;
            
            string parentVar = featureNames.ContainsKey(paramBox.ParentBox.Id) ? featureNames[paramBox.ParentBox.Id] : "null";
            
            sb.AppendLine($"// Procedural CSG: Anchored to {parentVar}'s {paramBox.SelectedFace} face");
            sb.AppendLine($"var {varName} = new BoxFeature(baseCorner: {parentVar}.GetFaceCorner(FaceType.{paramBox.SelectedFace}), width: {parentVar}.GetFaceWidth(FaceType.{paramBox.SelectedFace}), length: {parentVar}.GetFaceLength(FaceType.{paramBox.SelectedFace}), height: {Fmt(extrude.Depth)})");
            sb.AppendLine($"{{");
            sb.AppendLine($"    DirectionX = {parentVar}.GetFaceDirectionX(FaceType.{paramBox.SelectedFace}),");
            sb.AppendLine($"    DirectionY = {parentVar}.GetFaceDirectionY(FaceType.{paramBox.SelectedFace}),");
            sb.AppendLine($"    DirectionZ = {parentVar}.GetFaceNormal(FaceType.{paramBox.SelectedFace})");
            sb.AppendLine($"}};");
        }
        else if (extrude.Profile is ParametricCylinderFace paramCyl)
        {
            string varName = $"cylinder_{cylinderCount++}";
            featureNames[extrude.Id] = varName;
            returnedVarName = varName;
            
            string parentVar = featureNames.ContainsKey(paramCyl.ParentCylinder.Id) ? featureNames[paramCyl.ParentCylinder.Id] : "null";
            
            sb.AppendLine($"// Procedural CSG: Anchored to {parentVar}'s {paramCyl.SelectedFace} face");
            sb.AppendLine($"var {varName} = new CylinderFeature(globalBaseCenter: {parentVar}.GetFaceCenter(FaceType.{paramCyl.SelectedFace}), globalDirection: {parentVar}.GetFaceNormal(FaceType.{paramCyl.SelectedFace}), radius: {Fmt(paramCyl.ParentCylinder.Radius)}, height: {Fmt(extrude.Depth)});");
        }
        else
        {
            // Generic profile fallback (Option 1)
            string profileVar = $"profile_{extrudeCount}";
            string extrudeVar = $"extrude_{extrudeCount++}";
            featureNames[extrude.Id] = extrudeVar;
            returnedVarName = extrudeVar;
            
            sb.AppendLine($"var {profileVar} = new PolygonProfile(/* Custom polygon with area {Fmt(extrude.Profile?.Area)} */);");
            sb.AppendLine($"var {extrudeVar} = new ExtrudeFeature({profileVar}, depth: {Fmt(extrude.Depth)}, direction: new Vector3D({Fmt(extrude.Direction.X)}, {Fmt(extrude.Direction.Y)}, {Fmt(extrude.Direction.Z)}));");
        }
        return returnedVarName!;
    }

    private string WriteBooleanFeature(BooleanFeature boolean, StringBuilder sb, Dictionary<Guid, string> featureNames, ref int booleanCount)
    {
        string varName = $"boolOp_{booleanCount++}";
        featureNames[boolean.Id] = varName;
        
        string baseVar = boolean.BaseFeature != null && featureNames.ContainsKey(boolean.BaseFeature.Id) ? featureNames[boolean.BaseFeature.Id] : "null";
        
        var tools = new List<string>();
        if (boolean.ToolFeatures != null)
        {
            foreach(var tool in boolean.ToolFeatures)
            {
                if (featureNames.ContainsKey(tool.Id)) tools.Add(featureNames[tool.Id]);
            }
        }
        string toolsArray = tools.Count > 0 ? $"new IFeature[] {{ {string.Join(", ", tools)} }}" : "Array.Empty<IFeature>()";

        sb.AppendLine($"var {varName} = new BooleanFeature(name: \"{boolean.Name}\", operation: BooleanOperationType.{boolean.Operation}, baseFeature: {baseVar}, toolFeatures: {toolsArray});");
        return varName;
    }

    private string WriteWorkPlaneFeature(WorkPlaneFeature wpf, StringBuilder sb, Dictionary<Guid, string> featureNames)
    {
        string varName = $"workPlane_{wpf.Id.ToString("N").Substring(0, 4)}";
        featureNames[wpf.Id] = varName;
        
        if (wpf.IsBasePlane && Math.Abs(wpf.Plane.Origin.X) < 1e-5 && Math.Abs(wpf.Plane.Origin.Y) < 1e-5 && Math.Abs(wpf.Plane.Origin.Z) < 1e-5 && 
            Math.Abs(wpf.Plane.Normal.X) < 1e-5 && Math.Abs(wpf.Plane.Normal.Y) < 1e-5 && Math.Abs(wpf.Plane.Normal.Z - 1.0) < 1e-5)
        {
            sb.AppendLine($"// Plano Base Padrão");
            sb.AppendLine($"var {varName} = new WorkPlaneFeature(WorkPlane.XY, \"{wpf.Name}\", true);");
        }
        else
        {
            sb.AppendLine($"var {varName} = new WorkPlaneFeature(new WorkPlane(origin: new Point3D({Fmt(wpf.Plane.Origin.X)}, {Fmt(wpf.Plane.Origin.Y)}, {Fmt(wpf.Plane.Origin.Z)}), normal: new Vector3D({Fmt(wpf.Plane.Normal.X)}, {Fmt(wpf.Plane.Normal.Y)}, {Fmt(wpf.Plane.Normal.Z)})), \"{wpf.Name}\"{ (wpf.IsBasePlane ? ", true" : "") });");
        }
        return varName;
    }

    private string WriteBoxFeature(BoxFeature box, StringBuilder sb, Dictionary<Guid, string> featureNames, ref int boxCount)
    {
        string varName = $"box_{boxCount++}";
        featureNames[box.Id] = varName;
        
        sb.AppendLine($"var {varName} = new BoxFeature(baseCorner: new Point3D({Fmt(box.BaseCorner.X)}, {Fmt(box.BaseCorner.Y)}, {Fmt(box.BaseCorner.Z)}), width: {Fmt(box.Width)}, length: {Fmt(box.Length)}, height: {Fmt(box.Height)})");
        sb.AppendLine($"{{");
        sb.AppendLine($"    DirectionX = new Vector3D({Fmt(box.DirectionX.X)}, {Fmt(box.DirectionX.Y)}, {Fmt(box.DirectionX.Z)}),");
        sb.AppendLine($"    DirectionY = new Vector3D({Fmt(box.DirectionY.X)}, {Fmt(box.DirectionY.Y)}, {Fmt(box.DirectionY.Z)}),");
        sb.AppendLine($"    DirectionZ = new Vector3D({Fmt(box.DirectionZ.X)}, {Fmt(box.DirectionZ.Y)}, {Fmt(box.DirectionZ.Z)})");
        sb.AppendLine($"}};");
        return varName;
    }

    private string WriteCylinderFeature(CylinderFeature cylinder, StringBuilder sb, Dictionary<Guid, string> featureNames, ref int cylinderCount)
    {
        string varName = $"cylinder_{cylinderCount++}";
        featureNames[cylinder.Id] = varName;
        
        sb.AppendLine($"var {varName} = new CylinderFeature(globalBaseCenter: new Point3D({Fmt(cylinder.GlobalBaseCenter.X)}, {Fmt(cylinder.GlobalBaseCenter.Y)}, {Fmt(cylinder.GlobalBaseCenter.Z)}), globalDirection: new Vector3D({Fmt(cylinder.GlobalDirection.X)}, {Fmt(cylinder.GlobalDirection.Y)}, {Fmt(cylinder.GlobalDirection.Z)}), radius: {Fmt(cylinder.Radius)}, height: {Fmt(cylinder.Height)});");
        return varName;
    }
}
