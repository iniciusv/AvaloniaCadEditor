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
