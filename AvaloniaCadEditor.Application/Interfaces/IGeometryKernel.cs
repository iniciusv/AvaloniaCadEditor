using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;

using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Application.Interfaces;

public interface IGeometryKernel
{
    TriangleMesh BuildFeatureMesh(IFeature feature, TessellationSettings? settings = null);
    TriangleMesh BuildSolidMesh(IEnumerable<IFeature> features, TessellationSettings? settings = null);
    void InvalidateShape(System.Guid featureId);
    void InvalidateAll();
}
