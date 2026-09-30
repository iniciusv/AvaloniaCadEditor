using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ReferenceGuides;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.ReferenceGuides;

/// <summary>
/// Default <see cref="IReferenceGuideService"/>. Pure geometry over the feature graph:
///
/// 1. Collect holes (subtracted/transient cylinders) and wall solids (extrudes/boxes).
/// 2. Group them by extrusion/hole axis, and build a 2D (u, v) frame for each axis.
/// 3. Project every hole center and every axis-aligned profile edge/arc-center into that
///    frame, then cluster the projected coordinates along each of the two in-plane axes.
/// 4. Turn the clusters into guide lines, step/proximity dimensions, symmetry lines and
///    near-miss warnings.
///
/// Everything here is a pure function of its input features - no I/O, no shared mutable
/// state - so it is safe to run on a background thread and to unit test directly.
/// </summary>
public sealed class ReferenceGuideService : IReferenceGuideService
{
    public ReferenceGuideSet Analyze(
        IReadOnlyList<IFeature> features,
        ReferenceGuideOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new ReferenceGuideOptions();
        if (features.Count == 0) return ReferenceGuideSet.Empty;

        var holes = new List<CylinderFeature>();
        var walls = new List<IFeature>();
        var visited = new HashSet<Guid>();
        foreach (var feature in features)
        {
            CollectFeatures(feature, isSubtractTool: false, visited, holes, walls);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (holes.Count > opts.MaxHoles)
        {
            holes = holes.Take(opts.MaxHoles).ToList();
        }

        if (holes.Count == 0 && walls.Count == 0) return ReferenceGuideSet.Empty;

        var lines = new List<GuideLine>();
        var dimensions = new List<GuideDimension>();

        foreach (var group in GroupByDirection(holes, walls))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnalyzeGroup(group, opts, lines, dimensions);
        }

        return new ReferenceGuideSet(lines, dimensions);
    }

    // --- Feature collection -------------------------------------------------

    private static void CollectFeatures(
        IFeature? feature,
        bool isSubtractTool,
        HashSet<Guid> visited,
        List<CylinderFeature> holes,
        List<IFeature> walls)
    {
        if (feature == null || !visited.Add(feature.Id)) return;

        switch (feature)
        {
            case CylinderFeature cylinder:
                // A cylinder is a "hole" either because it was explicitly marked as a transient
                // tool, or because it is being subtracted from something as part of a boolean op.
                if (isSubtractTool || cylinder.IsTransientTool)
                {
                    holes.Add(cylinder);
                }
                return;

            case BooleanFeature boolean:
                foreach (var b in boolean.BaseFeatures)
                {
                    CollectFeatures(b, isSubtractTool, visited, holes, walls);
                }
                var subtractHere = boolean.Operation == BooleanOperationType.Subtract;
                foreach (var tool in boolean.ToolFeatures)
                {
                    CollectFeatures(tool, subtractHere, visited, holes, walls);
                }
                return;

            case ExtrudeFeature:
            case BoxFeature:
                walls.Add(feature);
                return;

            default:
                // Unknown feature kind: fall back to the generic dependency graph so future
                // feature types are still traversed (they just won't contribute guides).
                foreach (var dependency in feature.Dependencies)
                {
                    CollectFeatures(dependency, isSubtractTool, visited, holes, walls);
                }
                return;
        }
    }

    // --- Direction grouping ---------------------------------------------------

    private sealed class DirectionGroup
    {
        public Vector3D Direction;
        public readonly List<CylinderFeature> Holes = new();
        public readonly List<IFeature> Walls = new();
    }

