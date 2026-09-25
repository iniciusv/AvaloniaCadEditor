using System;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.ValueObjects;

public class LinkedWorkPlane : WorkPlane
{
    public FaceReference TargetFace { get; }
    
    private WorkPlane? _cachedPlane;
    private bool _isDirty = true;

    // Pass a dummy base WorkPlane to the parent constructor
    public LinkedWorkPlane(FaceReference targetFace) : base(Point3D.Origin, new Vector3D(0, 0, 1), new Vector3D(1, 0, 0))
    {
        TargetFace = targetFace;
    }

    private void EnsureEvaluated()
    {
        if (_isDirty || _cachedPlane == null)
        {
            _cachedPlane = TargetFace.GetWorkPlane();
            _isDirty = false;
        }
    }

    public override Point3D Origin { get { EnsureEvaluated(); return _cachedPlane!.Origin; } }
    public override Vector3D Normal { get { EnsureEvaluated(); return _cachedPlane!.Normal; } }
    public override Vector3D XAxis { get { EnsureEvaluated(); return _cachedPlane!.XAxis; } }
    public override Vector3D YAxis { get { EnsureEvaluated(); return _cachedPlane!.YAxis; } }
    
    public void MarkDirty() => _isDirty = true;
}
