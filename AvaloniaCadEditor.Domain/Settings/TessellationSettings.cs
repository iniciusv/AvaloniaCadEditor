using System;

namespace AvaloniaCadEditor.Domain.Settings;

public class TessellationSettings
{
    public double LinearDeflection { get; set; } = 0.1;
    public double AngularDeflection { get; set; } = 0.5;
    public int ProfileSegments { get; set; } = 32;
    public int FaceSegments { get; set; } = 36;
    
    // Visibilidade
    public bool ShowInternalEdges { get; set; } = false;
    public float CreaseAngle { get; set; } = 30.0f;
    
    // Cores em HEX (AARRGGBB)
    public string FeatureEdgeColor { get; set; } = "#FF1F2938";
    public string InternalEdgeColor { get; set; } = "#40808080";
}
