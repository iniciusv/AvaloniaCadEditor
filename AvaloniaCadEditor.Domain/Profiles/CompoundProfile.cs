using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles;

public class CompoundProfile : IProfile
{
    public Guid Id { get; } = Guid.NewGuid();
    public ProfileClassification Classification => ProfileClassification.Custom;
    public IReadOnlyList<ProfileCandidate> AvailableCandidates { get; set; } = Array.Empty<ProfileCandidate>();
    
    public IReadOnlyList<IProfile> SubProfiles { get; }
    public IReadOnlyList<Guid> SourceEntityIds { get; }

    public bool IsSelected { get; set; } = false;
    public bool IsVisible { get; set; } = true;
    public WorkPlane Plane { get; set; }

    public Point3D Center
    {
        get
        {
            if (SubProfiles.Count == 0) return Point3D.Origin;
            double sumX = 0, sumY = 0, sumZ = 0;
            foreach (var p in SubProfiles)
            {
                sumX += p.Center.X;
                sumY += p.Center.Y;
                sumZ += p.Center.Z;
            }
            return new Point3D(sumX / SubProfiles.Count, sumY / SubProfiles.Count, sumZ / SubProfiles.Count);
        }
    }

    public Vector3D Normal => Plane.Normal;

    public double Area => SubProfiles.Sum(p => p.Area);

    public CompoundProfile(IEnumerable<IProfile> subProfiles)
    {
        SubProfiles = subProfiles.ToList();
        var allIds = new List<Guid>();
        foreach (var p in SubProfiles) allIds.AddRange(p.SourceEntityIds);
        SourceEntityIds = allIds.Distinct().ToList().AsReadOnly();
        
        Plane = SubProfiles.FirstOrDefault()?.Plane ?? WorkPlane.XY;
    }

    public IReadOnlyList<Point3D> GetVertices(int segments = 128)
    {
        var verts = new List<Point3D>();
        foreach (var p in SubProfiles)
        {
            verts.AddRange(p.GetVertices(segments));
        }
        return verts.AsReadOnly();
    }

    public (IReadOnlyList<Point3D> Vertices, IReadOnlyList<uint> Indices) GetTessellatedFace(int segments = 128)
    {
        var allVerts = new List<Point3D>();
        var allIndices = new List<uint>();
        
        uint offset = 0;
        foreach (var p in SubProfiles)
        {
            var (verts, indices) = p.GetTessellatedFace(segments);
            allVerts.AddRange(verts);
            
            foreach (var idx in indices)
            {
                allIndices.Add(idx + offset);
            }
            offset += (uint)verts.Count;
        }
        
        return (allVerts, allIndices);
    }

    public bool ContainsPoint(Point3D point)
    {
        return SubProfiles.Any(p => p.ContainsPoint(point));
    }

    public IProfile SwitchClassification(ProfileClassification targetClassification)
    {
        return this;
    }
}
