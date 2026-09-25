using System.Threading.Tasks;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Application.Interfaces;

public interface IStlService
{
    Task<TriangleMesh> ImportAsync(string filePath);
    Task ExportAsync(TriangleMesh mesh, string filePath, bool asBinary = true);
}