    private static List<DirectionGroup> GroupByDirection(List<CylinderFeature> holes, List<IFeature> walls)
    {
        var groups = new Dictionary<(int, int, int), DirectionGroup>();

        DirectionGroup GroupFor(Vector3D rawDirection)
        {
            var canonical = Canonicalize(rawDirection);
            var key = (RoundKey(canonical.X), RoundKey(canonical.Y), RoundKey(canonical.Z));
            if (!groups.TryGetValue(key, out var group))
            {
                group = new DirectionGroup { Direction = canonical };
                groups[key] = group;
            }
            return group;
        }

        foreach (var hole in holes)
        {
            GroupFor(hole.GlobalDirection).Holes.Add(hole);
        }
        foreach (var wall in walls)
        {
            var normal = GetSolidNormal(wall);
            if (normal is { } n)
            {
                GroupFor(n).Walls.Add(wall);
            }
        }

        return groups.Values.ToList();
    }

    private static int RoundKey(double value) => (int)Math.Round(value * 1000.0);

    /// <summary>Normalizes a direction and flips its sign so opposite-facing axes (e.g. two
    /// holes drilled from either side) land in the same group.</summary>
    private static Vector3D Canonicalize(Vector3D direction)
    {
        var n = direction.Normalize();
        double ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
        double dominant = ax >= ay && ax >= az ? n.X : (ay >= az ? n.Y : n.Z);
        return dominant < 0 ? n * -1 : n;
    }

    private static Vector3D? GetSolidNormal(IFeature wall) => wall switch
    {
        ExtrudeFeature ext => ext.Direction.Normalize(),
        BoxFeature box => box.DirectionZ.Normalize(),
        _ => null
    };

    private static IReadOnlyList<Point3D> GetBoundaryVertices(IFeature wall) => wall switch
    {
        ExtrudeFeature ext => ext.Profile.GetVertices(),
        BoxFeature box => box.GetVertices().Take(4).ToList(),
        _ => Array.Empty<Point3D>()
    };

    private static double GetSolidTop(IFeature wall, WorkPlane plane) => wall switch
    {
        ExtrudeFeature ext => plane.ToLocalSpace(ext.TopCenter).Z,
        BoxFeature box => plane.ToLocalSpace(box.GetFaceCenter(FaceType.Top)).Z,
        _ => 0
    };

    // --- Per-group analysis -----------------------------------------------

    private enum CandidateKind { Hole, Wall }

    /// <summary>One point projected into a group's (u, v) frame, contributing to the
    /// clustering of one axis. <see cref="Coord"/> is the value being clustered (u for the
    /// X-axis pass, v for the Y-axis pass); <see cref="Cross"/> is the other in-plane
    /// coordinate, used to size the resulting guide line/dimension.</summary>
    private readonly record struct PlanarCandidate(double Coord, double Cross, double Top, Guid FeatureId, CandidateKind Kind);

    private readonly record struct ArcInfo(double CenterU, double CenterV, double Radius);

    private sealed class TightCluster
    {
        public double Coordinate;
        public double CrossMin;
        public double CrossMax;
        public double Top;
        public List<Guid> HoleFeatureIds = new();
        public List<Guid> WallFeatureIds = new();

        /// <summary>Raw candidate count backing this cluster (vertices/edge-endpoints/hole
        /// centers), before deduplicating by feature. Used only to tell a well-supported
        /// coordinate from a lone outlier when reporting a near-miss - never to decide which
        /// side is "correct", since that depends on design intent this service can't see.</summary>
        public int MemberCount;

        public IReadOnlyList<Guid> AllFeatureIds => HoleFeatureIds.Concat(WallFeatureIds).Distinct().ToList();
    }

