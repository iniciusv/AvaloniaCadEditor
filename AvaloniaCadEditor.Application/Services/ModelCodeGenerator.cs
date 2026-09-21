using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class ModelCodeGenerator
{
    public string Generate(Sketch sketch)
    {
        var sb = new StringBuilder();
        var varNames = new Dictionary<Guid, string>();
        int profileCounter = 0;
        int featureCounter = 0;

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using AvaloniaCadEditor.Domain.Profiles;");
        sb.AppendLine("using AvaloniaCadEditor.Domain.Features;");
        sb.AppendLine("using AvaloniaCadEditor.Domain.ValueObjects;");
        sb.AppendLine("using AvaloniaCadEditor.Domain.Interfaces;");
        sb.AppendLine();
        sb.AppendLine("public static class ModelDefinition");
        sb.AppendLine("{");
        sb.AppendLine("    public static void Build(IModelContext ctx)");
        sb.AppendLine("    {");

        // Collect profiles used by features
        var usedProfileIds = new HashSet<Guid>();
        foreach (var feature in sketch.Features)
        {
            if (feature is ExtrudeFeature extrude && extrude.Profile != null)
            {
                usedProfileIds.Add(extrude.Profile.Id);
            }
        }

        foreach (var profile in sketch.Profiles)
        {
            if (!usedProfileIds.Contains(profile.Id)) continue;

            profileCounter++;
            string varName = GenerateProfileCode(sb, profile, profileCounter);
            varNames[profile.Id] = varName;
        }

        foreach (var feature in sketch.Features)
        {
            // Skip WorkPlaneFeature which is normally added by Sketch constructor implicitly
            if (feature is WorkPlaneFeature) continue;

            featureCounter++;
            string? varName = GenerateFeatureCode(sb, feature, featureCounter, varNames);
            if (varName != null)
            {
                varNames[feature.Id] = varName;
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private string GenerateProfileCode(StringBuilder sb, IProfile profile, int index)
    {
        string varName = "";
        
        switch (profile.Classification)
        {
            case ProfileClassification.Circle:
                if (profile is CircleProfile circle)
                {
                    varName = $"circle{index}";
                    sb.AppendLine($"        var {varName} = ctx.CircleProfile(");
                    sb.AppendLine($"            center: new Point3D({Format(circle.Center.X)}, {Format(circle.Center.Y)}, {Format(circle.Center.Z)}),");
                    sb.AppendLine($"            radius: {Format(circle.Radius)},");
                    sb.AppendLine($"            normal: new Vector3D({Format(circle.Normal.X)}, {Format(circle.Normal.Y)}, {Format(circle.Normal.Z)}));");
                }
                break;
            case ProfileClassification.Rectangle:
                if (profile is RectangleProfile rect)
                {
                    varName = $"rect{index}";
                    sb.AppendLine($"        var {varName} = ctx.RectangleProfile(");
                    sb.AppendLine($"            center: new Point3D({Format(rect.Center.X)}, {Format(rect.Center.Y)}, {Format(rect.Center.Z)}),");
                    sb.AppendLine($"            width: {Format(rect.Width)},");
                    sb.AppendLine($"            height: {Format(rect.Height)},");
                    sb.AppendLine($"            rotationAngle: {Format(rect.RotationAngle)},");
                    sb.AppendLine($"            normal: new Vector3D({Format(rect.Normal.X)}, {Format(rect.Normal.Y)}, {Format(rect.Normal.Z)}));");
                }
                break;
            case ProfileClassification.Polygon:
                if (profile is PolygonProfile poly)
                {
                    varName = $"poly{index}";
                    sb.AppendLine($"        var {varName}_verts = new Point3D[] {{");
                    foreach (var pt in poly.Vertices)
                    {
                        sb.AppendLine($"            new Point3D({Format(pt.X)}, {Format(pt.Y)}, {Format(pt.Z)}),");
                    }
                    sb.AppendLine($"        }};");
                    sb.AppendLine($"        var {varName} = ctx.PolygonProfile({varName}_verts,");
                    sb.AppendLine($"            normal: new Vector3D({Format(poly.Normal.X)}, {Format(poly.Normal.Y)}, {Format(poly.Normal.Z)}));");
                }
                break;
            default:
                break;
        }

        sb.AppendLine();
        return varName;
    }

    private string? GenerateFeatureCode(StringBuilder sb, IFeature feature, int index, Dictionary<Guid, string> varNames)
    {
        if (feature is ExtrudeFeature extrude)
        {
            string varName = $"extrude{index}";
            string profileVar = extrude.Profile != null && varNames.TryGetValue(extrude.Profile.Id, out string? pVar) ? pVar : "null";
            
            sb.AppendLine($"        var {varName} = ctx.Extrude({profileVar},");
            sb.AppendLine($"            depth: {Format(extrude.Depth)},");
            sb.AppendLine($"            direction: new Vector3D({Format(extrude.Direction.X)}, {Format(extrude.Direction.Y)}, {Format(extrude.Direction.Z)}),");
            sb.AppendLine($"            name: \"{extrude.Name}\");");
            sb.AppendLine();
            return varName;
        }
        else if (feature is BooleanFeature boolean)
        {
            string varName = $"boolean{index}";
            string baseVar = boolean.BaseFeature != null && varNames.TryGetValue(boolean.BaseFeature.Id, out string? bVar) ? bVar : "null";
            
            var toolVars = new List<string>();
            foreach (var tool in boolean.ToolFeatures)
            {
                if (tool != null && varNames.TryGetValue(tool.Id, out string? tVar))
                {
                    toolVars.Add(tVar);
                }
            }
            string toolsArr = $"new IFeature[] {{ {string.Join(", ", toolVars)} }}";

            sb.AppendLine($"        var {varName} = ctx.Boolean(BooleanOperationType.{boolean.Operation},");
            sb.AppendLine($"            baseFeature: {baseVar},");
            sb.AppendLine($"            toolFeatures: {toolsArr},");
            sb.AppendLine($"            name: \"{boolean.Name}\");");
            sb.AppendLine();
            return varName;
        }
        
        return null;
    }

    private string Format(double value)
    {
        return value.ToString("G", CultureInfo.InvariantCulture);
    }
}
