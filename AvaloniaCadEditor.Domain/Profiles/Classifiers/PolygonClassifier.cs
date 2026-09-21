using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles.Classifiers;

public class PolygonClassifier : IShapeClassifier
{
    public ProfileCandidate? Classify(IReadOnlyList<Point3D> loopVertices, IReadOnlyList<Guid> sourceEntityIds, WorkPlane plane)
    {
        if (loopVertices == null || loopVertices.Count < 3)
            return null;

        return new ProfileCandidate(
            ProfileClassification.Polygon,
            "Polígono Geral (Vértices Livres)",
            specificityRank: 100, // Fallback
            factory: () => new PolygonProfile(loopVertices, sourceEntityIds, plane: plane)
        );
    }
}
