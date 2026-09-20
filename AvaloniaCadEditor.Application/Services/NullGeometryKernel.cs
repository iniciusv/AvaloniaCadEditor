using System.Collections.Generic;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Application.Services;

public class NullGeometryKernel : IGeometryKernel
{
    public TriangleMesh BuildFeatureMesh(IFeature feature) => TriangleMesh.Empty;
    public TriangleMesh BuildSolidMesh(IEnumerable<IFeature> features) => TriangleMesh.Empty;
}
