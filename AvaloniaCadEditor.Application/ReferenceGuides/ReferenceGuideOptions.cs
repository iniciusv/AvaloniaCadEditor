namespace AvaloniaCadEditor.Application.ReferenceGuides;

/// <summary>
/// Tolerances driving <see cref="IReferenceGuideService"/>. All distances are in scene units.
/// </summary>
public sealed class ReferenceGuideOptions
{
    /// <summary>Two coordinates within this distance are considered "the same" (a real alignment).</summary>
    public double AlignTolerance { get; init; } = 1e-3;

    /// <summary>
    /// Two coordinates farther apart than <see cref="AlignTolerance"/> but within this distance are
    /// flagged as a near-miss (likely modeling mistake) instead of being silently ignored.
    /// </summary>
    public double NearMissTolerance { get; init; } = 0.5;

    /// <summary>How close a candidate mirror center has to be to an existing one to be merged with it.</summary>
    public double SymmetryTolerance { get; init; } = 1e-3;

    /// <summary>A profile edge is considered aligned to an axis when |dx or dy| / length is below this ratio.</summary>
    public double EdgeAngleTolerance { get; init; } = 0.03;

    /// <summary>Relative tolerance (fraction of radius) used when matching consecutive arc circumcenters.</summary>
    public double ArcCenterTolerance { get; init; } = 0.08;

    /// <summary>Relative tolerance used when comparing consecutive arc radii.</summary>
    public double ArcRadiusTolerance { get; init; } = 0.1;

    /// <summary>Minimum number of consecutive vertices (as circumcenter triples) required to call something an arc.</summary>
    public int MinArcRunLength { get; init; } = 3;

    /// <summary>World-unit margin added past the outermost member when drawing a guide line's extent.</summary>
    public double LineMargin { get; init; } = 2.0;

    /// <summary>Safety cap on the number of holes analyzed, to keep worst-case cost bounded.</summary>
    public int MaxHoles { get; init; } = 2000;

    /// <summary>Safety cap on the number of distinct coordinate clusters considered for symmetry per axis.</summary>
    public int MaxSymmetryClusters { get; init; } = 200;
}
