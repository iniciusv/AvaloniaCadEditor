using System.Collections.Generic;
using System.Threading.Tasks;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Application.Interfaces;

public class ReconstructionResult
{
    public IFeature RootFeature { get; set; } = null!;
    public int OperationCount { get; set; }
    public double ErrorMargin { get; set; }
}

public interface IReconstructionAlgorithm
{
    string Name { get; }
    Task<ReconstructionResult?> ReconstructAsync(TriangleMesh mesh, double toleranceMultiplier = 1.0);
}

/// <summary>
/// Algoritmo que pode propor várias hipóteses de reconstrução na mesma passada
/// (por exemplo, uma por eixo de varredura). O <see cref="IShapeReconstructionService"/>
/// avalia todas por IoU volumétrico e escolhe a melhor.
/// </summary>
public interface IMultiCandidateReconstructionAlgorithm : IReconstructionAlgorithm
{
    Task<IReadOnlyList<ReconstructionResult>> ReconstructCandidatesAsync(TriangleMesh mesh, double toleranceMultiplier = 1.0);
}
