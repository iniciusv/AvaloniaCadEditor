namespace AvaloniaCadEditor.UI.Controls.Rendering;

public readonly record struct ShaderUniforms(
    int ViewLoc,
    int ProjLoc,
    int ModelLoc,
    int LightingLoc,
    int AlphaLoc
);
