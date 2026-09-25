using System;
using System.Text;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Application.Services;

public class FeatureToCSharpConverter
{
    private readonly StringBuilder _codeBuilder = new StringBuilder();
    private int _varCounter = 1;

    public static string GenerateCode(IFeature rootFeature, string finalVarName = "reconstructedPart")
    {
        var converter = new FeatureToCSharpConverter();
        string rootVar = converter.ProcessFeature(rootFeature);
        
        converter._codeBuilder.AppendLine();
        converter._codeBuilder.AppendLine($"AddFeature({rootVar});");
        
        return converter._codeBuilder.ToString();
    }

    private string Fmt(double val) => val.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private string ProcessFeature(IFeature feature)
    {
        if (feature is ExtrudeFeature extrude)
        {
            string profileVar = ProcessProfile(extrude.Profile);
            string extVar = $"extrude{_varCounter++}";
            
            _codeBuilder.AppendLine($"var {extVar} = new ExtrudeFeature({profileVar}, {Fmt(extrude.Height)}, new Vector3D({Fmt(extrude.Direction.X)}, {Fmt(extrude.Direction.Y)}, {Fmt(extrude.Direction.Z)}), \"{feature.Name}\");");
            return extVar;
        }
        else if (feature is CylinderFeature cylinder)
        {
            string cylVar = $"cyl{_varCounter++}";
            string pt = $"new Point3D({Fmt(cylinder.GlobalBaseCenter.X)}, {Fmt(cylinder.GlobalBaseCenter.Y)}, {Fmt(cylinder.GlobalBaseCenter.Z)})";
            string dir = $"new Vector3D({Fmt(cylinder.GlobalDirection.X)}, {Fmt(cylinder.GlobalDirection.Y)}, {Fmt(cylinder.GlobalDirection.Z)})";
            
            _codeBuilder.AppendLine($"var {cylVar} = new CylinderFeature({pt}, {dir}, {Fmt(cylinder.Radius)}, {Fmt(cylinder.Height)}, \"{feature.Name}\");");
            return cylVar;
        }
        else if (feature is BooleanFeature boolean)
        {
            string baseVar = ProcessFeature(boolean.BaseFeature);
            var toolVars = new List<string>();
            foreach (var tool in boolean.ToolFeatures)
            {
                toolVars.Add(ProcessFeature(tool));
            }
            
            string boolVar = $"boolOp{_varCounter++}";
            string toolsStr = string.Join(", ", toolVars);
            
            string methodName = boolean.Operation == BooleanOperationType.Union ? "CreateUnion" : 
                               (boolean.Operation == BooleanOperationType.Subtract ? "CreateDifference" : "CreateIntersection");
                               
            _codeBuilder.AppendLine($"var {boolVar} = BooleanFeature.{methodName}(\"{feature.Name}\", {baseVar}, {toolsStr});");
            return boolVar;
        }
        
        return "null";
    }

    private string ProcessProfile(IProfile profile)
    {
        if (profile is PolygonProfile poly)
        {
            string profileVar = $"poly{_varCounter++}";
            _codeBuilder.Append($"var {profileVar} = new PolygonProfile(new List<Point3D> {{ ");
            
            var ptsStr = new List<string>();
            foreach(var p in poly.Vertices)
            {
                ptsStr.Add($"new Point3D({Fmt(p.X)}, {Fmt(p.Y)}, {Fmt(p.Z)})");
            }
            _codeBuilder.Append(string.Join(", ", ptsStr));
            _codeBuilder.AppendLine(" });");
            
            return profileVar;
        }
        
        return "null";
    }
}
