using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

public class ParametricCylinderFace : IProfile
{
    public Guid Id { get; } = Guid.NewGuid();
    
    public ProfileClassification Classification => ProfileClassification.Circle;
    
    public IReadOnlyList<ProfileCandidate> AvailableCandidates { get; set; } = Array.Empty<ProfileCandidate>();
    
    public IReadOnlyList<Guid> SourceEntityIds { get; } = Array.Empty<Guid>();
    
    public WorkPlane Plane { get; set; }
    
    public CylinderFeature ParentCylinder { get; }
    public FaceType SelectedFace { get; }
    
    public Point3D Center => ParentCylinder.GetFaceCenter(SelectedFace);
    
    public Vector3D Normal => ParentCylinder.GetFaceNormal(SelectedFace);
    
    public double Area => Math.PI * ParentCylinder.Radius * ParentCylinder.Radius;
    
    public bool IsSelected { get; set; }
    public bool IsVisible { get; set; } = true;

    public ParametricCylinderFace(CylinderFeature parentCylinder, FaceType selectedFace)
    {
        ParentCylinder = parentCylinder ?? throw new ArgumentNullException(nameof(parentCylinder));
        SelectedFace = selectedFace;
        Plane = new WorkPlane(Normal, Center);
    }

    public IReadOnlyList<Point3D> GetVertices(int segments = 128)
    {
        // Calculate the circle vertices using the Center and Normal from the ParentCylinder
        var circleProfile = new CircleProfile(Point3D.Origin, ParentCylinder.Radius, Normal, null, Plane);
        return circleProfile.GetVertices(segments);
    }

    public (IReadOnlyList<Point3D> Vertices, IReadOnlyList<uint> Indices) GetTessellatedFace(int segments = 128)
    {
        var circleProfile = new CircleProfile(Point3D.Origin, ParentCylinder.Radius, Normal, null, Plane);
        return circleProfile.GetTessellatedFace(segments);
    }

    public bool ContainsPoint(Point3D point)
    {
        var circleProfile = new CircleProfile(Point3D.Origin, ParentCylinder.Radius, Normal, null, Plane);
        return circleProfile.ContainsPoint(point);
    }

    public IProfile SwitchClassification(ProfileClassification targetClassification)
    {
        return this;
    }
}
