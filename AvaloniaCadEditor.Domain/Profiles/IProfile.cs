using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

public interface IProfile
{
    Guid Id { get; }
    ProfileClassification Classification { get; }
    IReadOnlyList<ProfileCandidate> AvailableCandidates { get; set; }
    IReadOnlyList<Guid> SourceEntityIds { get; }
    Point3D Center { get; }
    Vector3D Normal { get; }
    double Area { get; }
    bool IsSelected { get; set; }

    IReadOnlyList<Point3D> GetVertices();
    IProfile SwitchClassification(ProfileClassification targetClassification);
}
