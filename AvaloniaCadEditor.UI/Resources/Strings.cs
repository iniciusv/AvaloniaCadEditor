using System.Reflection;
using System.Resources;

namespace AvaloniaCadEditor.UI.Resources;

public static class Strings
{
    private static readonly ResourceManager ResourceManager = new ResourceManager("AvaloniaCadEditor.UI.Resources.Strings", typeof(Strings).Assembly);

    public static string AppTitle => ResourceManager.GetString("AppTitle") ?? "Avalonia CAD Editor - 3D Parametric";
    public static string Selection => ResourceManager.GetString("Selection") ?? "Selection:";
    public static string Volumes => ResourceManager.GetString("Volumes") ?? "Volumes";
    public static string Faces => ResourceManager.GetString("Faces") ?? "Faces";
    public static string SelectTool => ResourceManager.GetString("SelectTool") ?? "Select (Space)";
    public static string LineTool => ResourceManager.GetString("LineTool") ?? "Line (L)";
    public static string CircleTool => ResourceManager.GetString("CircleTool") ?? "Circle (C)";
    public static string TrimTool => ResourceManager.GetString("TrimTool") ?? "Trim (T)";
    public static string ExitSketch => ResourceManager.GetString("ExitSketch") ?? "Exit Sketch";
    public static string ImportStl => ResourceManager.GetString("ImportStl") ?? "Import STL";
    public static string ExportStl => ResourceManager.GetString("ExportStl") ?? "Export STL";
    public static string DeduceSolid => ResourceManager.GetString("DeduceSolid") ?? "✨ Deduce Solid";
    public static string DeduceSolidTooltip => ResourceManager.GetString("DeduceSolidTooltip") ?? "Deduces CSG geometry from the selected STL mesh";
    public static string ExtrudeTool => ResourceManager.GetString("ExtrudeTool") ?? "Extrude (P)";
    public static string CutTool => ResourceManager.GetString("CutTool") ?? "Cut (-)";
    public static string FuseTool => ResourceManager.GetString("FuseTool") ?? "Fuse (+)";
    public static string WorkPlaneTool => ResourceManager.GetString("WorkPlaneTool") ?? "WorkPlane (W)";
    public static string DeleteTool => ResourceManager.GetString("DeleteTool") ?? "Delete (Del)";
    public static string Osnap => ResourceManager.GetString("Osnap") ?? "OSnap";
    public static string OsnapSettings => ResourceManager.GetString("OsnapSettings") ?? "OSnap Settings";
    public static string Endpoint => ResourceManager.GetString("Endpoint") ?? "Endpoint";
    public static string Midpoint => ResourceManager.GetString("Midpoint") ?? "Midpoint";
    public static string Center => ResourceManager.GetString("Center") ?? "Center";
    public static string Tangent => ResourceManager.GetString("Tangent") ?? "Tangent";
    public static string Tolerance => ResourceManager.GetString("Tolerance") ?? "Tolerance:";
    public static string DistanceSnap => ResourceManager.GetString("DistanceSnap") ?? "Distance Snap";
    public static string Increment => ResourceManager.GetString("Increment") ?? "Increment:";
    public static string PolarAngleSnap => ResourceManager.GetString("PolarAngleSnap") ?? "Polar Angle Snap";
    public static string Angle => ResourceManager.GetString("Angle") ?? "Angle (º):";
    public static string ViewSettings => ResourceManager.GetString("ViewSettings") ?? "View Settings";
    public static string FaceColorByNormal => ResourceManager.GetString("FaceColorByNormal") ?? "Face Color By Normal";
    public static string EdgeColorByAxis => ResourceManager.GetString("EdgeColorByAxis") ?? "Edge Color By Axis";
    public static string HiddenEdges => ResourceManager.GetString("HiddenEdges") ?? "Hidden Edges";
    public static string TreeTab => ResourceManager.GetString("TreeTab") ?? "Model";
    public static string CodeTab => ResourceManager.GetString("CodeTab") ?? "Code";
    public static string IOTab => ResourceManager.GetString("IOTab") ?? "Tools";
    public static string LoadCode => ResourceManager.GetString("LoadCode") ?? "Load (.cs)";
    public static string SaveCode => ResourceManager.GetString("SaveCode") ?? "Save (.cs)";
    public static string UpdateCode => ResourceManager.GetString("UpdateCode") ?? "Update Code";
    public static string RunCode => ResourceManager.GetString("RunCode") ?? "▶ Run";
    public static string Measurement => ResourceManager.GetString("Measurement") ?? "Measurement:";
    public static string ReferenceGuides => ResourceManager.GetString("ReferenceGuides") ?? "Guides";
    public static string AnalyzeGuides => ResourceManager.GetString("AnalyzeGuides") ?? "Analyze";
    public static string GuidesAnalyzing => ResourceManager.GetString("GuidesAnalyzing") ?? "Analyzing...";
    public static string GuidesStale => ResourceManager.GetString("GuidesStale") ?? "Outdated";
}
