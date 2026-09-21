using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public interface IOSnapService
{
    bool IsEnabled { get; set; }
    
    /// <summary>
    /// Calcula a melhor coordenada de snap dado o cursor atual e as entidades presentes no sketch.
    /// Se nenhum snap for encontrado dentro da tolerância, retorna o próprio cursor.
    /// </summary>
    Point3D GetSnappedPoint(Sketch sketch, Point3D cursor, double snapTolerance, AvaloniaCadEditor.Domain.Mesh.TriangleMesh? solidMesh = null);
}
