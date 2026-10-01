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

    /// <summary>
    /// Chamado quando a cena é reexecutada (features novas, com Ids novos). Descarta o que é
    /// indexado por Id, mas pode manter malhas indexadas pela geometria (<see cref="FeatureFingerprint"/>)
    /// para reaproveitá-las. Padrão: o mesmo que <see cref="InvalidateAll"/>.
    /// </summary>
    void BeginSceneRebuild() => InvalidateAll();
}
