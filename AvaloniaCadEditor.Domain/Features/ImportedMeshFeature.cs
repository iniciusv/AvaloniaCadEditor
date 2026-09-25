using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Domain.Features;

public class ImportedMeshFeature : FeatureBase
{
    public TriangleMesh Mesh { get; }
    
    public ImportedMeshFeature(TriangleMesh mesh, string name = "Malha Importada")
    {
        Mesh = mesh;
        Name = name;
    }
}
