using System.Threading.Tasks;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Application.Interfaces;

public interface IShapeReconstructionService
{
    Task<IFeature?> ReconstructMeshAsync(TriangleMesh mesh, string? preferredAlgorithmName = null);
}
