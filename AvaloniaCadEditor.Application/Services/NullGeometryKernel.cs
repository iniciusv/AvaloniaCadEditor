using System.Collections.Generic;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;

using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Application.Services;

public class NullGeometryKernel : IGeometryKernel
{
    public TriangleMesh BuildFeatureMesh(IFeature feature, TessellationSettings? settings = null) => TriangleMesh.Empty;
    public TriangleMesh BuildSolidMesh(IEnumerable<IFeature> features, TessellationSettings? settings = null)
    {
        return TriangleMesh.Empty;
    }

    public void InvalidateShape(Guid featureId) { }
    public void InvalidateAll() { }
}