    private static void AnalyzeGroup(DirectionGroup group, ReferenceGuideOptions opts, List<GuideLine> lines, List<GuideDimension> dimensions)
    {
        if (group.Holes.Count == 0 && group.Walls.Count == 0) return;

        var plane = new WorkPlane(group.Direction, Point3D.Origin);

        var xCandidates = new List<PlanarCandidate>();
        var yCandidates = new List<PlanarCandidate>();

        foreach (var hole in group.Holes)
        {
            var baseLocal = plane.ToLocalSpace(hole.GlobalBaseCenter);
            var topLocal = plane.ToLocalSpace(hole.GetFaceCenter(FaceType.Top));
            double top = Math.Max(baseLocal.Z, topLocal.Z);
            xCandidates.Add(new PlanarCandidate(baseLocal.X, baseLocal.Y, top, hole.Id, CandidateKind.Hole));
            yCandidates.Add(new PlanarCandidate(baseLocal.Y, baseLocal.X, top, hole.Id, CandidateKind.Hole));
        }

        foreach (var wall in group.Walls)
        {
            AddWallCandidates(wall, plane, opts, xCandidates, yCandidates, lines);
        }

        if (xCandidates.Count == 0 && yCandidates.Count == 0) return;

        double top1 = ComputeSharedTop(xCandidates, yCandidates);

        var xClusters = ClusterAxis(xCandidates, opts);
        var yClusters = ClusterAxis(yCandidates, opts);

        // Clusters caught in a near-miss get their own full-span red line from EmitNearMisses
        // instead of a normal-colored one here, so the conflict is what the user actually sees
        // (two near-overlapping red lines), not two ordinary-looking lines plus an easy-to-miss
        // stub between them.
        var xAmbiguous = new HashSet<TightCluster>(xClusters.AmbiguousNeighborhoods.SelectMany(n => n));
        var yAmbiguous = new HashSet<TightCluster>(yClusters.AmbiguousNeighborhoods.SelectMany(n => n));

        EmitAxisLines(xClusters.Tight.Where(c => !xAmbiguous.Contains(c)).ToList(), isXAxis: true, plane, top1, opts, lines);
        EmitAxisLines(yClusters.Tight.Where(c => !yAmbiguous.Contains(c)).ToList(), isXAxis: false, plane, top1, opts, lines);

        EmitNearMisses(xClusters.AmbiguousNeighborhoods, isXAxis: true, plane, top1, opts, lines, dimensions);
        EmitNearMisses(yClusters.AmbiguousNeighborhoods, isXAxis: false, plane, top1, opts, lines, dimensions);

        EmitRowDimensions(xClusters.Tight, isXAxis: true, plane, top1, opts, dimensions);
        EmitRowDimensions(yClusters.Tight, isXAxis: false, plane, top1, opts, dimensions);

        EmitSymmetry(xClusters.Tight, isXAxis: true, plane, top1, opts, lines);
        EmitSymmetry(yClusters.Tight, isXAxis: false, plane, top1, opts, lines);
    }

    /// <summary>A single elevation, slightly above the tallest thing in the group, so every
    /// guide line/label in this group renders in a common, unobstructed plane.</summary>
    private static double ComputeSharedTop(List<PlanarCandidate> xCandidates, List<PlanarCandidate> yCandidates)
    {
        double maxTop = 0;
        double minCoord = double.MaxValue, maxCoord = double.MinValue;
        bool any = false;
        foreach (var c in xCandidates.Concat(yCandidates))
        {
            if (!any || c.Top > maxTop) maxTop = c.Top;
            if (c.Coord < minCoord) minCoord = c.Coord;
            if (c.Coord > maxCoord) maxCoord = c.Coord;
            any = true;
        }
        double span = any ? Math.Max(maxCoord - minCoord, 1.0) : 1.0;
        return maxTop + Math.Max(span * 0.01, 1e-3);
    }

