using System.Collections.Generic;
using System.Threading;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ReferenceGuides;

namespace AvaloniaCadEditor.Application.ReferenceGuides;

/// <summary>
/// Analyzes a scene's features and infers alignment/reference guides: rows of holes sharing an
/// axis coordinate, walls aligned to an axis, arc centers, symmetry lines and near-miss coordinates.
/// Pure geometry - no rendering, no UI, no scene mutation - so it is cheap to call from a background
/// thread and safe to unit test with plain feature objects.
/// </summary>
public interface IReferenceGuideService
{
    ReferenceGuideSet Analyze(
        IReadOnlyList<IFeature> features,
        ReferenceGuideOptions? options = null,
        CancellationToken cancellationToken = default);
}
