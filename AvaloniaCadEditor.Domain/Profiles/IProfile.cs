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
    bool IsVisible { get; set; }

    IReadOnlyList<Point3D> GetVertices(int segments = 32);
    (IReadOnlyList<Point3D> Vertices, IReadOnlyList<uint> Indices) GetTessellatedFace(int segments = 36);
    bool ContainsPoint(Point3D point);
    IProfile SwitchClassification(ProfileClassification targetClassification);
}
