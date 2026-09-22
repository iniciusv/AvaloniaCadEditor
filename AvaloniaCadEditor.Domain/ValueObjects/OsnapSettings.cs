namespace AvaloniaCadEditor.Domain.ValueObjects;

public class OsnapSettings
{
    public bool EndpointEnabled { get; set; } = true;
    public bool MidpointEnabled { get; set; } = true;
    public bool CenterEnabled { get; set; } = true;
    public bool TangentEnabled { get; set; } = true;
    
    public double SnapTolerance { get; set; } = 5.0;

    public bool GridSnapEnabled { get; set; } = true;
    public double GridIncrement { get; set; } = 1.0;

    public bool PolarSnapEnabled { get; set; } = true;
    public double PolarAngleIncrement { get; set; } = 15.0;

    public bool ColorFacesByNormal { get; set; } = false;
    public bool ColorEdgesByAxis { get; set; } = false;
}
