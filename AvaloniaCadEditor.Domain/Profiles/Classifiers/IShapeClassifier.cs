using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles.Classifiers;

public interface IShapeClassifier
{
    ProfileCandidate? Classify(IReadOnlyList<Point3D> loopVertices, IReadOnlyList<Guid> sourceEntityIds);
}