    private static void AddWallCandidates(
        IFeature wall,
        WorkPlane plane,
        ReferenceGuideOptions opts,
        List<PlanarCandidate> xCandidates,
        List<PlanarCandidate> yCandidates,
        List<GuideLine> lines)
    {
        var worldVerts = GetBoundaryVertices(wall);
        if (worldVerts.Count < 2) return;

        var local = worldVerts.Select(plane.ToLocalSpace).ToList();
        double top = GetSolidTop(wall, plane);

        int n = local.Count;
        for (int i = 0; i < n; i++)
        {
            var p1 = local[i];
            var p2 = local[(i + 1) % n];
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;

            if (Math.Abs(dx) / len < opts.EdgeAngleTolerance)
            {
                xCandidates.Add(new PlanarCandidate(p1.X, p1.Y, top, wall.Id, CandidateKind.Wall));
                xCandidates.Add(new PlanarCandidate(p2.X, p2.Y, top, wall.Id, CandidateKind.Wall));
            }
            else if (Math.Abs(dy) / len < opts.EdgeAngleTolerance)
            {
                yCandidates.Add(new PlanarCandidate(p1.Y, p1.X, top, wall.Id, CandidateKind.Wall));
                yCandidates.Add(new PlanarCandidate(p2.Y, p2.X, top, wall.Id, CandidateKind.Wall));
            }
        }

        foreach (var arc in DetectArcs(local, opts))
        {
            xCandidates.Add(new PlanarCandidate(arc.CenterU, arc.CenterV, top, wall.Id, CandidateKind.Wall));
            yCandidates.Add(new PlanarCandidate(arc.CenterV, arc.CenterU, top, wall.Id, CandidateKind.Wall));

            double s = Math.Max(arc.Radius * 0.08, opts.LineMargin * 0.25);
            var h1 = plane.ToWorldSpace(new Point3D(arc.CenterU - s, arc.CenterV, top));
            var h2 = plane.ToWorldSpace(new Point3D(arc.CenterU + s, arc.CenterV, top));
            var v1 = plane.ToWorldSpace(new Point3D(arc.CenterU, arc.CenterV - s, top));
            var v2 = plane.ToWorldSpace(new Point3D(arc.CenterU, arc.CenterV + s, top));
            lines.Add(new GuideLine(GuideKind.ArcCenter, h1, h2, arc.CenterU, new[] { wall.Id }));
            lines.Add(new GuideLine(GuideKind.ArcCenter, v1, v2, arc.CenterV, new[] { wall.Id }));
        }
    }

    // --- Arc detection -------------------------------------------------------

    /// <summary>Finds runs of consecutive vertices whose circumcenters agree, i.e. polyline
    /// approximations of a circular arc, and returns each run's averaged center/radius.</summary>
    private static List<ArcInfo> DetectArcs(List<Point3D> local, ReferenceGuideOptions opts)
    {
        var result = new List<ArcInfo>();
        int n = local.Count;
        if (n < opts.MinArcRunLength + 2) return result;

        var centers = new Point3D?[n];
        var radii = new double[n];
        for (int i = 1; i < n - 1; i++)
        {
            var c = CircumCenter2D(local[i - 1], local[i], local[i + 1]);
            if (c == null) continue;
            centers[i] = c;
            radii[i] = Distance2D(c.Value, local[i]);
        }

        int runStart = centers[1] != null ? 1 : -1;
        for (int i = 2; i < n - 1; i++)
        {
            bool continuesRun = false;
            if (runStart >= 0 && centers[i] != null && centers[i - 1] != null)
            {
                double centerDist = Distance2D(centers[i]!.Value, centers[i - 1]!.Value);
                double refRadius = Math.Max(radii[i], 1e-6);
                double radiusDiff = Math.Abs(radii[i] - radii[i - 1]);
                continuesRun = centerDist / refRadius < opts.ArcCenterTolerance
                               && radiusDiff / refRadius < opts.ArcRadiusTolerance;
            }

            if (continuesRun) continue;

            if (runStart >= 0) FinalizeRun(runStart, i - 1, centers, local, opts, result);
            runStart = centers[i] != null ? i : -1;
        }
        if (runStart >= 0) FinalizeRun(runStart, n - 2, centers, local, opts, result);

        return result;
    }

