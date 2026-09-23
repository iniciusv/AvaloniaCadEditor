using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

public class ParametricBoxFace : IProfile
{
    public Guid Id { get; } = Guid.NewGuid();
    
    public ProfileClassification Classification => ProfileClassification.Rectangle;
    
    public IReadOnlyList<ProfileCandidate> AvailableCandidates { get; set; } = Array.Empty<ProfileCandidate>();
    
    public IReadOnlyList<Guid> SourceEntityIds { get; } = Array.Empty<Guid>();
    
    public WorkPlane Plane { get; set; }
    
    public BoxFeature ParentBox { get; }
    public FaceType SelectedFace { get; }
    
    public Point3D Center => ParentBox.GetFaceCenter(SelectedFace);
    
    public Vector3D Normal => ParentBox.GetFaceNormal(SelectedFace);
    
    public double Area => ParentBox.GetFaceWidth(SelectedFace) * ParentBox.GetFaceLength(SelectedFace);
    
    public bool IsSelected { get; set; }
    public bool IsVisible { get; set; } = true;

    public ParametricBoxFace(BoxFeature parentBox, FaceType selectedFace)
    {
        ParentBox = parentBox ?? throw new ArgumentNullException(nameof(parentBox));
        SelectedFace = selectedFace;
        Plane = new WorkPlane(Center, Normal, ParentBox.GetFaceDirectionX(SelectedFace));
    }

    public IReadOnlyList<Point3D> GetVertices(int segments = 128)
    {
        // Calculate the rectangle vertices using the Center and Normal from the ParentBox
        // This is only for preview/rendering purposes, not for CSG history.
        double w = ParentBox.GetFaceWidth(SelectedFace);
        double h = ParentBox.GetFaceLength(SelectedFace);
        
        var rectProfile = new RectangleProfile(Point3D.Origin, w, h, 0, Normal, null, Plane);
        return rectProfile.GetVertices(segments);
    }

    public (IReadOnlyList<Point3D> Vertices, IReadOnlyList<uint> Indices) GetTessellatedFace(int segments = 128)
    {
        var rectProfile = new RectangleProfile(Point3D.Origin, ParentBox.GetFaceWidth(SelectedFace), ParentBox.GetFaceLength(SelectedFace), 0, Normal, null, Plane);
        return rectProfile.GetTessellatedFace(segments);
    }

    public bool ContainsPoint(Point3D point)
    {
        var rectProfile = new RectangleProfile(Point3D.Origin, ParentBox.GetFaceWidth(SelectedFace), ParentBox.GetFaceLength(SelectedFace), 0, Normal, null, Plane);
        return rectProfile.ContainsPoint(point);
    }

    public IProfile SwitchClassification(ProfileClassification targetClassification)
    {
        return this;
    }
}
