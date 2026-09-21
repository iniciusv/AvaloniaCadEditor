using System;

namespace AvaloniaCadEditor.Domain.Settings;

public class TessellationSettings
{
    public double LinearDeflection { get; set; } = 0.1;
    public double AngularDeflection { get; set; } = 0.5;
    public int ProfileSegments { get; set; } = 32;
    public int FaceSegments { get; set; } = 36;
}
