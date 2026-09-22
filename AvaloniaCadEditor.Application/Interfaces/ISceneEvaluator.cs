using System.Collections.Generic;
using System.Threading.Tasks;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Application.Interfaces;

public record SceneEvaluationResult(
    IReadOnlyList<IFeature> Features,
    bool Success,
    string? ErrorMessage = null
);

public interface ISceneEvaluator
{
    Task<SceneEvaluationResult> EvaluateAsync(string code);
}