    /// <summary>
    /// Turns a confirmed run of matching circumcenters into a single (center, radius). Rather
    /// than averaging the per-triple circumcenters - which is numerically unstable right where
    /// the arc is nearly tangent to a straight edge, since those triples are close to collinear
    /// - this refits a circle by least squares over every vertex actually spanned by the run.
    /// That is far less sensitive to the 3-decimal rounding of the input vertices, so two arcs
    /// that are meant to share a center (e.g. both ends of a slot) reliably compute to centers
    /// within a thousandth of each other instead of drifting into a spurious near-miss.
    /// </summary>
    private static void FinalizeRun(int start, int end, Point3D?[] centers, List<Point3D> local, ReferenceGuideOptions opts, List<ArcInfo> result)
    {
        int count = end - start + 1;
        if (count < opts.MinArcRunLength) return;

        // The run's vertices span one wider than [start, end]: centers[i] is the circumcenter
        // of local[i-1..i+1], so the run actually covers local[start-1 .. end+1].
        int firstVertex = Math.Max(0, start - 1);
        int lastVertex = Math.Min(local.Count - 1, end + 1);
        var points = local.GetRange(firstVertex, lastVertex - firstVertex + 1);

        var fit = FitCircleLeastSquares(points);
        if (fit == null)
        {
            // Degenerate fit (shouldn't happen for a real arc): fall back to the old average.
            double cu0 = 0, cv0 = 0, r0 = 0;
            for (int i = start; i <= end; i++)
            {
                cu0 += centers[i]!.Value.X;
                cv0 += centers[i]!.Value.Y;
                r0 += Distance2D(centers[i]!.Value, local[i]);
            }
            result.Add(new ArcInfo(cu0 / count, cv0 / count, r0 / count));
            return;
        }

        result.Add(new ArcInfo(fit.Value.CenterU, fit.Value.CenterV, fit.Value.Radius));
    }

    /// <summary>Algebraic (Kása) least-squares circle fit: solves the linear system for
    /// x²+y²+Dx+Ey+F=0 over all given points, then center=(-D/2,-E/2), radius from F. Far more
    /// stable than averaging circumcenters of individual point triples.</summary>
    private static (double CenterU, double CenterV, double Radius)? FitCircleLeastSquares(IReadOnlyList<Point3D> points)
    {
        if (points.Count < 3) return null;

        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0, sz = 0;
        int n = points.Count;
        foreach (var p in points)
        {
            double x = p.X, y = p.Y, z = x * x + y * y;
            sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
            sxz += x * z; syz += y * z; sz += z;
        }

        // Normal equations for [D, E, F] minimizing sum (x²+y² + Dx + Ey + F)².
        double m00 = sxx, m01 = sxy, m02 = sx, b0 = -sxz;
        double m10 = sxy, m11 = syy, m12 = sy, b1 = -syz;
        double m20 = sx, m21 = sy, m22 = n, b2 = -sz;

        if (!Solve3x3(m00, m01, m02, b0, m10, m11, m12, b1, m20, m21, m22, b2, out double d, out double e, out double f))
        {
            return null;
        }

        double centerU = -d / 2.0;
        double centerV = -e / 2.0;
        double radiusSquared = (d * d + e * e) / 4.0 - f;
        if (radiusSquared <= 0) return null;

        return (centerU, centerV, Math.Sqrt(radiusSquared));
    }

    private static bool Solve3x3(
        double a00, double a01, double a02, double b0,
        double a10, double a11, double a12, double b1,
        double a20, double a21, double a22, double b2,
        out double x, out double y, out double z)
    {
        double det = a00 * (a11 * a22 - a12 * a21)
                   - a01 * (a10 * a22 - a12 * a20)
                   + a02 * (a10 * a21 - a11 * a20);

        if (Math.Abs(det) < 1e-9)
        {
            x = y = z = 0;
            return false;
        }

        double detX = b0 * (a11 * a22 - a12 * a21)
                    - a01 * (b1 * a22 - a12 * b2)
                    + a02 * (b1 * a21 - a11 * b2);
        double detY = a00 * (b1 * a22 - a12 * b2)
                    - b0 * (a10 * a22 - a12 * a20)
                    + a02 * (a10 * b2 - b1 * a20);
        double detZ = a00 * (a11 * b2 - b1 * a21)
                    - a01 * (a10 * b2 - b1 * a20)
                    + b0 * (a10 * a21 - a11 * a20);

        x = detX / det;
        y = detY / det;
        z = detZ / det;
        return true;
    }

