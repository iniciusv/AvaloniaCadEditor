using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.ReferenceGuides;

/// <summary>
/// What kind of alignment a guide line or dimension represents.
/// Kept as a single enum (rather than separate ones per model) so the renderer
/// can pick a color/style for both lines and dimensions from the same value.
/// </summary>
public enum GuideKind
{
    /// <summary>Two or more holes share the same coordinate along an axis.</summary>
    HoleRow,

    /// <summary>A straight wall/profile edge is aligned to an axis.</summary>
    Wall,

    /// <summary>The center of a detected circular arc on a profile boundary.</summary>
    ArcCenter,

    /// <summary>A line of symmetry inferred from mirrored pairs of holes/walls.</summary>
    Symmetry,

    /// <summary>Two coordinates are close but not equal - a likely modeling mistake.</summary>
    NearMiss
}

/// <summary>
/// A single dashed reference line drawn in world space, spanning from <see cref="Start"/>
/// to <see cref="End"/> at a fixed coordinate along one axis of the analysis plane.
/// <see cref="SourceFeatureIds"/> are the primitives involved; <see cref="OwnerIds"/> the top-level
/// scene features that contain them (a part instance, for a hole inside it), used to highlight the
/// guides of the selected feature.
/// </summary>
public sealed record GuideLine(
    GuideKind Kind,
    Point3D Start,
    Point3D End,
    double Coordinate,
    IReadOnlyList<Guid> SourceFeatureIds,
    IReadOnlyList<Guid>? OwnerIds = null);

/// <summary>
/// A distance/label to render between two world points, e.g. the step between two
/// consecutive hole rows, or the two conflicting values of a near-miss.
/// </summary>
public sealed record GuideDimension(
    GuideKind Kind,
    Point3D From,
    Point3D To,
    double Value,
    string Label,
    IReadOnlyList<Guid> SourceFeatureIds,
    IReadOnlyList<Guid>? OwnerIds = null);

/// <summary>
/// The full result of one reference-guide analysis pass over a scene.
/// Immutable: a new instance is produced by each analysis, the previous one can keep
/// being rendered while a re-analysis runs in the background.
/// </summary>
public sealed record ReferenceGuideSet(
    IReadOnlyList<GuideLine> Lines,
    IReadOnlyList<GuideDimension> Dimensions)
{
    public static readonly ReferenceGuideSet Empty = new(Array.Empty<GuideLine>(), Array.Empty<GuideDimension>());
}
