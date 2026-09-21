using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class CsCodeGenerator
{
    public string GenerateCode(Sketch sketch, WorkPlane? referenceLens = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Avalonia CAD Editor - C# Scene Representation");
        sb.AppendLine("using System;");
        sb.AppendLine("using AvaloniaCadEditor.Domain.Features;");
        sb.AppendLine("using AvaloniaCadEditor.Domain.ValueObjects;");
        sb.AppendLine("using AvaloniaCadEditor.Domain.Profiles;");
        sb.AppendLine();
        
        if (referenceLens != null)
        {
            sb.AppendLine("// --- Lente de Referência Ativa ---");
            var origin = referenceLens.Origin;
            var normal = referenceLens.Normal;
            sb.AppendLine($"var lensPlane = new WorkPlane(origin: new Point3D({origin.X:F2}, {origin.Y:F2}, {origin.Z:F2}), normal: new Vector3D({normal.X:F2}, {normal.Y:F2}, {normal.Z:F2}));");
            sb.AppendLine();
        }

        // We only care about features that actually form volumes.
        // If they are boolean features, they consume other features.
        // We need to write them in dependency order.
        
        var allFeatures = sketch.Features.ToList();
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
        if (feature is ExtrudeFeature extrude)
        {
            WriteExtrudeFeature(extrude, sb, lens, featureNames, ref cylinderCount, ref boxCount, ref extrudeCount);
        }
        else if (feature is BooleanFeature boolean)
        {
            WriteBooleanFeature(boolean, sb, featureNames, ref booleanCount);
        }
        else
        {
            // Generic fallback
            string varName = $"feature_{feature.Id.ToString("N").Substring(0, 4)}";
            featureNames[feature.Id] = varName;
            sb.AppendLine($"// Note: Unknown feature type {feature.GetType().Name}");
            sb.AppendLine($"var {varName} = /* {feature.Name} */;");
        }
    }

    private void WriteExtrudeFeature(ExtrudeFeature extrude, StringBuilder sb, WorkPlane? lens, 
        Dictionary<Guid, string> featureNames,
        ref int cylinderCount, ref int boxCount, ref int extrudeCount)
    {
        // Pattern Matching for Intelligent Translation
        if (extrude.Profile is CircleProfile circle)
        {
            string varName = $"cylinder_{cylinderCount++}";
            featureNames[extrude.Id] = varName;
            
            if (lens != null)
            {
                // In a real implementation, convert circle.Center (Global) to Local based on the lens Matrix.
                // For now, we mock the local coordinates by subtracting the lens origin.
                var dx = circle.Center.X - lens.Origin.X;
                var dy = circle.Center.Y - lens.Origin.Y;
                var dz = circle.Center.Z - lens.Origin.Z;
                
                sb.AppendLine($"var {varName} = CylinderFeature.FromWorkPlane(lensPlane, localCenter: new Point3D({dx:F2}, {dy:F2}, {dz:F2}), radius: {circle.Radius:F2}, height: {extrude.Depth:F2});");
            }
            else
            {
                sb.AppendLine($"var {varName} = new CylinderFeature(globalBaseCenter: new Point3D({circle.Center.X:F2}, {circle.Center.Y:F2}, {circle.Center.Z:F2}), globalDirection: new Vector3D({extrude.Direction.X:F2}, {extrude.Direction.Y:F2}, {extrude.Direction.Z:F2}), radius: {circle.Radius:F2}, height: {extrude.Depth:F2});");
            }
        }
        else if (extrude.Profile is RectangleProfile rect)
        {
            string varName = $"box_{boxCount++}";
            featureNames[extrude.Id] = varName;
            
            if (lens != null)
            {
                var dx = rect.Center.X - lens.Origin.X;
                var dy = rect.Center.Y - lens.Origin.Y;
                var dz = rect.Center.Z - lens.Origin.Z;
                sb.AppendLine($"// Mock box from workplane implementation");
                sb.AppendLine($"var {varName} = BoxFeature.FromWorkPlane(lensPlane, localCenter: new Point3D({dx:F2}, {dy:F2}, {dz:F2}), width: {rect.Width:F2}, length: {rect.Height:F2}, height: {extrude.Depth:F2});");
            }
            else
            {
                sb.AppendLine($"var {varName} = new BoxFeature(globalCenter: new Point3D({rect.Center.X:F2}, {rect.Center.Y:F2}, {rect.Center.Z:F2}), width: {rect.Width:F2}, length: {rect.Height:F2}, height: {extrude.Depth:F2});");
            }
        }
        else
        {
            // Generic profile fallback (Option 1)
            string profileVar = $"profile_{extrudeCount}";
            string extrudeVar = $"extrude_{extrudeCount++}";
            featureNames[extrude.Id] = extrudeVar;
            
            sb.AppendLine($"var {profileVar} = new PolygonProfile(/* Custom polygon with area {extrude.Profile?.Area:F2} */);");
            sb.AppendLine($"var {extrudeVar} = new ExtrudeFeature({profileVar}, depth: {extrude.Depth:F2}, direction: new Vector3D({extrude.Direction.X:F2}, {extrude.Direction.Y:F2}, {extrude.Direction.Z:F2}));");
        }
    }

    private void WriteBooleanFeature(BooleanFeature boolean, StringBuilder sb, Dictionary<Guid, string> featureNames, ref int booleanCount)
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

        sb.AppendLine($"var {varName} = new BooleanFeature(baseFeature: {baseVar}, toolFeatures: {toolsArray}, operation: BooleanOperationType.{boolean.Operation});");
    }
}