    private static double Distance2D(Point3D a, Point3D b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Point3D? CircumCenter2D(Point3D a, Point3D b, Point3D c)
    {
        double ax = a.X, ay = a.Y, bx = b.X, by = b.Y, cx = c.X, cy = c.Y;
        double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (Math.Abs(d) < 1e-9) return null; // collinear, no circle

        double ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
        double uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
        return new Point3D(ux, uy, 0);
    }

    // --- Axis clustering -----------------------------------------------------

    private sealed class ClusterResult
    {
        public readonly List<TightCluster> Tight = new();
        public readonly List<List<TightCluster>> AmbiguousNeighborhoods = new();
    }

    /// <summary>
    /// Two-level clustering: first group values within <see cref="ReferenceGuideOptions.NearMissTolerance"/>
    /// of each other into a "neighborhood", then split each neighborhood into tight sub-clusters
    /// using the much stricter <see cref="ReferenceGuideOptions.AlignTolerance"/>. A neighborhood
    /// that splits into more than one tight cluster is a near-miss: values close enough to look
    /// intentional, but not actually equal.
    /// </summary>
    private static ClusterResult ClusterAxis(List<PlanarCandidate> candidates, ReferenceGuideOptions opts)
    {
        var result = new ClusterResult();
        if (candidates.Count == 0) return result;

        var sorted = candidates.OrderBy(c => c.Coord).ToList();

        var neighborhoods = new List<List<PlanarCandidate>>();
        var currentNeighborhood = new List<PlanarCandidate> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Coord - sorted[i - 1].Coord <= opts.NearMissTolerance)
            {
                currentNeighborhood.Add(sorted[i]);
            }
            else
            {
                neighborhoods.Add(currentNeighborhood);
                currentNeighborhood = new List<PlanarCandidate> { sorted[i] };
            }
        }
        neighborhoods.Add(currentNeighborhood);

        foreach (var neighborhood in neighborhoods)
        {
            var tightGroups = new List<List<PlanarCandidate>>();
            var currentTight = new List<PlanarCandidate> { neighborhood[0] };
            for (int i = 1; i < neighborhood.Count; i++)
            {
                if (neighborhood[i].Coord - neighborhood[i - 1].Coord <= opts.AlignTolerance)
                {
                    currentTight.Add(neighborhood[i]);
                }
                else
                {
                    tightGroups.Add(currentTight);
                    currentTight = new List<PlanarCandidate> { neighborhood[i] };
                }
            }
            tightGroups.Add(currentTight);

            var tightClusters = tightGroups.Select(BuildTightCluster).ToList();
            result.Tight.AddRange(tightClusters);

            if (tightClusters.Count > 1)
            {
                result.AmbiguousNeighborhoods.Add(tightClusters);
            }
        }

