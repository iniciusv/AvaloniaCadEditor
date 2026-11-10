using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.Profiles.Classifiers;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class ProfileRecognitionService : IProfileRecognitionService
{
    private readonly List<IShapeClassifier> _classifiers;

    public ProfileRecognitionService(IEnumerable<IShapeClassifier>? classifiers = null)
    {
        _classifiers = classifiers?.ToList() ?? new List<IShapeClassifier>
        {
            new RectangleClassifier(),
            new PolygonClassifier()
        };
    }

    /// <summary>
    /// As regiões do esboço, plano a plano. Linhas e círculos de um mesmo plano formam um só arranjo
    /// (<see cref="SketchRegionFinder"/>): um círculo que cruza um contorno o divide em regiões. O círculo
    /// que não cruza nada continua sendo um perfil circular paramétrico.
    /// </summary>
    public IReadOnlyList<IProfile> RecognizeProfiles(Sketch sketch)
    {
        var circleProfiles = new List<IProfile>();
        var regionProfiles = new List<IProfile>();

        var drawn = sketch.Entities
            .Where(e => e.IsVisible && e is LineEntity or CircleEntity)
            .GroupBy(e => e.Plane);

        foreach (var group in drawn)
        {
            var lines = group.OfType<LineEntity>().ToList();
            var circles = group.OfType<CircleEntity>().ToList();
            var found = SketchRegionFinder.Find(lines, circles);

            circleProfiles.AddRange(found.StandaloneCircles.Select(CircleProfileOf));

            foreach (var region in found.Regions)
            {
                if (ClassifyRegion(region, group.Key) is { } profile)
                    regionProfiles.Add(profile);
            }
        }

        circleProfiles.AddRange(regionProfiles);
        return circleProfiles;
    }

    private IProfile? ClassifyRegion(SketchRegionFinder.Region region, WorkPlane plane)
    {
        var candidates = new List<ProfileCandidate>();
        foreach (var classifier in _classifiers)
        {
            var candidate = classifier.Classify(region.Vertices, region.SourceEntityIds, plane);
            // Um arco em polilinha pode dar quatro vértices em esquadro, mas não é um retângulo.
            if (candidate == null || (region.HasArcs && candidate.Classification == ProfileClassification.Rectangle))
                continue;
            candidates.Add(candidate);
        }

        if (candidates.Count == 0) return null;

        // O mais específico (mais simples) primeiro.
        var sorted = candidates.OrderBy(c => c.SpecificityRank).ToList();
        var profile = sorted[0].Factory();
        profile.AvailableCandidates = sorted;
        return profile;
    }

    private static IProfile CircleProfileOf(CircleEntity circle)
    {
        var localCenter = circle.Center;

        var circleCandidate = new ProfileCandidate(
            ProfileClassification.Circle,
            "Círculo (Paramétrico)",
            specificityRank: 1,
            factory: () => new CircleProfile(localCenter, circle.Radius, sourceEntityIds: new[] { circle.Id }, plane: circle.Plane)
        );

        var polyCandidate = new ProfileCandidate(
            ProfileClassification.Polygon,
            "Polígono Aproximado",
            specificityRank: 100,
            factory: () =>
            {
                var polyVerts = new List<Point3D>();
                int segments = 128;
                for (int i = 0; i < segments; i++)
                {
                    double theta = 2.0 * Math.PI * i / segments;
                    polyVerts.Add(new Point3D(
                        localCenter.X + circle.Radius * Math.Cos(theta),
                        localCenter.Y + circle.Radius * Math.Sin(theta),
                        localCenter.Z
                    ));
                }
                return new PolygonProfile(polyVerts, new[] { circle.Id }, plane: circle.Plane);
            }
        );

        return new CircleProfile(localCenter, circle.Radius, sourceEntityIds: new[] { circle.Id }, plane: circle.Plane)
        {
            AvailableCandidates = new List<ProfileCandidate> { circleCandidate, polyCandidate }
        };
    }
}
