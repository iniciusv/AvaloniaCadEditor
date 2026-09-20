using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Application.Interfaces;

public interface IGeometryKernel
{
    TriangleMesh BuildFeatureMesh(IFeature feature);
    TriangleMesh BuildSolidMesh(IEnumerable<IFeature> features);
}