        return result;
    }

    private static TightCluster BuildTightCluster(List<PlanarCandidate> members) => new()
    {
        Coordinate = members.Average(m => m.Coord),
        CrossMin = members.Min(m => m.Cross),
        CrossMax = members.Max(m => m.Cross),
        Top = members.Max(m => m.Top),
        HoleFeatureIds = members.Where(m => m.Kind == CandidateKind.Hole).Select(m => m.FeatureId).Distinct().ToList(),
        WallFeatureIds = members.Where(m => m.Kind == CandidateKind.Wall).Select(m => m.FeatureId).Distinct().ToList(),
        MemberCount = members.Count
    };

    // --- Emitting lines/dimensions --------------------------------------------

    private static bool TryGetLineKind(TightCluster cluster, out GuideKind kind)
    {
        if (cluster.HoleFeatureIds.Count >= 2) { kind = GuideKind.HoleRow; return true; }
        if (cluster.WallFeatureIds.Count >= 1) { kind = GuideKind.Wall; return true; }
        kind = default;
        return false;
    }

    private static void EmitAxisLines(List<TightCluster> clusters, bool isXAxis, WorkPlane plane, double top, ReferenceGuideOptions opts, List<GuideLine> lines)
    {
        foreach (var cluster in clusters)
        {
            if (!TryGetLineKind(cluster, out var kind)) continue;

            double crossMin = cluster.CrossMin - opts.LineMargin;
            double crossMax = cluster.CrossMax + opts.LineMargin;
            var start = MakePoint(cluster.Coordinate, crossMin, top, isXAxis, plane);
            var end = MakePoint(cluster.Coordinate, crossMax, top, isXAxis, plane);
            lines.Add(new GuideLine(kind, start, end, cluster.Coordinate, cluster.AllFeatureIds));
        }
    }

    private static void EmitNearMisses(
        List<List<TightCluster>> neighborhoods,
        bool isXAxis,
        WorkPlane plane,
        double top,
        ReferenceGuideOptions opts,
        List<GuideLine> lines,
        List<GuideDimension> dimensions)
    {
        foreach (var neighborhood in neighborhoods)
        {
            var sorted = neighborhood.OrderBy(c => c.Coordinate).ToList();

            // Draw a full-span red line for every cluster in the conflict, not just a stub
            // between the two points - the stub's length is the mismatch itself (often well
            // under a millimeter at scene scale), so on its own it is effectively invisible.
            foreach (var cluster in sorted)
            {
                double crossMin = cluster.CrossMin - opts.LineMargin;
                double crossMax = cluster.CrossMax + opts.LineMargin;
                var start = MakePoint(cluster.Coordinate, crossMin, top, isXAxis, plane);
                var end = MakePoint(cluster.Coordinate, crossMax, top, isXAxis, plane);
                lines.Add(new GuideLine(GuideKind.NearMiss, start, end, cluster.Coordinate, cluster.AllFeatureIds));
            }

            for (int i = 1; i < sorted.Count; i++)
            {
                var a = sorted[i - 1];
                var b = sorted[i];
                double diff = b.Coordinate - a.Coordinate;
                double cross = OverlapMidpoint(a, b);

                var pa = MakePoint(a.Coordinate, cross, top, isXAxis, plane);
                var pb = MakePoint(b.Coordinate, cross, top, isXAxis, plane);
                var ids = a.AllFeatureIds.Concat(b.AllFeatureIds).Distinct().ToList();

                // Report how many vertices/edges support each side, but never which one is
                // "right" - that depends on design intent this service can't see.
                string axisLabel = isXAxis ? "X" : "Y";
                string label = FormattableString.Invariant(
                    $"{axisLabel}: {a.Coordinate:0.###} (×{a.MemberCount}) vs {b.Coordinate:0.###} (×{b.MemberCount})  Δ{diff:0.###}");

                dimensions.Add(new GuideDimension(GuideKind.NearMiss, pa, pb, diff, label, ids));
            }
        }
    }

    private static void EmitRowDimensions(List<TightCluster> clusters, bool isXAxis, WorkPlane plane, double top, ReferenceGuideOptions opts, List<GuideDimension> dimensions)
    {
        // Any coordinate that carries at least one hole is a "row" for pitch measurement,
        // even if that particular row only has a single hole (e.g. a lone hole that lines
        // up with the row spacing of the others).
        var rows = clusters.Where(c => c.HoleFeatureIds.Count >= 1).OrderBy(c => c.Coordinate).ToList();
        var walls = clusters.Where(c => c.WallFeatureIds.Count >= 1).ToList();

        var steps = new List<double>();
        for (int i = 1; i < rows.Count; i++)
        {
            var a = rows[i - 1];
            var b = rows[i];
            double step = b.Coordinate - a.Coordinate;
            steps.Add(step);

            double cross = OverlapMidpoint(a, b);
            var pa = MakePoint(a.Coordinate, cross, top, isXAxis, plane);
            var pb = MakePoint(b.Coordinate, cross, top, isXAxis, plane);
            dimensions.Add(new GuideDimension(GuideKind.HoleRow, pa, pb, step, step.ToString("0.###", CultureInfo.InvariantCulture),
                a.AllFeatureIds.Concat(b.AllFeatureIds).Distinct().ToList()));
        }

        if (steps.Count >= 2 && steps.All(s => Math.Abs(s - steps[0]) <= opts.AlignTolerance * 10))
        {
            var first = rows[0];
            var last = rows[^1];
            double cross = OverlapMidpoint(first, last);
            var pa = MakePoint(first.Coordinate, cross, top, isXAxis, plane);
            var pb = MakePoint(last.Coordinate, cross, top, isXAxis, plane);
            var allIds = rows.SelectMany(r => r.AllFeatureIds).Distinct().ToList();
            dimensions.Add(new GuideDimension(GuideKind.HoleRow, pa, pb, steps[0],
                FormattableString.Invariant($"{steps.Count}× {steps[0]:0.###}"), allIds));
        }

        if (walls.Count == 0) return;
        foreach (var row in rows)
        {
            var nearestWall = walls.OrderBy(w => Math.Abs(w.Coordinate - row.Coordinate)).First();
            double dist = Math.Abs(nearestWall.Coordinate - row.Coordinate);
            if (dist < 1e-9) continue; // the row itself doubles as a wall coordinate (e.g. an arc-center match)

            double cross = OverlapMidpoint(row, nearestWall);
            var pa = MakePoint(row.Coordinate, cross, top, isXAxis, plane);
            var pb = MakePoint(nearestWall.Coordinate, cross, top, isXAxis, plane);
            dimensions.Add(new GuideDimension(GuideKind.Wall, pa, pb, dist, dist.ToString("0.###", CultureInfo.InvariantCulture),
                row.AllFeatureIds.Concat(nearestWall.AllFeatureIds).Distinct().ToList()));
        }
    }

    private static void EmitSymmetry(List<TightCluster> clusters, bool isXAxis, WorkPlane plane, double top, ReferenceGuideOptions opts, List<GuideLine> lines)
    {
        var coords = clusters;
        if (coords.Count > opts.MaxSymmetryClusters)
        {
            coords = coords.Take(opts.MaxSymmetryClusters).ToList();
        }
        if (coords.Count < 2) return;

        // Group candidate mirror centers (a+b)/2 by a rounded key; a center backed by two or
        // more independent mirrored pairs is a real symmetry axis, not a coincidence.
        var centerGroups = new Dictionary<long, List<(TightCluster A, TightCluster B)>>();
        for (int i = 0; i < coords.Count; i++)
        {
            for (int j = i + 1; j < coords.Count; j++)
            {
                var a = coords[i];
                var b = coords[j];
                if (Math.Abs(a.Coordinate - b.Coordinate) < opts.AlignTolerance) continue;

                double center = (a.Coordinate + b.Coordinate) / 2.0;
                long key = (long)Math.Round(center / opts.SymmetryTolerance);
                if (!centerGroups.TryGetValue(key, out var list))
                {
                    list = new List<(TightCluster, TightCluster)>();
                    centerGroups[key] = list;
                }
                list.Add((a, b));
            }
        }

        double crossMin = coords.Min(c => c.CrossMin) - opts.LineMargin;
        double crossMax = coords.Max(c => c.CrossMax) + opts.LineMargin;

        foreach (var pairs in centerGroups.Values)
        {
            if (pairs.Count < 2) continue;

            double centerCoord = pairs.Average(p => (p.A.Coordinate + p.B.Coordinate) / 2.0);
            var ids = pairs.SelectMany(p => p.A.AllFeatureIds.Concat(p.B.AllFeatureIds)).Distinct().ToList();

            var start = MakePoint(centerCoord, crossMin, top, isXAxis, plane);
            var end = MakePoint(centerCoord, crossMax, top, isXAxis, plane);
            lines.Add(new GuideLine(GuideKind.Symmetry, start, end, centerCoord, ids));
        }
    }

    private static double OverlapMidpoint(TightCluster a, TightCluster b)
    {
        double lo = Math.Max(a.CrossMin, b.CrossMin);
        double hi = Math.Min(a.CrossMax, b.CrossMax);
        if (lo <= hi) return (lo + hi) / 2.0;
        return (a.CrossMax + b.CrossMin) / 2.0; // spans don't overlap: split the gap
    }

    private static Point3D MakePoint(double coord, double cross, double top, bool isXAxis, WorkPlane plane) => isXAxis
        ? plane.ToWorldSpace(new Point3D(coord, cross, top))
        : plane.ToWorldSpace(new Point3D(cross, coord, top));
}
